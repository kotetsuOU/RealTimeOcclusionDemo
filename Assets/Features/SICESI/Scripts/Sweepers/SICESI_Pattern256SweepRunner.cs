using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// SICE SI 評価実験において、256 通りのセクター占有パターン単独二値マスクの
    /// 全網羅スイープ撮影・保存を担当するランナークラス。
    /// </summary>
    public class SICESI_Pattern256SweepRunner
    {
        private readonly SICESI_StereoEvaluationController _controller;
        private readonly SICESI_SectorMaskCollector _collector;

        public SICESI_Pattern256SweepRunner(SICESI_StereoEvaluationController controller, SICESI_SectorMaskCollector collector)
        {
            _controller = controller;
            _collector = collector;
        }

        public IEnumerator Pattern256MaskSweepRoutine()
        {
            _collector.isCollecting = true;
            SICESI_SectorMaskCollector.IsCollectingActive = true;
            _collector.statusMessage = "Starting 256-Pattern Mask Sweep...";

            string conditionRootDir = _controller.ConditionRootDir;
            string sweepRootDir = Path.Combine(conditionRootDir, "Pattern256MaskSweep");
            Directory.CreateDirectory(sweepRootDir);
            _controller.SaveSceneTransformsJson(sweepRootDir);
            _controller.SaveSceneTransformsJson(conditionRootDir);

            AppLogger.Log("SICESI", $"=== 256占有パターン単独二値マスク 密度スイープ開始: {sweepRootDir} ===");

            int prevDebugPatternId = -1;
            bool prevRecordNeighborCount = false;
            bool prevSoftFade = true;

            if (_controller.occlusionPipelineController != null)
            {
                prevDebugPatternId = _controller.occlusionPipelineController.debugPatternId;
                prevRecordNeighborCount = _controller.occlusionPipelineController.recordNeighborCountMap;
                prevSoftFade = _controller.occlusionPipelineController.enableSoftOcclusionFade;
                _controller.occlusionPipelineController.recordNeighborCountMap = true;
                _controller.occlusionPipelineController.debugPatternId = -1;
                _controller.occlusionPipelineController.enableSoftOcclusionFade = false;
            }
            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                PCDRendererFeature.Instance.settings.recordNeighborCountMap = true;
                PCDRendererFeature.Instance.settings.debugPatternId = -1;
                PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = false;
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
                _collector.statusMessage = "Capturing Ground Truth for 256-Pattern Sweep...";
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
                            PCDRendererFeature.Instance.settings.debugPatternId = -1;
                            PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = false;
                        }
                    }
                    if (_controller.occlusionPipelineController != null)
                    {
                        _controller.occlusionPipelineController.recordNeighborCountMap = true;
                        _controller.occlusionPipelineController.debugPatternId = -1;
                        _controller.occlusionPipelineController.enableSoftOcclusionFade = false;
                    }

                    for (int f = 0; f < _controller.waitFramesAfterDensityChange; f++) yield return null;
                    yield return new WaitForEndOfFrame();

                    // カメラ姿勢固定
                    using (new SICESI_CameraPoseLock(_controller.leftEyeCamera, _controller.rightEyeCamera))
                    {
                        // 1. 通常テスト画像
                        _collector.statusMessage = $"Density {densityStr} | Normal Test Image";
                        SetDebugPatternId(-1);
                        yield return null;
                        yield return new WaitForEndOfFrame();
                        _controller.CaptureStereoViews(targetDir, $"test_{densityStr}");

                        // 2. 256パターン単独二値マスクの順次撮影
                        int[] patterns = _collector.targetPatterns;
                        if (patterns == null || patterns.Length == 0)
                        {
                            patterns = new int[256];
                            for (int p = 0; p < 256; p++) patterns[p] = p;
                        }

                        string masksDir = Path.Combine(targetDir, "masks");
                        Directory.CreateDirectory(masksDir);

                        for (int p = 0; p < patterns.Length; p++)
                        {
                            int pattern = patterns[p];
                            _collector.statusMessage = $"Density {densityStr} | Pattern {pattern}/255 ({p + 1}/{patterns.Length})";

                            SetDebugPatternId(pattern);
                            yield return null;
                            yield return new WaitForEndOfFrame();

                            _controller.CaptureStereoViews(masksDir, $"pattern_{pattern:D3}");
                        }
                    }

                    SetDebugPatternId(-1);
                    yield return null;

                    SaveParamsJson(targetDir, density, _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.occlusionThreshold : 0.1f);
                    AppLogger.Log("SICESI", $"[{d + 1}/{densities.Length}] 密度 {densityStr} の256パターン撮影完了: {targetDir}");
                }

                _collector.statusMessage = "All 256-Pattern Mask Sweeps Completed!";
                AppLogger.Log("SICESI", $"=== 全密度の256パターン撮影が完了しました! 保存先: {sweepRootDir} ===");
            }
            finally
            {
                _controller.RestoreVirtualObjectState(voBackup);
                SetDebugPatternId(prevDebugPatternId);
                if (_controller.occlusionPipelineController != null)
                {
                    _controller.occlusionPipelineController.recordNeighborCountMap = prevRecordNeighborCount;
                    _controller.occlusionPipelineController.enableSoftOcclusionFade = prevSoftFade;
                }
                if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
                {
                    PCDRendererFeature.Instance.settings.recordNeighborCountMap = prevRecordNeighborCount;
                    PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = prevSoftFade;
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
