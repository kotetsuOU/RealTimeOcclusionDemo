using System.Collections;
using System.IO;
using UnityEngine;
using RealSense.DummyPointCloud;

namespace SICESI
{
    /// <summary>
    /// SICE SI 評価実験において、点群密度（Density）× オクルージョン判定閾値（OcclusionThreshold）の
    /// 2D グリッドスイープ撮影を担当するランナークラス。
    /// </summary>
    public class SICESI_ThresholdSweepRunner
    {
        private readonly SICESI_StereoEvaluationController _controller;
        private readonly SICESI_MaterialSwapper _swapper;

        public SICESI_ThresholdSweepRunner(SICESI_StereoEvaluationController controller, SICESI_MaterialSwapper swapper)
        {
            _controller = controller;
            _swapper = swapper;
        }

        public IEnumerator RunRoutine()
        {
            _controller.isCapturing = true;
            string conditionRootDir = _controller.ConditionRootDir;
            SICESI_ScreenCaptureUtil.SaveSceneTransformsJson(conditionRootDir, _controller.conditionName, _controller.groundTruthObject, _controller.virtualObject, _controller.leftEyeCamera, _controller.rightEyeCamera, _controller.sceneCaptureCamera);

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
                    _controller.occlusionPipelineController.evaluationMode = _controller.fixedEvaluationMode;
                    _controller.occlusionPipelineController.minOccludedSectors = _controller.fixedMinOccludedSectors;
                }

                string unitSuffix = _controller.GetDensityUnitSuffix();
                for (int d = 0; d < _controller.sweepDensities.Length; d++)
                {
                    float density = _controller.sweepDensities[d];
                    string densityStr = FormatFloat(density);
                    _controller.dummyPointCloudProvider.densityUnit = _controller.densityUnit;
                    _controller.dummyPointCloudProvider.densityValue = density;

                    for (int f = 0; f < _controller.waitFramesAfterDensityChange; f++) yield return null;

                    for (int t = 0; t < _controller.sweepOcclusionThresholds.Length; t++)
                    {
                        float threshold = _controller.sweepOcclusionThresholds[t];
                        string thStr = FormatFloat(threshold);

                        if (_controller.occlusionPipelineController != null)
                        {
                            _controller.occlusionPipelineController.occlusionThreshold = threshold;
                        }

                        for (int f = 0; f < 2; f++) yield return null;
                        yield return new WaitForEndOfFrame();

                        string targetDir = Path.Combine(conditionRootDir, $"density_{densityStr}{unitSuffix}", $"th_{thStr}");
                        SICESI_ScreenCaptureUtil.CaptureStereoViews(_controller.leftEyeCamera, _controller.rightEyeCamera, targetDir, $"test_{densityStr}_th{thStr}", _controller.applySRGBConversion);
                        SICESI_ScreenCaptureUtil.SaveEvaluationParamsJson(targetDir, _controller.conditionName, density, _controller.densityUnit.ToString(), threshold, _controller.fixedEvaluationMode.ToString(), _controller.fixedMinOccludedSectors, 8, _controller.groundTruthObject, _controller.virtualObject);
                    }
                }

                _controller.statusMessage = "Density & Threshold Sweep Completed!";
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
