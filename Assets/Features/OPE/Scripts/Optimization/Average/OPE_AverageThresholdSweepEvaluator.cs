using System;
using System.Collections;
using System.Collections.Generic;
using Core.Logging;
using UnityEngine;

namespace OPE
{
    /// <summary>
    /// Average 手法 T_ave スイープ測定の結果データモデル。
    /// </summary>
    public class OPE_AverageThresholdSweepResult
    {
        public float[] tValues;
        public float[,] presetIoUMatrix;
        public float[,] presetOverOccMatrix;
        public float[,] presetUnderOccMatrix;
        public float[] overallMeanIoU;
        public float[] overallMeanOverOcc;
        public float[] overallMeanUnderOcc;
        public float bestTAve;
        public float bestMeanIoU;
        public int bestTIndex;
    }

    /// <summary>
    /// メモリ内レンダリングと IoU 高速計算を用いて、全プリセットおよび複数密度 (sweepDensities) に対する
    /// T_ave スイープ評価を行う Evaluator クラス。
    /// </summary>
    public static class OPE_AverageThresholdSweepEvaluator
    {
        private const string Tag = OPE_AverageThresholdOptimizationRunner.Tag;

        /// <summary>
        /// 全プリセットおよび各密度に対して各 T_ave の IoU をメモリ上で高速計算し、最適値を導出するコルーチン。
        /// </summary>
        public static IEnumerator EvaluateSweepRoutine(
            OPE_StereoEvaluationController controller,
            List<OPE_PresetPoseManager.PresetEntry> presets,
            float[] tValues,
            Action<string, float> onProgress,
            Action<OPE_AverageThresholdSweepResult> onCompleted)
        {
            if (controller == null || presets == null || presets.Count == 0 || tValues == null || tValues.Length == 0)
            {
                yield break;
            }

            int stepCount = tValues.Length;
            float[,] presetIoUMatrix = new float[presets.Count, stepCount];
            float[,] presetOverOccMatrix = new float[presets.Count, stepCount];
            float[,] presetUnderOccMatrix = new float[presets.Count, stepCount];

            float[] densities = (controller.sweepDensities != null && controller.sweepDensities.Length > 0)
                ? controller.sweepDensities
                : new float[] { 0.25f, 1.0f, 4.0f };

            float originalDensity = controller.dummyPointCloudProvider != null
                ? controller.dummyPointCloudProvider.densityValue
                : 1.0f;

            AppLogger.Log(controller, $"[TAveOpt] スイープ開始: プリセット数={presets.Count}, 評価密度=[{string.Join(", ", densities)}], T_aveステップ数={stepCount}", Tag);

            try
            {
                for (int pi = 0; pi < presets.Count; pi++)
                {
                    var preset = presets[pi];
                    AppLogger.Log(controller, $"--- [{pi + 1}/{presets.Count}] プリセット '{preset.caseName}' 計測開始 ---", Tag);

                    // 1. プリセット適用
                    bool applyOk = OPE_PresetPoseManager.ApplyPresetToScene(controller, preset.jsonPath);
                    if (!applyOk)
                    {
                        AppLogger.LogWarning(controller, $"プリセット '{preset.caseName}' の適用に失敗したためスキップします。", Tag);
                        continue;
                    }

                    if (controller.dummyPointCloudProvider != null) controller.dummyPointCloudProvider.ForceUpdateSampling();
                    if (PCDRendererFeature.Instance != null) PCDRendererFeature.Instance.MarkPointCloudDataDirty();

                    for (int f = 0; f < 5; f++) yield return null;

                    // 2. 仮想物体を Unlit White に設定
                    var voBackup = controller.MaterialSwapper.SetVirtualObjectUnlitWhiteState(
                        controller.virtualObject, controller.renderVirtualObjectAsUnlitWhite);

                    Texture2D gtLeftTex = null;
                    Texture2D gtRightTex = null;

                    try
                    {
                        // 3. 理想メッシュによる真値 (GT) をメモリ上にキャプチャ (点群密度に依存しないためプリセット毎に1回)
                        var swapper = controller.MaterialSwapper;
                        var gtBackup = swapper.SetGroundTruthState(
                            controller.groundTruthObject,
                            controller.groundTruthCaptureLayer,
                            controller.renderGroundTruthAsBlack);

                        if (controller.pointCloudObject != null) controller.pointCloudObject.SetActive(false);

                        for (int f = 0; f < 4; f++) yield return null;
                        yield return new WaitForEndOfFrame();

                        gtLeftTex = OPE_ScreenCaptureUtil.CaptureCameraTexture(controller.leftEyeCamera, controller.applySRGBConversion);
                        gtRightTex = OPE_ScreenCaptureUtil.CaptureCameraTexture(controller.rightEyeCamera, controller.applySRGBConversion);

                        swapper.RestoreGroundTruthState(gtBackup);
                        if (controller.pointCloudObject != null) controller.pointCloudObject.SetActive(true);
                        if (PCDRendererFeature.Instance != null) PCDRendererFeature.Instance.MarkPointCloudDataDirty();

                        for (int f = 0; f < 3; f++) yield return null;
                        yield return new WaitForEndOfFrame();

                        if (gtLeftTex == null || gtRightTex == null)
                        {
                            AppLogger.LogError(controller, $"GT テクスチャ取得失敗: {preset.caseName}", Tag);
                            continue;
                        }

                        Color32[] gtLeftPixels = gtLeftTex.GetPixels32();
                        Color32[] gtRightPixels = gtRightTex.GetPixels32();
                        const byte VoThreshold = 128; // Python UnionMaskMetrics threshold=128 完全一致

                        // プリセット内の全密度累積バッファ [ti, metric]
                        float[,] accumPreset = new float[stepCount, 3];

                        // 4. 複数密度ループ (0.25, 1.0, 4.0 等)
                        for (int di = 0; di < densities.Length; di++)
                        {
                            float currentDensity = densities[di];
                            if (controller.dummyPointCloudProvider != null)
                            {
                                controller.dummyPointCloudProvider.densityUnit = controller.densityUnit;
                                controller.dummyPointCloudProvider.densityValue = currentDensity;
                                controller.dummyPointCloudProvider.ForceUpdateSampling();
                            }
                            if (PCDRendererFeature.Instance != null) PCDRendererFeature.Instance.MarkPointCloudDataDirty();

                            for (int f = 0; f < 3; f++) yield return null;
                            yield return new WaitForEndOfFrame();

                            // 5. 各 T_ave に対する高速メモリ内 IoU 計算
                            for (int ti = 0; ti < stepCount; ti++)
                            {
                                float curT = tValues[ti];
                                controller.occlusionPipelineController.T_ave = curT;
                                controller.occlusionPipelineController.occlusionThreshold = curT;
                                controller.occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.Average;

                                if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
                                {
                                    PCDRendererFeature.Instance.settings.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.Average;
                                    PCDRendererFeature.Instance.settings.occlusionThreshold_T_ave = curT;
                                    PCDRendererFeature.Instance.MarkPointCloudDataDirty();
                                }

                                yield return null;
                                yield return new WaitForEndOfFrame();

                                var testLeftTex = OPE_ScreenCaptureUtil.CaptureCameraTexture(controller.leftEyeCamera, controller.applySRGBConversion);
                                var testRightTex = OPE_ScreenCaptureUtil.CaptureCameraTexture(controller.rightEyeCamera, controller.applySRGBConversion);

                                if (testLeftTex != null && testRightTex != null)
                                {
                                    Color32[] testLeftPixels = testLeftTex.GetPixels32();
                                    Color32[] testRightPixels = testRightTex.GetPixels32();

                                    var leftMetrics = OPE_IoUCalculator.Calculate(gtLeftPixels, testLeftPixels, VoThreshold);
                                    var rightMetrics = OPE_IoUCalculator.Calculate(gtRightPixels, testRightPixels, VoThreshold);
                                    var stereo = OPE_IoUCalculator.StereoMetricsResult.FromStereo(leftMetrics, rightMetrics);

                                    accumPreset[ti, 0] += stereo.meanIoU;
                                    accumPreset[ti, 1] += (leftMetrics.overOcclusionRatio + rightMetrics.overOcclusionRatio) * 0.5f;
                                    accumPreset[ti, 2] += (leftMetrics.underOcclusionRatio + rightMetrics.underOcclusionRatio) * 0.5f;

                                    UnityEngine.Object.DestroyImmediate(testLeftTex);
                                    UnityEngine.Object.DestroyImmediate(testRightTex);
                                }

                                if (ti % 25 == 0 || ti == stepCount - 1)
                                {
                                    float currentProgress = ((float)pi + ((float)di + (float)ti / stepCount) / densities.Length) / presets.Count;
                                    string stepStatus = $"[{pi + 1}/{presets.Count}] {preset.caseName} (Density={currentDensity:F2}): T_ave={curT:F2}";
                                    onProgress?.Invoke(stepStatus, currentProgress);
                                }
                            }
                        }

                        // プリセット内の全密度平均を格納
                        for (int ti = 0; ti < stepCount; ti++)
                        {
                            presetIoUMatrix[pi, ti] = accumPreset[ti, 0] / densities.Length;
                            presetOverOccMatrix[pi, ti] = accumPreset[ti, 1] / densities.Length;
                            presetUnderOccMatrix[pi, ti] = accumPreset[ti, 2] / densities.Length;
                        }
                    }
                    finally
                    {
                        if (gtLeftTex != null) UnityEngine.Object.DestroyImmediate(gtLeftTex);
                        if (gtRightTex != null) UnityEngine.Object.DestroyImmediate(gtRightTex);
                        controller.MaterialSwapper.RestoreVirtualObjectState(voBackup);
                    }
                }
            }
            finally
            {
                // 点群密度の復元
                if (controller.dummyPointCloudProvider != null)
                {
                    controller.dummyPointCloudProvider.densityValue = originalDensity;
                    controller.dummyPointCloudProvider.ForceUpdateSampling();
                }
                if (PCDRendererFeature.Instance != null) PCDRendererFeature.Instance.MarkPointCloudDataDirty();
            }

            // 6. 全配置における各 T_ave の総合平均 IoU を集計
            float[] overallMeanIoU = new float[stepCount];
            float[] overallMeanOverOcc = new float[stepCount];
            float[] overallMeanUnderOcc = new float[stepCount];

            float bestMeanIoU = -1f;
            int bestTIndex = 0;

            for (int ti = 0; ti < stepCount; ti++)
            {
                float sumIoU = 0f;
                float sumOver = 0f;
                float sumUnder = 0f;

                for (int pi = 0; pi < presets.Count; pi++)
                {
                    sumIoU += presetIoUMatrix[pi, ti];
                    sumOver += presetOverOccMatrix[pi, ti];
                    sumUnder += presetUnderOccMatrix[pi, ti];
                }

                overallMeanIoU[ti] = sumIoU / presets.Count;
                overallMeanOverOcc[ti] = sumOver / presets.Count;
                overallMeanUnderOcc[ti] = sumUnder / presets.Count;

                if (overallMeanIoU[ti] > bestMeanIoU)
                {
                    bestMeanIoU = overallMeanIoU[ti];
                    bestTIndex = ti;
                }
            }

            AppLogger.Log(controller, $"[TAveOpt] 最適化完了: 最適 T_ave = {tValues[bestTIndex]:F2}, 最高全密度平均 IoU = {bestMeanIoU:P2}", Tag);

            var result = new OPE_AverageThresholdSweepResult
            {
                tValues = tValues,
                presetIoUMatrix = presetIoUMatrix,
                presetOverOccMatrix = presetOverOccMatrix,
                presetUnderOccMatrix = presetUnderOccMatrix,
                overallMeanIoU = overallMeanIoU,
                overallMeanOverOcc = overallMeanOverOcc,
                overallMeanUnderOcc = overallMeanUnderOcc,
                bestTAve = tValues[bestTIndex],
                bestMeanIoU = bestMeanIoU,
                bestTIndex = bestTIndex
            };

            onCompleted?.Invoke(result);
        }
    }
}

