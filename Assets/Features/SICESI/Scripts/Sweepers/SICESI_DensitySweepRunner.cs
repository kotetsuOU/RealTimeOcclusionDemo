using System.Collections;
using System.IO;
using UnityEngine;
using RealSense.DummyPointCloud;

namespace SICESI
{
    /// <summary>
    /// SICE SI 評価実験において、点群密度（Density）のスイープ撮影を担当するランナークラス。
    /// </summary>
    public class SICESI_DensitySweepRunner
    {
        private readonly SICESI_StereoEvaluationController _controller;
        private readonly SICESI_MaterialSwapper _swapper;

        public SICESI_DensitySweepRunner(SICESI_StereoEvaluationController controller, SICESI_MaterialSwapper swapper)
        {
            _controller = controller;
            _swapper = swapper;
        }

        public IEnumerator RunRoutine()
        {
            _controller.isCapturing = true;
            string conditionRootDir = _controller.ConditionRootDir;
            SICESI_ScreenCaptureUtil.SaveSceneTransformsJson(conditionRootDir, _controller.conditionName, _controller.groundTruthObject, _controller.virtualObject, _controller.leftEyeCamera, _controller.rightEyeCamera, _controller.sceneCaptureCamera);
            Debug.Log($"[SICESI] === 点群密度スイープキャプチャ開始 (条件: {_controller.conditionName}) 保存先: {conditionRootDir} ===");

            var voBackup = _swapper.SetVirtualObjectUnlitWhiteState(_controller.virtualObject, _controller.renderVirtualObjectAsUnlitWhite);
            try
            {
                // Step 1: GT 撮影
                var backup = _swapper.SetGroundTruthState(_controller.groundTruthObject, _controller.groundTruthCaptureLayer, _controller.renderGroundTruthAsBlack);
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(false);

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                string gtDir = Path.Combine(conditionRootDir, "GT");
                SICESI_ScreenCaptureUtil.CaptureStereoViews(_controller.leftEyeCamera, _controller.rightEyeCamera, gtDir, "gt", _controller.applySRGBConversion);
                _swapper.RestoreGroundTruthState(backup);

                // Step 2: 点群表示に切り替え
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(true);

                // Step 3: 各密度で順次撮影
                string unitSuffix = _controller.GetDensityUnitSuffix();
                for (int i = 0; i < _controller.sweepDensities.Length; i++)
                {
                    float density = _controller.sweepDensities[i];
                    string densityStr = density.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                    _controller.statusMessage = $"Running sweep ({i + 1}/{_controller.sweepDensities.Length}): Density = {densityStr}{unitSuffix}";

                    _controller.dummyPointCloudProvider.densityUnit = _controller.densityUnit;
                    _controller.dummyPointCloudProvider.densityValue = density;

                    for (int f = 0; f < _controller.waitFramesAfterDensityChange; f++) yield return null;
                    yield return new WaitForEndOfFrame();

                    string targetDir = Path.Combine(conditionRootDir, $"density_{densityStr}{unitSuffix}");
                    SICESI_ScreenCaptureUtil.CaptureStereoViews(_controller.leftEyeCamera, _controller.rightEyeCamera, targetDir, $"test_{densityStr}", _controller.applySRGBConversion);

                    var curMode = _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.evaluationMode : PCDRendererFeature.PCD_OcclusionEvaluationMode.Average;
                    float occThreshold = _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.occlusionThreshold : 0.8f;
                    int minSec = _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.minOccludedSectors : 1;
                    int maxZeros = _controller.occlusionPipelineController != null ? _controller.occlusionPipelineController.maxConsecutiveEmptySectors : 8;

                    SICESI_ScreenCaptureUtil.SaveEvaluationParamsJson(targetDir, _controller.conditionName, density, _controller.densityUnit.ToString(), occThreshold, curMode.ToString(), minSec, maxZeros, _controller.groundTruthObject, _controller.virtualObject);
                }

                _controller.statusMessage = "Density Sweep Completed!";
                Debug.Log("[SICESI] === 点群密度スイープキャプチャ完了 ===");
            }
            finally
            {
                _swapper.RestoreVirtualObjectState(voBackup);
                _controller.isCapturing = false;
            }
        }
    }
}
