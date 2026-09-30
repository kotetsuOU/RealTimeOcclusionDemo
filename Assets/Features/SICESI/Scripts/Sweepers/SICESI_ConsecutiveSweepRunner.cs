using System.Collections;
using System.IO;
using UnityEngine;
using RealSense.DummyPointCloud;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// SICE SI 2026 提案手法である「連続非占有セクター許容規則（SectorConsecutiveZeros モード）」の
    /// パラメータスイープ撮影を担当するランナークラス。
    /// </summary>
    public class SICESI_ConsecutiveSweepRunner
    {
        private readonly SICESI_StereoEvaluationController _controller;
        private readonly SICESI_MaterialSwapper _swapper;

        public SICESI_ConsecutiveSweepRunner(SICESI_StereoEvaluationController controller, SICESI_MaterialSwapper swapper)
        {
            _controller = controller;
            _swapper = swapper;
        }

        public IEnumerator RunRoutine()
        {
            _controller.isCapturing = true;
            string conditionRootDir = _controller.ConditionRootDir;
            SICESI_ScreenCaptureUtil.SaveSceneTransformsJson(conditionRootDir, _controller.conditionName, _controller.groundTruthObject, _controller.virtualObject, _controller.leftEyeCamera, _controller.rightEyeCamera, _controller.sceneCaptureCamera);
            AppLogger.Log(_controller, "[SICESI] === 連続非占有セクタースイープ開始 ===", SICESI_StereoEvaluationController.TagStereoSweep);

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
                    _controller.occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorConsecutiveZeros;
                }

                string unitSuffix = _controller.GetDensityUnitSuffix();

                // 重複排除設定が反映された候補ペアリストを取得
                var validPairs = _controller.GetValidConsecutivePairs();

                if (_controller.sweepDensitiesAcrossConsecutive && _controller.dummyPointCloudProvider != null)
                {
                    for (int d = 0; d < _controller.sweepDensities.Length; d++)
                    {
                        float density = _controller.sweepDensities[d];
                        string densityStr = FormatFloat(density);
                        _controller.dummyPointCloudProvider.densityUnit = _controller.densityUnit;
                        _controller.dummyPointCloudProvider.densityValue = density;

                        for (int f = 0; f < _controller.waitFramesAfterDensityChange; f++) yield return null;

                        foreach (var pair in validPairs)
                        {
                            int sector = pair.sector;
                            int maxZero = pair.maxZero;

                            if (_controller.occlusionPipelineController != null)
                            {
                                _controller.occlusionPipelineController.minOccludedSectors = sector;
                                _controller.occlusionPipelineController.maxConsecutiveEmptySectors = maxZero;
                            }

                            for (int f = 0; f < 2; f++) yield return null;
                            yield return new WaitForEndOfFrame();

                            string folderName = $"R{sector}_L{maxZero}";
                            string targetDir = Path.Combine(conditionRootDir, $"density_{densityStr}{unitSuffix}", folderName);
                            SICESI_ScreenCaptureUtil.CaptureStereoViews(_controller.leftEyeCamera, _controller.rightEyeCamera, targetDir, $"test_{densityStr}_{folderName}", _controller.applySRGBConversion);

                            float occTh = _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.occlusionThreshold : 0.8f;
                            SICESI_ScreenCaptureUtil.SaveEvaluationParamsJson(targetDir, _controller.conditionName, density, _controller.densityUnit.ToString(), occTh, PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorConsecutiveZeros.ToString(), sector, maxZero, _controller.groundTruthObject, _controller.virtualObject);
                        }
                    }
                }
                else
                {
                    float curDensity = _controller.dummyPointCloudProvider != null ? _controller.dummyPointCloudProvider.densityValue : 1.0f;
                    string densityStr = FormatFloat(curDensity);

                    foreach (var pair in validPairs)
                    {
                        int sector = pair.sector;
                        int maxZero = pair.maxZero;

                        if (_controller.occlusionPipelineController != null)
                        {
                            _controller.occlusionPipelineController.minOccludedSectors = sector;
                            _controller.occlusionPipelineController.maxConsecutiveEmptySectors = maxZero;
                        }

                        for (int f = 0; f < 2; f++) yield return null;
                        yield return new WaitForEndOfFrame();

                        string folderName = $"R{sector}_L{maxZero}";
                        string targetDir = Path.Combine(conditionRootDir, folderName);
                        SICESI_ScreenCaptureUtil.CaptureStereoViews(_controller.leftEyeCamera, _controller.rightEyeCamera, targetDir, $"test_{densityStr}_{folderName}", _controller.applySRGBConversion);

                        float occTh = _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.occlusionThreshold : 0.8f;
                        SICESI_ScreenCaptureUtil.SaveEvaluationParamsJson(targetDir, _controller.conditionName, curDensity, _controller.densityUnit.ToString(), occTh, PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorConsecutiveZeros.ToString(), sector, maxZero, _controller.groundTruthObject, _controller.virtualObject);
                    }
                }

                _controller.statusMessage = "Consecutive Sector Sweep Completed!";
                AppLogger.Log(_controller, "[SICESI] === 連続非占有セクタースイープ完了 ===", SICESI_StereoEvaluationController.TagStereoSweep);
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
