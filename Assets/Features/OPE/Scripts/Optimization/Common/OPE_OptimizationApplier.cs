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

#if UNITY_EDITOR
            EditorUtility.SetDirty(pipeline);
#endif

            AppLogger.Log(pipeline, $"=== [OPE_OptimizationApplier] パイプラインコントローラーに最適化値を適用しました ===", Tag);
            AppLogger.Log(pipeline, $"  EvaluationMode: {pipeline.evaluationMode}", Tag);
            AppLogger.Log(pipeline, $"  Mode 8 閾値 R: {pipeline.minOccludedSectorsMode8}", Tag);
            AppLogger.Log(pipeline, $"  Mode 20 閾値 R: {pipeline.minOccludedSectorsMode20}, L: {pipeline.maxConsecutiveEmptySectors}", Tag);
            AppLogger.Log(pipeline, $"  Custom LUT HEX: {(string.IsNullOrEmpty(pipeline.customPatternLutHex) ? "(default)" : pipeline.customPatternLutHex.Substring(0, Mathf.Min(16, pipeline.customPatternLutHex.Length)) + "...")}", Tag);

            return true;
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
