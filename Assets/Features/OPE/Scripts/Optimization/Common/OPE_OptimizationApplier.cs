using System;
using System.Collections.Generic;
using UnityEngine;
using Core.Logging;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace OPE
{
    /// <summary>
    /// Python 最適化スクリプト等で算出された最新の最適化パラメータ (OPE_OptimizationResult) を、
    /// シーン内の PCDOcclusionPipelineController およびベンチマーク用 MethodRegistry へ反映・置換するクラス。
    /// </summary>
    [AppLoggable("OPE (OptApplier)")]
    public static class OPE_OptimizationApplier
    {
        public const string Tag = "OptApplier";

        /// <summary>
        /// 最適化パラメータをパイプラインコントローラーおよびベンチマークレジストリに一括適用します。
        /// </summary>
        public static bool ApplyAll(
            PCDOcclusionPipelineController pipeline,
            OPE_OptimizationResult result,
            PCDRendererFeature.PCD_OcclusionEvaluationMode targetMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.PatternLUT256)
        {
            if (result == null)
            {
                Debug.LogWarning("[OPE_OptimizationApplier] 適用対象の最適化結果が null です。");
                return false;
            }

            bool pipelineOk = ApplyToPipeline(pipeline, result, targetMode);
            ApplyToBenchmarkRegistry(result);

            return pipelineOk;
        }

        /// <summary>
        /// PCDOcclusionPipelineController に最適化パラメータを適用します。
        /// </summary>
        public static bool ApplyToPipeline(
            PCDOcclusionPipelineController pipeline,
            OPE_OptimizationResult result,
            PCDRendererFeature.PCD_OcclusionEvaluationMode targetMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.PatternLUT256)
        {
            if (pipeline == null)
            {
                Debug.LogWarning("[OPE_OptimizationApplier] 対象の PCDOcclusionPipelineController が見つかりません。");
                return false;
            }

            if (result == null) return false;

#if UNITY_EDITOR
            Undo.RecordObject(pipeline, "Apply Optimized Occlusion Parameters");
#endif

            // Mode Average パラメータ
            if (result.averageMeanIoU > 0f && result.averageBestTAve > 0f)
            {
                pipeline.T_ave = result.averageBestTAve;
            }

            // Mode 8 パラメータ
            pipeline.minOccludedSectorsMode8 = result.mode8ThresholdR;

            // Mode 20 パラメータ
            pipeline.minOccludedSectorsMode20 = result.mode20ThresholdR;
            pipeline.maxConsecutiveEmptySectors = result.mode20MaxConsecutiveEmptyL;

            // Mode 36 / 256 / Average パラメータ
            if (targetMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.RotationLUT36)
            {
                pipeline.customPatternLutHex = result.mode36Hex;
                pipeline.evaluationMode = targetMode;
            }
            else if (targetMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.PatternLUT256)
            {
                pipeline.customPatternLutHex = result.mode256Hex;
                pipeline.evaluationMode = targetMode;
            }
            else if (targetMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold)
            {
                pipeline.evaluationMode = targetMode;
            }
            else if (targetMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorConsecutiveZeros)
            {
                pipeline.evaluationMode = targetMode;
            }
            else if (targetMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.Average)
            {
                pipeline.evaluationMode = targetMode;
            }

            ApplyDensityLutEntries(pipeline, result, targetMode);
            pipeline.MarkLutDirty();

#if UNITY_EDITOR
            EditorUtility.SetDirty(pipeline);
#endif

            AppLogger.Log(pipeline, $"=== [OPE_OptimizationApplier] パイプラインコントローラーに最適化値を適用しました ===", Tag);
            AppLogger.Log(pipeline, $"  EvaluationMode: {pipeline.evaluationMode}", Tag);
            AppLogger.Log(pipeline, $"  Mode 8 閾値 R: {pipeline.minOccludedSectorsMode8}", Tag);
            AppLogger.Log(pipeline, $"  Mode 20 閾値 R: {pipeline.minOccludedSectorsMode20}, L: {pipeline.maxConsecutiveEmptySectors}", Tag);
            AppLogger.Log(pipeline, $"  Custom LUT HEX: {(string.IsNullOrEmpty(pipeline.customPatternLutHex) ? "(default)" : pipeline.customPatternLutHex.Substring(0, Mathf.Min(16, pipeline.customPatternLutHex.Length)) + "...")}", Tag);
            AppLogger.Log(pipeline, $"  Density-Adaptive LUT: {(pipeline.useDensityAdaptiveLut ? "ON" : "OFF")} ({(pipeline.densityLutEntries != null ? pipeline.densityLutEntries.Count : 0)} bins)", Tag);

            return true;
        }

        /// <summary>
        /// 最適化結果の <c>densityRules</c> を <see cref="PCDOcclusionPipelineController.densityLutEntries"/> へ変換して反映します。
        /// </summary>
        private static void ApplyDensityLutEntries(
            PCDOcclusionPipelineController pipeline,
            OPE_OptimizationResult result,
            PCDRendererFeature.PCD_OcclusionEvaluationMode targetMode)
        {
            if (pipeline == null || result?.densityRules == null || result.densityRules.Count == 0)
            {
                return;
            }

            bool useMode36 = targetMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.RotationLUT36;
            var validRules = new List<OPE_DensityRuleEntry>(result.densityRules.Count);
            for (int i = 0; i < result.densityRules.Count; i++)
            {
                var rule = result.densityRules[i];
                if (rule == null) continue;
                string hex = useMode36 ? rule.mode36Hex : rule.mode256Hex;
                if (string.IsNullOrEmpty(hex)) continue;
                validRules.Add(rule);
            }

            if (validRules.Count == 0)
            {
                return;
            }

            validRules.Sort((a, b) => a.density.CompareTo(b.density));

            int count = Math.Min(validRules.Count, PCDOcclusionPipelineController.MAX_DENSITY_LUT_BINS);
            var existingEntries = pipeline.densityLutEntries;
            bool reuseExistingThresholds = existingEntries != null && existingEntries.Count == count;
            var newEntries = new List<PCDOcclusionPipelineController.PCD_DensityLutEntry>(count);

            const float minAutoScreenDensity = 0.08f;
            for (int i = 0; i < count; i++)
            {
                var rule = validRules[i];
                float maxScreenDensity;
                if (reuseExistingThresholds && existingEntries[i] != null)
                {
                    maxScreenDensity = existingEntries[i].maxScreenDensity;
                }
                else if (count == 1 || i == count - 1)
                {
                    maxScreenDensity = 1.0f;
                }
                else
                {
                    // 既存の 0.08 -> 0.28 -> 1.00 と整合する対数ステップで上限を割り当てる
                    float t = (float)i / (count - 1);
                    maxScreenDensity = Mathf.Round(minAutoScreenDensity * Mathf.Pow(1.0f / minAutoScreenDensity, t) * 100f) * 0.01f;
                }

                newEntries.Add(new PCDOcclusionPipelineController.PCD_DensityLutEntry
                {
                    label = $"Density {rule.density:0.##} pts/mm²",
                    sourcePhysicalDensity = rule.density,
                    maxScreenDensity = maxScreenDensity,
                    patternLutHex = useMode36 ? rule.mode36Hex : rule.mode256Hex
                });
            }

            pipeline.densityLutEntries = newEntries;
        }

        /// <summary>
        /// ベンチマーク評価用の MethodRegistry に最適化パラメータを設定します。
        /// </summary>
        public static void ApplyToBenchmarkRegistry(OPE_OptimizationResult result)
        {
            OPE_BenchmarkMethodRegistry.ActiveOptimizedParameters = result;
            if (result != null)
            {
                Debug.Log($"[OPE_OptimizationApplier] OPE_BenchmarkMethodRegistry に最新最適化パラメータ (Mode 8: R={result.mode8ThresholdR}, Mode 20: R={result.mode20ThresholdR}/L={result.mode20MaxConsecutiveEmptyL}, Mode 36, Mode 256) を設定しました。");
            }
            else
            {
                Debug.Log("[OPE_OptimizationApplier] OPE_BenchmarkMethodRegistry の動的最適化設定をリセットしました。");
            }
        }
    }
}
