using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace SICESI
{
    /// <summary>
    /// SICE SI 評価実験において、Ground Truth（真値）、現在の設定での単発撮影、
    /// およびシーン俯瞰（第三者視点＋ステレオ眼）の撮影シーケンスを担当するランナークラス。
    /// </summary>
    public class SICESI_SnapshotRunner
    {
        private readonly SICESI_StereoEvaluationController _controller;
        private readonly SICESI_MaterialSwapper _swapper;

        public SICESI_SnapshotRunner(SICESI_StereoEvaluationController controller, SICESI_MaterialSwapper swapper)
        {
            _controller = controller;
            _swapper = swapper;
        }

        /// <summary>
        /// Ground Truth (手メッシュ黒化＋仮想物体Unlit白化) の左右ステレオ画像を撮影し保存します。
        /// </summary>
        public IEnumerator CaptureGroundTruthRoutine()
        {
            _controller.isCapturing = true;
            _controller.statusMessage = "Capturing Ground Truth...";

            var gtBackup = _swapper.SetGroundTruthState(_controller.groundTruthObject, _controller.groundTruthCaptureLayer, _controller.renderGroundTruthAsBlack);
            var voBackup = _swapper.SetVirtualObjectUnlitWhiteState(_controller.virtualObject, _controller.renderVirtualObjectAsUnlitWhite);
            try
            {
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(false);

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                string conditionRootDir = _controller.ConditionRootDir;
                SICESI_ScreenCaptureUtil.SaveSceneTransformsJson(conditionRootDir, _controller.conditionName, _controller.groundTruthObject, _controller.virtualObject, _controller.leftEyeCamera, _controller.rightEyeCamera, _controller.sceneCaptureCamera);
                string gtDir = Path.Combine(conditionRootDir, "GT");
                SICESI_ScreenCaptureUtil.CaptureStereoViews(_controller.leftEyeCamera, _controller.rightEyeCamera, gtDir, "gt", _controller.applySRGBConversion);

                string commonGtDir = Path.Combine(_controller.outputDirectory, "GT");
                if (commonGtDir != gtDir)
                {
                    SICESI_ScreenCaptureUtil.CaptureStereoViews(_controller.leftEyeCamera, _controller.rightEyeCamera, commonGtDir, "gt", _controller.applySRGBConversion);
                }

                _controller.statusMessage = "Ground Truth Capture Completed!";
                Debug.Log($"[SICESI] GT撮影完了: {gtDir} (共通: {commonGtDir})");
            }
            finally
            {
                _swapper.RestoreVirtualObjectState(voBackup);
                _swapper.RestoreGroundTruthState(gtBackup);
                _controller.isCapturing = false;
            }
        }

        /// <summary>
        /// 現在の点群およびパイプライン設定で単発ステレオ画像を撮影します。
        /// </summary>
        public IEnumerator CaptureCurrentRoutine(string subFolderName)
        {
            _controller.isCapturing = true;
            _controller.statusMessage = $"Capturing condition: {_controller.conditionName}...";

            var voBackup = _swapper.SetVirtualObjectUnlitWhiteState(_controller.virtualObject, _controller.renderVirtualObjectAsUnlitWhite);
            try
            {
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(true);
                if (_controller.dummyPointCloudProvider != null) _controller.dummyPointCloudProvider.densityUnit = _controller.densityUnit;

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                string targetDir = string.IsNullOrEmpty(subFolderName) ? _controller.ConditionRootDir : Path.Combine(_controller.ConditionRootDir, subFolderName);
                SICESI_ScreenCaptureUtil.CaptureStereoViews(_controller.leftEyeCamera, _controller.rightEyeCamera, targetDir, "test", _controller.applySRGBConversion);

                _controller.statusMessage = $"Condition {_controller.conditionName} Capture Completed!";
                Debug.Log($"[SICESI] 条件撮影完了: {targetDir}");
            }
            finally
            {
                _swapper.RestoreVirtualObjectState(voBackup);
                _controller.isCapturing = false;
            }
        }

        /// <summary>
        /// シーン俯瞰カメラおよびステレオ両眼視点からの確認用画像を撮影します。
        /// </summary>
        public IEnumerator CaptureSceneViewRoutine()
        {
            _controller.isCapturing = true;
            _controller.statusMessage = "Capturing Scene Overview...";

            var skinBackup = _swapper.SetGroundTruthSkinState(_controller.groundTruthObject, _controller.groundTruthCaptureLayer, _controller.groundTruthSkinColor);
            try
            {
                if (_controller.pointCloudObject != null) _controller.pointCloudObject.SetActive(true);

                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                string conditionRootDir = _controller.ConditionRootDir;
                SICESI_ScreenCaptureUtil.SaveSceneTransformsJson(conditionRootDir, _controller.conditionName, _controller.groundTruthObject, _controller.virtualObject, _controller.leftEyeCamera, _controller.rightEyeCamera, _controller.sceneCaptureCamera);

                string sceneDir = Path.Combine(conditionRootDir, "Scene");
                Directory.CreateDirectory(sceneDir);

                if (_controller.sceneCaptureCamera != null)
                {
                    string scenePath = Path.Combine(sceneDir, "scene_overview.png");
                    SICESI_ScreenCaptureUtil.SaveCameraView(_controller.sceneCaptureCamera, scenePath, _controller.applySRGBConversion);
                }

                if (_controller.captureStereoEyesWithScene)
                {
                    SICESI_ScreenCaptureUtil.SaveEyeViewsForScene(_controller.leftEyeCamera, _controller.rightEyeCamera, sceneDir, _controller.applySRGBConversion);
                }

                _controller.statusMessage = "Scene Overview Capture Completed!";
                Debug.Log($"[SICESI] シーン俯瞰撮影完了: {sceneDir}");
            }
            finally
            {
                _swapper.RestoreGroundTruthSkinState(_controller.groundTruthObject, skinBackup);
                _controller.isCapturing = false;
            }
        }
    }
}
