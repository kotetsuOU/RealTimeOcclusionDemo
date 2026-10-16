using System;
using System.Collections;
using System.Collections.Generic;
using Core.Logging;
using UnityEngine;

namespace SICESI
{
    /// <summary>
    /// Average 手法 T_ave スイープ測定の結果データモデル。
    /// </summary>
    public class SICESI_AverageThresholdSweepResult
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
    /// メモリ内レンダリングと IoU 高速計算を用いて、全プリセットに対する T_ave スイープ評価を行う Evaluator クラス。
    /// </summary>
    public static class SICESI_AverageThresholdSweepEvaluator
    {
        private const string Tag = SICESI_AverageThresholdOptimizationRunner.Tag;

        /// <summary>
        /// 全プリセットに対して各 T_ave の IoU をメモリ上で高速計算し、最適値を導出するコルーチン。
        /// </summary>
        public static IEnumerator EvaluateSweepRoutine(
            SICESI_StereoEvaluationController controller,
            List<SICESI_PresetPoseManager.PresetEntry> presets,
            float[] tValues,
            Action<string, float> onProgress,
            Action<SICESI_AverageThresholdSweepResult> onCompleted)
        {
            if (controller == null || presets == null || presets.Count == 0 || tValues == null || tValues.Length == 0)
            {
                yield break;
            }

            int stepCount = tValues.Length;
            float[,] presetIoUMatrix = new float[presets.Count, stepCount];
            float[,] presetOverOccMatrix = new float[presets.Count, stepCount];
            float[,] presetUnderOccMatrix = new float[presets.Count, stepCount];

            for (int pi = 0; pi < presets.Count; pi++)
            {
                var preset = presets[pi];
                float currentProgress = (float)pi / presets.Count;
                string status = $"Evaluating Preset {pi + 1}/{presets.Count} [{preset.caseName}]...";
                onProgress?.Invoke(status, currentProgress);

                AppLogger.Log(controller, $"--- [{pi + 1}/{presets.Count}] プリセット '{preset.caseName}' 計測開始 ---", Tag);

                // 1. プリセット適用
                bool applyOk = SICESI_PresetPoseManager.ApplyPresetToScene(controller, preset.jsonPath);
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
                    // 3. 最終出力 GT をメモリ上に直接取得 (ディスク保存なし)
                    var swapper = controller.MaterialSwapper;
                    var gtBackup = swapper.SetGroundTruthState(
                        controller.groundTruthObject,
                        controller.groundTruthCaptureLayer,
                        controller.renderGroundTruthAsBlack);

                    if (controller.pointCloudObject != null) controller.pointCloudObject.SetActive(false);

                    for (int f = 0; f < 4; f++) yield return null;
                    yield return new WaitForEndOfFrame();

                    gtLeftTex = SICESI_ScreenCaptureUtil.CaptureCameraTexture(controller.leftEyeCamera, controller.applySRGBConversion);
                    gtRightTex = SICESI_ScreenCaptureUtil.CaptureCameraTexture(controller.rightEyeCamera, controller.applySRGBConversion);

                    swapper.RestoreGroundTruthState(gtBackup);
                    if (controller.pointCloudObject != null) controller.pointCloudObject.SetActive(true);
                    if (PCDRendererFeature.Instance != null) PCDRendererFeature.Instance.MarkPointCloudDataDirty();

                    // 点群描画の再開待機
                    for (int f = 0; f < 3; f++) yield return null;
                    yield return new WaitForEndOfFrame();

                    if (gtLeftTex == null || gtRightTex == null)
                    {
                        AppLogger.LogError(controller, $"GT テクスチャ取得失敗: {preset.caseName}", Tag);
                        continue;
                    }

                    // GT ピクセル配列のキャッシュ (高速計算のため)
                    Color32[] gtLeftPixels = gtLeftTex.GetPixels32();
                    Color32[] gtRightPixels = gtRightTex.GetPixels32();

                    const byte VoThreshold = 128; // Python UnionMaskMetrics threshold=128 完全一致

                    // 4. 各 T_ave に対する高速メモリ内 IoU 計算
                    for (int ti = 0; ti < stepCount; ti++)
                    {
                        float curT = tValues[ti];
                        controller.occlusionPipelineController.T_ave = curT;
                        controller.occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.Average;

                        if (PCDRendererFeature.Instance != null)
                        {
                            PCDRendererFeature.Instance.MarkPointCloudDataDirty();
                        }

                        // シェーダーおよび描画更新待機 (2フレーム待機)
                        yield return null;
                        yield return new WaitForEndOfFrame();

                        var testLeftTex = SICESI_ScreenCaptureUtil.CaptureCameraTexture(controller.leftEyeCamera, controller.applySRGBConversion);
                        var testRightTex = SICESI_ScreenCaptureUtil.CaptureCameraTexture(controller.rightEyeCamera, controller.applySRGBConversion);

                        if (testLeftTex != null && testRightTex != null)
                        {
                            Color32[] testLeftPixels = testLeftTex.GetPixels32();
                            Color32[] testRightPixels = testRightTex.GetPixels32();

                            var leftMetrics = SICESI_IoUCalculator.Calculate(gtLeftPixels, testLeftPixels, VoThreshold);
                            var rightMetrics = SICESI_IoUCalculator.Calculate(gtRightPixels, testRightPixels, VoThreshold);
                            var stereo = SICESI_IoUCalculator.StereoMetricsResult.FromStereo(leftMetrics, rightMetrics);

                            presetIoUMatrix[pi, ti] = stereo.meanIoU;
                            presetOverOccMatrix[pi, ti] = (leftMetrics.overOcclusionRatio + rightMetrics.overOcclusionRatio) * 0.5f;
                            presetUnderOccMatrix[pi, ti] = (leftMetrics.underOcclusionRatio + rightMetrics.underOcclusionRatio) * 0.5f;

                            UnityEngine.Object.DestroyImmediate(testLeftTex);
                            UnityEngine.Object.DestroyImmediate(testRightTex);
                        }

                        if (ti % 20 == 0 || ti == stepCount - 1)
                        {
                            string stepStatus = $"[{pi + 1}/{presets.Count}] {preset.caseName}: T_ave={curT:F2} (IoU: {presetIoUMatrix[pi, ti]:P1})";
                            onProgress?.Invoke(stepStatus, currentProgress + ((float)ti / stepCount) / presets.Count);
                        }
                    }
                }
                finally
                {
                    if (gtLeftTex != null) UnityEngine.Object.DestroyImmediate(gtLeftTex);
                    if (gtRightTex != null) UnityEngine.Object.DestroyImmediate(gtRightTex);
                    controller.MaterialSwapper.RestoreVirtualObjectState(voBackup);
                }
            }

            // 5. 全配置における各 T_ave の平均 IoU を集計
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

            var result = new SICESI_AverageThresholdSweepResult
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
