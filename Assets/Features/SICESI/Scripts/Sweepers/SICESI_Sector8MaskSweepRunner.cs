using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// SICE SI 評価実験において、8 セクター占有マスク、直接投影マスク、通常テスト画像、
    /// 統合 8bit パターンマスク、GPU 実遮蔽判定マスクの画面レンダリング画像を
    /// 左右カメラから一括キャプチャ・保存するランナークラス。
    /// </summary>
    public class SICESI_Sector8MaskSweepRunner
    {
        private readonly SICESI_StereoEvaluationController _controller;
        private readonly SICESI_SectorMaskCollector _collector;

        public SICESI_Sector8MaskSweepRunner(SICESI_StereoEvaluationController controller, SICESI_SectorMaskCollector collector)
        {
            _controller = controller;
            _collector = collector;
        }

        public IEnumerator RunRoutine()
        {
            _collector.isCollecting = true;
            SICESI_SectorMaskCollector.IsCollectingActive = true;
            _collector.statusMessage = "Starting 8-Sector Binary Mask Sweep...";

            string conditionRootDir = _controller.ConditionRootDir;
            string sweepRootDir = Path.Combine(conditionRootDir, "Sector8MaskSweep");
            Directory.CreateDirectory(sweepRootDir);
            _controller.SaveSceneTransformsJson(sweepRootDir);
            _controller.SaveSceneTransformsJson(conditionRootDir);

            AppLogger.Log("SICESI", $"=== 8セクター二値マスク 密度スイープ開始: {sweepRootDir} ===");

            float prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            int prevDebugSectorId = -1;
            int prevDebugPatternId = -1;
            bool prevRecordNeighborCount = false;
            bool prevSoftFade = true;
            PCDRendererFeature.PCD_HoleFillingMethod prevHoleFilling = PCDRendererFeature.PCD_HoleFillingMethod.None;

            if (_controller.occlusionPipelineController != null)
            {
                prevDebugSectorId = _controller.occlusionPipelineController.debugSectorId;
                prevDebugPatternId = _controller.occlusionPipelineController.debugPatternId;
                prevRecordNeighborCount = _controller.occlusionPipelineController.recordNeighborCountMap;
                prevSoftFade = _controller.occlusionPipelineController.enableSoftOcclusionFade;
                prevHoleFilling = _controller.occlusionPipelineController.holeFillingMethod;

                _controller.occlusionPipelineController.recordNeighborCountMap = true;
                _controller.occlusionPipelineController.debugSectorId = -1;
                _controller.occlusionPipelineController.debugPatternId = -1;
                _controller.occlusionPipelineController.enableSoftOcclusionFade = false;
                _controller.occlusionPipelineController.holeFillingMethod = PCDRendererFeature.PCD_HoleFillingMethod.None;
            }
            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                PCDRendererFeature.Instance.settings.recordNeighborCountMap = true;
                PCDRendererFeature.Instance.settings.debugSectorId = -1;
                PCDRendererFeature.Instance.settings.debugPatternId = -1;
                PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = false;
                PCDRendererFeature.Instance.settings.holeFillingMethod = PCDRendererFeature.PCD_HoleFillingMethod.None;
            }

            var voBackup = _controller.SetVirtualObjectUnlitWhiteState(true);
            try
            {
                // Step 0: 仮想物体単独シルエット
                _collector.statusMessage = "Capturing Virtual Object Silhouette...";
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(false);
                bool prevGtActive = _controller.groundTruthObject != null && _controller.groundTruthObject.activeSelf;
                if (_controller.groundTruthObject != null) _controller.groundTruthObject.SetActive(false);

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                string gtDir = Path.Combine(sweepRootDir, "GT");
                _controller.CaptureStereoViews(gtDir, "vo_silhouette");

                string commonGtDir = Path.Combine(_controller.outputDirectory, "GT");
                if (commonGtDir != gtDir)
                {
                    _controller.CaptureStereoViews(commonGtDir, "vo_silhouette");
                }

                if (_controller.groundTruthObject != null) _controller.groundTruthObject.SetActive(prevGtActive);
                AppLogger.Log("SICESI", $"[0/2] 仮想物体単独シルエット撮影完了: {gtDir}");

                // Step 1: Ground Truth 撮影
                _collector.statusMessage = "Capturing Ground Truth for Mask Sweep...";
                var backup = _controller.SetGroundTruthState(true);
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(false);

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                _controller.CaptureStereoViews(gtDir, "gt");
                if (commonGtDir != gtDir)
                {
                    _controller.CaptureStereoViews(commonGtDir, "gt");
                }

                _controller.RestoreGroundTruthState(backup);
                AppLogger.Log("SICESI", $"[1/2] Ground Truth 撮影完了: {gtDir}");

                // Step 2: 点群表示 & 密度ループ
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(true);

                float[] densities = _controller.sweepDensities;
                if (densities == null || densities.Length == 0)
                {
                    densities = new float[] { _controller.dummyPointCloudProvider != null ? _controller.dummyPointCloudProvider.densityValue : 4.0f };
                }

                for (int d = 0; d < densities.Length; d++)
                {
                    float density = densities[d];
                    string densityStr = density.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture);
                    string densityFolderName = $"density_{densityStr}pts_mm2";
                    string targetDir = Path.Combine(sweepRootDir, densityFolderName);
                    Directory.CreateDirectory(targetDir);

                    _collector.statusMessage = $"Setting Density ({d + 1}/{densities.Length}): {densityStr} pts/mm2";
                    AppLogger.Log("SICESI", $"[2/2] 密度設定変更 ({d + 1}/{densities.Length}): {densityStr}");

                    if (_controller.dummyPointCloudProvider != null)
                    {
                        _controller.dummyPointCloudProvider.densityUnit = _controller.densityUnit;
                        _controller.dummyPointCloudProvider.densityValue = density;
                        _controller.dummyPointCloudProvider.ForceUpdateSampling();
                    }

                    if (PCDRendererFeature.Instance != null)
                    {
                        PCDRendererFeature.Instance.MarkPointCloudDataDirty();
                        if (PCDRendererFeature.Instance.settings != null)
                        {
                            PCDRendererFeature.Instance.settings.recordNeighborCountMap = true;
                            PCDRendererFeature.Instance.settings.debugSectorId = -1;
                            PCDRendererFeature.Instance.settings.debugPatternId = -1;
                            PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = false;
                        }
                    }
                    if (_controller.occlusionPipelineController != null)
                    {
                        _controller.occlusionPipelineController.recordNeighborCountMap = true;
                        _controller.occlusionPipelineController.debugSectorId = -1;
                        _controller.occlusionPipelineController.debugPatternId = -1;
                        _controller.occlusionPipelineController.enableSoftOcclusionFade = false;
                    }

                    for (int f = 0; f < _controller.waitFramesAfterDensityChange; f++) yield return null;
                    yield return new WaitForEndOfFrame();

                    using (new SICESI_CameraPoseLock(_controller.leftEyeCamera, _controller.rightEyeCamera))
                    {
                        // 0. 直接投影マスク
                        _collector.statusMessage = $"Density {densityStr} | Point Mask (Direct Projection)";
                        SetDebugSectorId(10);
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        _controller.CaptureStereoViews(targetDir, "point_mask", bypassSRGBConversion: true);

                        // 1. 通常テスト画像
                        SetDebugSectorId(-1);
                        SetDebugPatternId(-1);
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        _controller.CaptureStereoViews(targetDir, $"test_{densityStr}");

                        // 2. 8セクター二値マスク
                        for (int s = 0; s < 8; s++)
                        {
                            _collector.statusMessage = $"Density {densityStr} | Sector {s}/7 Binary Mask";
                            SetDebugSectorId(s);
                            yield return null;
                            yield return new WaitForEndOfFrame();
                            _controller.CaptureStereoViews(targetDir, $"sector_{s}_mask");
                        }

                        // 3. 統合 8bit パターンマスク
                        _collector.statusMessage = $"Density {densityStr} | Unified 8-bit Pattern Mask";
                        SetDebugSectorId(8);
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        _controller.CaptureStereoViews(targetDir, "sector_mask", bypassSRGBConversion: true);

                        // 4. GPU 実遮蔽判定マスク
                        _collector.statusMessage = $"Density {densityStr} | GPU Occluded Truth Mask";
                        SetDebugSectorId(9);
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        _controller.CaptureStereoViews(targetDir, "gpu_occluded_mask");
                    }

                    SetDebugSectorId(-1);
                    yield return null;

                    SaveParamsJson(targetDir, density, _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.occlusionThreshold : 0.1f);
                    AppLogger.Log("SICESI", $"[{d + 1}/{densities.Length}] 密度 {densityStr} の統合セクターマスク収集完了: {targetDir}");
                }

                _collector.statusMessage = "All 8-Sector Binary Mask Sweeps Completed!";
                AppLogger.Log("SICESI", $"=== 全密度の8セクター二値マスク収集が完了しました! 保存先: {sweepRootDir} ===");
            }
            finally
            {
                _controller.RestoreVirtualObjectState(voBackup);
                Time.timeScale = prevTimeScale;
                SetDebugSectorId(prevDebugSectorId);
                SetDebugPatternId(prevDebugPatternId);
                if (_controller.occlusionPipelineController != null)
                {
                    _controller.occlusionPipelineController.recordNeighborCountMap = prevRecordNeighborCount;
                    _controller.occlusionPipelineController.enableSoftOcclusionFade = prevSoftFade;
                    _controller.occlusionPipelineController.holeFillingMethod = prevHoleFilling;
                }
                if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
                {
                    PCDRendererFeature.Instance.settings.recordNeighborCountMap = prevRecordNeighborCount;
                    PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = prevSoftFade;
                    PCDRendererFeature.Instance.settings.holeFillingMethod = prevHoleFilling;
                }
                SICESI_SectorMaskCollector.IsCollectingActive = false;
                _collector.isCollecting = false;
            }
        }

        private void SetDebugPatternId(int patternId)
        {
            if (_controller.occlusionPipelineController != null)
            {
                _controller.occlusionPipelineController.debugPatternId = patternId;
            }
            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                PCDRendererFeature.Instance.settings.debugPatternId = patternId;
            }
        }

        private void SetDebugSectorId(int sectorId)
        {
            if (_controller.occlusionPipelineController != null)
            {
                _controller.occlusionPipelineController.debugSectorId = sectorId;
            }
            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                PCDRendererFeature.Instance.settings.debugSectorId = sectorId;
            }
        }

        private void SaveParamsJson(string targetDir, float density, float threshold)
        {
            try
            {
                var pipe = _controller.occlusionPipelineController;
                string jsonMode = pipe != null ? pipe.evaluationMode.ToString() : "SectorConsecutiveZeros";
                int minSec = pipe != null ? pipe.minOccludedSectors : 1;
                int maxZeros = pipe != null ? pipe.maxConsecutiveEmptySectors : 8;

                SICESI_ScreenCaptureUtil.SaveEvaluationParamsJson(
                    targetDir,
                    _controller.conditionName,
                    density,
                    _controller.densityUnit.ToString(),
                    threshold,
                    jsonMode,
                    minSec,
                    maxZeros,
                    _controller.groundTruthObject,
                    _controller.virtualObject
                );
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(_controller, $"[SICESI] JSON保存エラー: {ex.Message}", SICESI_StereoEvaluationController.TagMaskSweep);
            }
        }
    }
}
