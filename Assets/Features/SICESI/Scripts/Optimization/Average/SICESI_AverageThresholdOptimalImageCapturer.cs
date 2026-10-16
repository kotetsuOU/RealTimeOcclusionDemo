using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SICESI
{
    /// <summary>
    /// Average 手法 T_ave 最適化スイープ完了後、最適値における画像（GT, 最適テスト画像, 差分マップ）を
    /// 各プリセットごとに撮影・保存する Capturer クラス。
    /// </summary>
    public static class SICESI_AverageThresholdOptimalImageCapturer
    {
        /// <summary>
        /// 最適値 T_ave における画像を各プリセットごとに撮影・保存するコルーチン。
        /// </summary>
        public static IEnumerator CaptureOptimalImagesRoutine(
            SICESI_StereoEvaluationController controller,
            List<SICESI_PresetPoseManager.PresetEntry> presets,
            float bestTAve,
            string baseOutputDir,
            Action<string> onStatusUpdate = null)
        {
            if (controller == null || presets == null || presets.Count == 0) yield break;

            controller.occlusionPipelineController.T_ave = bestTAve;

            for (int pi = 0; pi < presets.Count; pi++)
            {
                var preset = presets[pi];
                string status = $"Capturing Optimal Images for Best T_ave ({bestTAve:F2}) [{pi + 1}/{presets.Count}]: {preset.caseName}";
                onStatusUpdate?.Invoke(status);

                SICESI_PresetPoseManager.ApplyPresetToScene(controller, preset.jsonPath);
                if (controller.dummyPointCloudProvider != null) controller.dummyPointCloudProvider.ForceUpdateSampling();
                if (PCDRendererFeature.Instance != null) PCDRendererFeature.Instance.MarkPointCloudDataDirty();

                for (int f = 0; f < 3; f++) yield return null;

                var voBackup = controller.MaterialSwapper.SetVirtualObjectUnlitWhiteState(
                    controller.virtualObject, controller.renderVirtualObjectAsUnlitWhite);

                try
                {
                    string presetSubDir = Path.Combine(baseOutputDir, preset.caseName);
                    Directory.CreateDirectory(presetSubDir);

                    // 1. GT 撮影
                    string gtDir = Path.Combine(presetSubDir, "GT");
                    yield return SICESI_BenchmarkGroundTruthCapturer.CaptureFinalOutputGroundTruth(controller, gtDir);

                    // 2. 最適 T_ave でのテスト撮影
                    yield return null;
                    yield return new WaitForEndOfFrame();

                    string optDir = Path.Combine(presetSubDir, $"Best_TAve_{bestTAve:F2}");
                    SICESI_ScreenCaptureUtil.CaptureStereoViews(
                        controller.leftEyeCamera, controller.rightEyeCamera, optDir, "test_optimal", controller.applySRGBConversion);

                    yield return new WaitForSeconds(0.1f);

                    // 3. 差分画像の生成・保存
                    var (gtL, _) = SICESI_BenchmarkReporter.LoadStereoTextures(gtDir, "gt");
                    var (optL, _) = SICESI_BenchmarkImageIO.LoadStereoTextures(optDir, "test_optimal");
                    if (gtL != null && optL != null)
                    {
                        SICESI_BenchmarkImageIO.SaveDiffImage(gtL, optL, Path.Combine(optDir, "diff_left.png"));
                        UnityEngine.Object.DestroyImmediate(gtL);
                        UnityEngine.Object.DestroyImmediate(optL);
                    }
                }
                finally
                {
                    controller.MaterialSwapper.RestoreVirtualObjectState(voBackup);
                }
            }
        }
    }
}
