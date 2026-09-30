using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// SICE SI 評価実験において、AsyncGPUReadback を利用して全密度の
    /// 実測セクター占有マスクデータ（RAW / CSV）を一括取得・エクスポートするランナークラス。
    /// </summary>
    public class SICESI_SectorMaskDensityRunner
    {
        private readonly SICESI_StereoEvaluationController _controller;
        private readonly SICESI_SectorMaskCollector _collector;
        private readonly SICESI_GpuMaskReadbackHandler _readbackHandler;

        public SICESI_GpuMaskReadbackHandler ReadbackHandler => _readbackHandler;

        public SICESI_SectorMaskDensityRunner(SICESI_StereoEvaluationController controller, SICESI_SectorMaskCollector collector)
        {
            _controller = controller;
            _collector = collector;
            _readbackHandler = new SICESI_GpuMaskReadbackHandler();
        }

        public IEnumerator RunRoutine()
        {
            _collector.isCollecting = true;
            SICESI_SectorMaskCollector.IsCollectingActive = true;
            _collector.statusMessage = "Starting SectorMask Density Sweep...";

            string conditionRootDir = _controller.ConditionRootDir;
            string sweepRootDir = Path.Combine(conditionRootDir, "SectorMaskSweep");
            Directory.CreateDirectory(sweepRootDir);
            _controller.SaveSceneTransformsJson(sweepRootDir);
            _controller.SaveSceneTransformsJson(conditionRootDir);

            AppLogger.Log("SICESI", $"=== 占有セクターマスク 密度スイープ開始: {sweepRootDir} ===");

            float prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            bool prevRecordNeighborCount = false;
            bool prevSoftFade = true;
            if (_controller.occlusionPipelineController != null)
            {
                prevRecordNeighborCount = _controller.occlusionPipelineController.recordNeighborCountMap;
                prevSoftFade = _controller.occlusionPipelineController.enableSoftOcclusionFade;
                _controller.occlusionPipelineController.recordNeighborCountMap = true;
                _controller.occlusionPipelineController.enableSoftOcclusionFade = false;
            }
            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                PCDRendererFeature.Instance.settings.recordNeighborCountMap = true;
                PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = false;
            }

            var voBackup = _controller.SetVirtualObjectUnlitWhiteState(true);
            try
            {
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

                _collector.statusMessage = "Capturing Ground Truth for SectorMask Sweep...";
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

                    _collector.statusMessage = $"Collecting ({d + 1}/{densities.Length}): Density = {densityStr} ({_controller.densityUnit})";
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
                            PCDRendererFeature.Instance.settings.enableSoftOcclusionFade = false;
                        }
                    }
                    if (_controller.occlusionPipelineController != null)
                    {
                        _controller.occlusionPipelineController.recordNeighborCountMap = true;
                        _controller.occlusionPipelineController.enableSoftOcclusionFade = false;
                    }

                    for (int f = 0; f < _controller.waitFramesAfterDensityChange; f++) yield return null;
                    yield return new WaitForEndOfFrame();

                    yield return _controller.StartCoroutine(
                        _readbackHandler.CaptureAndExportStereoEyes(
                            _controller.leftEyeCamera,
                            _controller.rightEyeCamera,
                            targetDir,
                            densityStr,
                            (cam, path) => _controller.SaveCameraView(cam, path)
                        )
                    );

                    SaveParamsJson(targetDir, density, _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.occlusionThreshold : 0.1f);
                    AppLogger.Log("SICESI", $"[{d + 1}/{densities.Length}] 密度 {densityStr} の同期データ収集完了: {targetDir}");
                }

                _collector.statusMessage = "All SectorMask Density Sweeps Completed!";
                AppLogger.Log("SICESI", $"=== 全密度の占有セクターマスク収集が完了しました! 保存先: {sweepRootDir} ===");
            }
            finally
            {
                _controller.RestoreVirtualObjectState(voBackup);
                Time.timeScale = prevTimeScale;
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
                Debug.LogWarning($"[SICESI] JSON保存エラー: {ex.Message}");
            }
        }
    }
}
