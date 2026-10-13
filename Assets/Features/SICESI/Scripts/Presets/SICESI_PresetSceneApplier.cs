using System;
using System.IO;
using UnityEngine;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// SICE SI 配置プリセット (JSON) の内容を読み込み、現在のシーン内の手・仮想物体・カメラへ
    /// 復元・適用する処理を担当するクラス。
    /// </summary>
    public static class SICESI_PresetSceneApplier
    {
        /// <summary>
        /// 指定したプリセット JSON を読み込み、シーン内の手・仮想物体・カメラに適用します。
        /// 必要に応じてシーン内から該当オブジェクトを自動検索・切り替えます。
        /// </summary>
        public static bool ApplyPresetToScene(SICESI_StereoEvaluationController controller, string jsonPath)
        {
            if (controller == null || !File.Exists(jsonPath)) return false;

            try
            {
                string json = File.ReadAllText(jsonPath);
                var data = JsonUtility.FromJson<SICESI_SceneMetadataSerializer.SceneTransformsData>(json);
                if (data == null) return false;

                // 1. 仮想オブジェクトの解決 & 切り替え & Transform 適用
                if (data.virtualObject != null)
                {
                    GameObject targetVo = ResolveTargetObject(controller, data.virtualObject.name, controller.virtualObject);
                    if (targetVo != null)
                    {
                        if (controller.virtualObject != null && controller.virtualObject != targetVo)
                        {
                            controller.virtualObject.SetActive(false);
                        }
                        targetVo.SetActive(true);
                        controller.virtualObject = targetVo;
                        data.virtualObject.ApplyToTransform(targetVo.transform);
                    }
                }

                // 2. 手メッシュの解決 & 切り替え & Transform 適用
                if (data.handMesh != null)
                {
                    GameObject targetHand = ResolveTargetObject(controller, data.handMesh.name, controller.groundTruthObject);
                    if (targetHand != null)
                    {
                        if (controller.groundTruthObject != null && controller.groundTruthObject != targetHand)
                        {
                            controller.groundTruthObject.SetActive(false);
                        }
                        targetHand.SetActive(true);
                        controller.groundTruthObject = targetHand;
                        data.handMesh.ApplyToTransform(targetHand.transform);

                        // ダミー点群プロバイダーのターゲットを同期し、即時サンプリング反映
                        controller.SyncDummyPointCloudWithGroundTruth(true);
                    }
                }

                // 3. カメラの Transform 適用
                if (data.leftCamera != null && controller.leftEyeCamera != null)
                {
                    data.leftCamera.ApplyToTransform(controller.leftEyeCamera.transform);
                }
                if (data.rightCamera != null && controller.rightEyeCamera != null)
                {
                    data.rightCamera.ApplyToTransform(controller.rightEyeCamera.transform);
                }
                if (data.sceneCamera != null && controller.sceneCaptureCamera != null)
                {
                    data.sceneCamera.ApplyToTransform(controller.sceneCaptureCamera.transform);
                }

                if (!string.IsNullOrEmpty(data.conditionName))
                {
                    controller.conditionName = data.conditionName;
                }

                AppLogger.Log(controller, $"[SICESI_PresetSceneApplier] プリセット配置をシーンに適用しました: {Path.GetFileName(Path.GetDirectoryName(jsonPath))}", SICESI_StereoEvaluationController.TagCore);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError(controller, $"[SICESI_PresetSceneApplier] プリセット適用エラー: {ex.Message}", SICESI_StereoEvaluationController.TagCore);
                return false;
            }
        }

        private static GameObject ResolveTargetObject(SICESI_StereoEvaluationController controller, string objectName, GameObject fallback)
        {
            if (string.IsNullOrEmpty(objectName)) return fallback;
            if (fallback != null && fallback.name == objectName) return fallback;

            // データセットリストから検索
            if (controller.datasetConfig != null)
            {
                var fromVo = controller.datasetConfig.FindVirtualObjectByName(objectName);
                if (fromVo != null) return fromVo;

                var fromHand = controller.datasetConfig.FindHandMeshByName(objectName);
                if (fromHand != null) return fromHand;
            }

            // シーン全体から同名オブジェクトを検索
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in all)
            {
                if (go.hideFlags == HideFlags.NotEditable || go.hideFlags == HideFlags.HideAndDontSave) continue;
                if (!go.scene.isLoaded) continue;
                if (go.name == objectName) return go;
            }

            return fallback;
        }
    }
}
