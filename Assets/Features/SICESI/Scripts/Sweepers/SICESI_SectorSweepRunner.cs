using System.Collections;
using System.IO;
using UnityEngine;
using RealSense.DummyPointCloud;

namespace SICESI
{
    /// <summary>
    /// SICE SI 評価実験において、セクター閾値（SectorThreshold モード: 1〜8）および
    /// 比較用の従来 Average モードのスイープ撮影を担当するランナークラス。
    /// </summary>
    public class SICESI_SectorSweepRunner
    {
        private readonly SICESI_StereoEvaluationController _controller;
        private readonly SICESI_MaterialSwapper _swapper;

        public SICESI_SectorSweepRunner(SICESI_StereoEvaluationController controller, SICESI_MaterialSwapper swapper)
        {
            _controller = controller;
            _swapper = swapper;
        }

        public IEnumerator RunRoutine()
        {
            _controller.isCapturing = true;
            string conditionRootDir = _controller.ConditionRootDir;
            SICESI_ScreenCaptureUtil.SaveSceneTransformsJson(conditionRootDir, _controller.conditionName, _controller.groundTruthObject, _controller.virtualObject, _controller.leftEyeCamera, _controller.rightEyeCamera, _controller.sceneCaptureCamera);
            Debug.Log($"[SICESI] === セクタースイープキャプチャ開始 (条件: {_controller.conditionName}) ===");

            var voBackup = _swapper.SetVirtualObjectUnlitWhiteState(_controller.virtualObject, _controller.renderVirtualObjectAsUnlitWhite);
            try
            {
                // GT 撮影
                var backup = _swapper.SetGroundTruthState(_controller.groundTruthObject, _controller.groundTruthCaptureLayer, _controller.renderGroundTruthAsBlack);
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(false);

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                string gtDir = Path.Combine(conditionRootDir, "GT");
                SICESI_ScreenCaptureUtil.CaptureStereoViews(_controller.leftEyeCamera, _controller.rightEyeCamera, gtDir, "gt", _controller.applySRGBConversion);
                _swapper.RestoreGroundTruthState(backup);

                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(true);
                if (_controller.occlusionPipelineController != null)
                {
                    _controller.occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold;
                }

                string unitSuffix = _controller.GetDensityUnitSuffix();

                if (_controller.sweepDensitiesAcrossSectors && _controller.dummyPointCloudProvider != null)
                {
                    for (int d = 0; d < _controller.sweepDensities.Length; d++)
                    {
                        float density = _controller.sweepDensities[d];
                        string densityStr = FormatFloat(density);
                        _controller.dummyPointCloudProvider.densityUnit = _controller.densityUnit;
                        _controller.dummyPointCloudProvider.densityValue = density;

                        for (int f = 0; f < _controller.waitFramesAfterDensityChange; f++) yield return null;

                        if (_controller.includeAverageMode && _controller.occlusionPipelineController != null)
                        {
                            _controller.occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.Average;
                            for (int f = 0; f < 2; f++) yield return null;
                            yield return new WaitForEndOfFrame();

                            string avgTargetDir = Path.Combine(conditionRootDir, $"density_{densityStr}{unitSuffix}", "mode_Average");
                            SICESI_ScreenCaptureUtil.CaptureStereoViews(_controller.leftEyeCamera, _controller.rightEyeCamera, avgTargetDir, $"test_{densityStr}_avg", _controller.applySRGBConversion);
                            SICESI_ScreenCaptureUtil.SaveEvaluationParamsJson(avgTargetDir, _controller.conditionName, density, _controller.densityUnit.ToString(), _controller.occlusionPipelineController.occlusionThreshold, PCDRendererFeature.PCD_OcclusionEvaluationMode.Average.ToString(), 1, 8, _controller.groundTruthObject, _controller.virtualObject);
                        }

                        if (_controller.occlusionPipelineController != null)
                        {
                            _controller.occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold;
                        }

                        for (int s = 0; s < _controller.sweepSectors.Length; s++)
                        {
                            int sector = _controller.sweepSectors[s];
                            _controller.statusMessage = $"Density {densityStr} ({d + 1}/{_controller.sweepDensities.Length}) | Sector {sector}/8";
                            if (_controller.occlusionPipelineController != null)
                            {
                                _controller.occlusionPipelineController.minOccludedSectors = sector;
                            }

                            for (int f = 0; f < 2; f++) yield return null;
                            yield return new WaitForEndOfFrame();

                            string targetDir = Path.Combine(conditionRootDir, $"density_{densityStr}{unitSuffix}", $"sector_{sector}");
                            SICESI_ScreenCaptureUtil.CaptureStereoViews(_controller.leftEyeCamera, _controller.rightEyeCamera, targetDir, $"test_{densityStr}_sec{sector}", _controller.applySRGBConversion);
                            float occTh = _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.occlusionThreshold : 0.8f;
                            SICESI_ScreenCaptureUtil.SaveEvaluationParamsJson(targetDir, _controller.conditionName, density, _controller.densityUnit.ToString(), occTh, PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold.ToString(), sector, 8, _controller.groundTruthObject, _controller.virtualObject);
                        }
                    }
                }
                else
                {
                    float curDensity = _controller.dummyPointCloudProvider != null ? _controller.dummyPointCloudProvider.densityValue : 1.0f;
                    string densityStr = FormatFloat(curDensity);

                    if (_controller.includeAverageMode && _controller.occlusionPipelineController != null)
                    {
                        _controller.occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.Average;
                        for (int f = 0; f < 2; f++) yield return null;
                        yield return new WaitForEndOfFrame();

                        string avgTargetDir = Path.Combine(conditionRootDir, "mode_Average");
                        SICESI_ScreenCaptureUtil.CaptureStereoViews(_controller.leftEyeCamera, _controller.rightEyeCamera, avgTargetDir, $"test_{densityStr}_avg", _controller.applySRGBConversion);
                        SICESI_ScreenCaptureUtil.SaveEvaluationParamsJson(avgTargetDir, _controller.conditionName, curDensity, _controller.densityUnit.ToString(), _controller.occlusionPipelineController.occlusionThreshold, PCDRendererFeature.PCD_OcclusionEvaluationMode.Average.ToString(), 1, 8, _controller.groundTruthObject, _controller.virtualObject);
                    }

                    if (_controller.occlusionPipelineController != null)
                    {
                        _controller.occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold;
                    }

                    for (int s = 0; s < _controller.sweepSectors.Length; s++)
                    {
                        int sector = _controller.sweepSectors[s];
                        _controller.statusMessage = $"Sector {sector}/8 ({s + 1}/{_controller.sweepSectors.Length})";
                        if (_controller.occlusionPipelineController != null)
                        {
                            _controller.occlusionPipelineController.minOccludedSectors = sector;
                        }

                        for (int f = 0; f < 2; f++) yield return null;
                        yield return new WaitForEndOfFrame();

                        string targetDir = Path.Combine(conditionRootDir, $"sector_{sector}");
                        SICESI_ScreenCaptureUtil.CaptureStereoViews(_controller.leftEyeCamera, _controller.rightEyeCamera, targetDir, $"test_sec{sector}", _controller.applySRGBConversion);
                        float occTh = _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.occlusionThreshold : 0.8f;
                        SICESI_ScreenCaptureUtil.SaveEvaluationParamsJson(targetDir, _controller.conditionName, curDensity, _controller.densityUnit.ToString(), occTh, PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold.ToString(), sector, 8, _controller.groundTruthObject, _controller.virtualObject);
                    }
                }

                _controller.statusMessage = "Sector Sweep Completed!";
                Debug.Log("[SICESI] === セクタースイープキャプチャ完了 ===");
            }
            finally
            {
                _swapper.RestoreVirtualObjectState(voBackup);
                _controller.isCapturing = false;
            }
        }

        private static string FormatFloat(float val)
        {
            return val.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
