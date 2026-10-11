using System.Collections;
using System.IO;
using UnityEngine;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// SICE SI において、Unity 空間上のオブジェクト配置（手メッシュ肌色化、点群オフ）を
    /// 左目視点 (scene_left.png)、右目視点 (scene_right.png)、および俯瞰視点 (scene_overview.png)
    /// の計 3 画像として撮影・記録し、Transform 情報を JSON にシリアライズする専任クラス。
    /// （※ 俯瞰カメラは 3DDisplay 投影と無関係な純粋な通常 URP 画像として独立レンダリングされます）
    /// </summary>
    public static class SICESI_SceneOverviewCapturer
    {
        /// <summary>
        /// 指定されたディレクトリに、Unity空間の配置（手:肌色, 点群:オフ）を
        /// 左・右・俯瞰の計 3 画像として撮影・保存します。
        /// 1. scene_transforms.json の保存
        /// 2. 俯瞰カメラ (sceneCaptureCamera) による通常 URP 俯瞰画像 (scene_overview.png) 保存
        /// 3. 左右目線カメラによる視点画像 (scene_left.png, scene_right.png) 保存
        /// </summary>
        public static IEnumerator CaptureSceneOverviewRoutine(
            SICESI_StereoEvaluationController controller,
            string targetDir,
            string conditionOrCaseName = "")
        {
            if (controller == null) yield break;

            var swapper = controller.MaterialSwapper;
            var skinBackup = swapper.SetGroundTruthSkinState(
                controller.groundTruthObject,
                controller.groundTruthCaptureLayer,
                controller.groundTruthSkinColor);

            bool prevPointCloudActive = controller.pointCloudObject != null && controller.pointCloudObject.activeSelf;
            if (controller.pointCloudObject != null)
            {
                controller.pointCloudObject.SetActive(false);
            }

            try
            {
                for (int i = 0; i < 3; i++) yield return null;
                yield return new WaitForEndOfFrame();

                Directory.CreateDirectory(targetDir);
                string condName = string.IsNullOrEmpty(conditionOrCaseName) ? controller.conditionName : conditionOrCaseName;

                // 1. 手・仮想物体・各カメラの Transform 情報を JSON シリアライズ保存
                SICESI_SceneMetadataSerializer.SaveSceneTransformsJson(
                    targetDir,
                    condName,
                    controller.groundTruthObject,
                    controller.virtualObject,
                    controller.leftEyeCamera,
                    controller.rightEyeCamera,
                    controller.sceneCaptureCamera);

                // 2. 俯瞰カメラ (sceneCaptureCamera) による通常 URP 俯瞰画像撮影 (scene_overview.png)
                if (controller.sceneCaptureCamera != null)
                {
                    string sceneOverviewPath = Path.Combine(targetDir, "scene_overview.png");
                    SICESI_ScreenCaptureUtil.SaveCameraViewDirect(controller.sceneCaptureCamera, sceneOverviewPath);
                }

                // 3. 左右目線カメラによる視点画像保存 (scene_left.png, scene_right.png)
                if (controller.captureStereoEyesWithScene)
                {
                    SICESI_ScreenCaptureUtil.SaveEyeViewsForScene(
                        controller.leftEyeCamera,
                        controller.rightEyeCamera,
                        targetDir,
                        controller.applySRGBConversion);
                }

                AppLogger.Log(controller, $"[SICESI] シーン配置撮影完了 (俯瞰: scene_overview.png, ステレオ: {(controller.captureStereoEyesWithScene ? "保存" : "スキップ")}): {targetDir}", SICESI_StereoEvaluationController.TagCapture);
            }
            finally
            {
                swapper.RestoreGroundTruthSkinState(controller.groundTruthObject, skinBackup);
                if (controller.pointCloudObject != null)
                {
                    controller.pointCloudObject.SetActive(prevPointCloudActive);
                }
            }
        }
    }
}
