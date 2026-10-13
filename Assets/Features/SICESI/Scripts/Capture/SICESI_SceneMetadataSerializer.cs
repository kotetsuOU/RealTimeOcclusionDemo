using System;
using System.IO;
using UnityEngine;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// SICE SI 評価実験において、手・仮想物体・カメラの Transform 情報 (scene_transforms.json) や
    /// 評価実験パラメータ (evaluation_params.json) のシリアライズ・デシリアライズ・保存・復元を担当するクラス。
    /// （※ 画像キャプチャを担当する SICESI_ScreenCaptureUtil からデータモデル・JSON I/O 責務を分離）
    /// </summary>
    public static class SICESI_SceneMetadataSerializer
    {
        [System.Serializable]
        public class SerializableTransformData
        {
            public string name;
            public Vector3 position;
            public Vector3 rotationEuler;
            public Quaternion rotationQuaternion;
            public Vector3 lossyScale;
            public Vector3 localPosition;
            public Vector3 localEulerAngles;
            public Vector3 localScale;

            public static SerializableTransformData FromTransform(Transform t)
            {
                if (t == null) return null;
                return new SerializableTransformData
                {
                    name = t.name,
                    position = t.position,
                    rotationEuler = t.eulerAngles,
                    rotationQuaternion = t.rotation,
                    lossyScale = t.lossyScale,
                    localPosition = t.localPosition,
                    localEulerAngles = t.localEulerAngles,
                    localScale = t.localScale
                };
            }

            public void ApplyToTransform(Transform t)
            {
                if (t == null) return;
                t.position = position;
                if (rotationQuaternion.x != 0f || rotationQuaternion.y != 0f || rotationQuaternion.z != 0f || rotationQuaternion.w != 0f)
                {
                    t.rotation = rotationQuaternion;
                }
                else
                {
                    t.eulerAngles = rotationEuler;
                }
                t.localScale = localScale;
            }
        }

        [System.Serializable]
        public class SceneTransformsData
        {
            public string timestamp;
            public string conditionName;
            public SerializableTransformData handMesh;
            public SerializableTransformData virtualObject;
            public SerializableTransformData leftCamera;
            public SerializableTransformData rightCamera;
            public SerializableTransformData sceneCamera;
        }

        [System.Serializable]
        public class EvaluationParamsData
        {
            public string conditionName;
            public float densityValue;
            public string densityUnit;
            public float occlusionThreshold;
            public string evaluationMode;
            public int minOccludedSectors;
            public int maxConsecutiveEmptySectors;
            public string timestamp;
            public SerializableTransformData handMeshTransform;
            public SerializableTransformData virtualObjectTransform;
        }

        /// <summary>
        /// 手メッシュ、仮想オブジェクト、およびカメラの Transform 情報を JSON に保存します。
        /// </summary>
        public static void SaveSceneTransformsJson(
            string targetDir,
            string conditionName,
            GameObject gtObj,
            GameObject voObj,
            Camera leftCam,
            Camera rightCam,
            Camera sceneCam)
        {
            try
            {
                var data = new SceneTransformsData
                {
                    timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    conditionName = conditionName,
                    handMesh = SerializableTransformData.FromTransform(gtObj != null ? gtObj.transform : null),
                    virtualObject = SerializableTransformData.FromTransform(voObj != null ? voObj.transform : null),
                    leftCamera = SerializableTransformData.FromTransform(leftCam != null ? leftCam.transform : null),
                    rightCamera = SerializableTransformData.FromTransform(rightCam != null ? rightCam.transform : null),
                    sceneCamera = SerializableTransformData.FromTransform(sceneCam != null ? sceneCam.transform : null)
                };

                string json = JsonUtility.ToJson(data, true);
                Directory.CreateDirectory(targetDir);
                string savePath = Path.Combine(targetDir, "scene_transforms.json");
                File.WriteAllText(savePath, json);
                AppLogger.Log(SICESI_StereoEvaluationController.TagCapture, $"[SICESI] Transform 情報を保存しました: {savePath}");
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(SICESI_StereoEvaluationController.TagCapture, $"[SICESI] scene_transforms.json 保存失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// JSON から Transform 情報を復元・適用します。
        /// </summary>
        /// <summary>
        /// JSON から Transform 情報を復元・適用します (ConditionName の受け取りが不要な場合)。
        /// </summary>
        public static bool LoadAndApplySceneTransformsJson(
            string jsonPath,
            GameObject gtObj,
            GameObject voObj,
            Camera leftCam,
            Camera rightCam,
            Camera sceneCam)
        {
            return LoadAndApplySceneTransformsJson(jsonPath, gtObj, voObj, leftCam, rightCam, sceneCam, out _);
        }

        public static bool LoadAndApplySceneTransformsJson(
            string jsonPath,
            GameObject gtObj,
            GameObject voObj,
            Camera leftCam,
            Camera rightCam,
            Camera sceneCam,
            out string outConditionName)
        {
            outConditionName = "";
            try
            {
                if (!File.Exists(jsonPath))
                {
                    AppLogger.LogWarning(SICESI_StereoEvaluationController.TagCapture, $"[SICESI] scene_transforms.json が見つかりません: {jsonPath}");
                    return false;
                }
                string json = File.ReadAllText(jsonPath);
                var data = JsonUtility.FromJson<SceneTransformsData>(json);
                if (data == null) return false;

                if (data.handMesh != null && gtObj != null) data.handMesh.ApplyToTransform(gtObj.transform);
                if (data.virtualObject != null && voObj != null) data.virtualObject.ApplyToTransform(voObj.transform);
                if (data.leftCamera != null && leftCam != null) data.leftCamera.ApplyToTransform(leftCam.transform);
                if (data.rightCamera != null && rightCam != null) data.rightCamera.ApplyToTransform(rightCam.transform);
                if (data.sceneCamera != null && sceneCam != null) data.sceneCamera.ApplyToTransform(sceneCam.transform);

                if (!string.IsNullOrEmpty(data.conditionName))
                {
                    outConditionName = data.conditionName;
                }

                AppLogger.Log(SICESI_StereoEvaluationController.TagCapture, $"[SICESI] Transform 情報を復元適用しました: {jsonPath}");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(SICESI_StereoEvaluationController.TagCapture, $"[SICESI] scene_transforms.json 適用失敗: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 撮影時の正確な評価設定値を JSON として記録します。
        /// </summary>
        public static void SaveEvaluationParamsJson(
            string targetDir,
            string conditionName,
            float density,
            string densityUnit,
            float threshold,
            string mode,
            int sectors,
            int maxZeros,
            GameObject gtObj,
            GameObject voObj)
        {
            try
            {
                var data = new EvaluationParamsData
                {
                    conditionName = conditionName,
                    densityValue = density,
                    densityUnit = densityUnit,
                    occlusionThreshold = threshold,
                    evaluationMode = mode,
                    minOccludedSectors = sectors,
                    maxConsecutiveEmptySectors = maxZeros,
                    timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    handMeshTransform = SerializableTransformData.FromTransform(gtObj != null ? gtObj.transform : null),
                    virtualObjectTransform = SerializableTransformData.FromTransform(voObj != null ? voObj.transform : null)
                };
                string json = JsonUtility.ToJson(data, true);
                Directory.CreateDirectory(targetDir);
                File.WriteAllText(Path.Combine(targetDir, "evaluation_params.json"), json);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(SICESI_StereoEvaluationController.TagCapture, $"[SICESI] evaluation_params.json 保存失敗: {ex.Message}");
            }
        }
    }
}
