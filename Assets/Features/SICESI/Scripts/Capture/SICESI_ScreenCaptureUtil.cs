using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// SICE SI 評価実験において、カメラ映像のキャプチャ（PNG保存）、
    /// およびシーンの Transform / 評価パラメータの JSON 出力を担当するユーティリティクラス。
    /// </summary>
    public static class SICESI_ScreenCaptureUtil
    {
        [System.Serializable]
        public class SerializableTransformData
        {
            public Vector3 position;
            public Vector3 rotationEuler;
            public Quaternion rotationQuaternion;
            public Vector3 localScale;

            public static SerializableTransformData FromTransform(Transform t)
            {
                if (t == null) return null;
                return new SerializableTransformData
                {
                    position = t.position,
                    rotationEuler = t.eulerAngles,
                    rotationQuaternion = t.rotation,
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
        /// 指定カメラの描画結果からピクセルを読み出してPNG保存します。
        /// </summary>
        public static void SaveCameraView(Camera cam, string destinationPath, bool applySRGBConversion, bool bypassSRGBConversion = false)
        {
            if (cam == null) return;

            int width = cam.pixelWidth > 0 ? cam.pixelWidth : Screen.width;
            int height = cam.pixelHeight > 0 ? cam.pixelHeight : Screen.height;

            RenderTexture prevActive = RenderTexture.active;
            Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);

            if (cam.targetTexture != null)
            {
                if (!bypassSRGBConversion && applySRGBConversion && QualitySettings.desiredColorSpace == ColorSpace.Linear)
                {
                    RenderTexture tempSRGB = RenderTexture.GetTemporary(
                        width, height, 0,
                        RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.sRGB
                    );
                    Graphics.Blit(cam.targetTexture, tempSRGB);
                    RenderTexture.active = tempSRGB;
                    screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    RenderTexture.ReleaseTemporary(tempSRGB);
                }
                else
                {
                    RenderTexture.active = cam.targetTexture;
                    screenshot.ReadPixels(new Rect(0, 0, cam.targetTexture.width, cam.targetTexture.height), 0, 0);
                }
            }
            else
            {
                RenderTexture.active = null;
                Rect screenRect = cam.rect;
                int x = Mathf.RoundToInt(screenRect.x * Screen.width);
                int y = Mathf.RoundToInt(screenRect.y * Screen.height);
                screenshot.ReadPixels(new Rect(x, y, width, height), 0, 0);
            }

            RenderTexture.active = prevActive;

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            byte[] pngBytes = screenshot.EncodeToPNG();
            UnityEngine.Object.Destroy(screenshot);

            Task.Run(() =>
            {
                try
                {
                    File.WriteAllBytes(destinationPath, pngBytes);
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning(SICESI_StereoEvaluationController.TagCapture, $"[SICESI] 非同期画像保存エラー ({destinationPath}): {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 左右目線カメラの映像を指定ディレクトリに保存します。
        /// </summary>
        public static void CaptureStereoViews(Camera leftCam, Camera rightCam, string targetDir, string filePrefix, bool applySRGB, bool bypassSRGBConversion = false)
        {
            string leftDir = Path.Combine(targetDir, "Left");
            string rightDir = Path.Combine(targetDir, "Right");
            Directory.CreateDirectory(leftDir);
            Directory.CreateDirectory(rightDir);

            if (leftCam != null)
            {
                string leftPath = Path.Combine(leftDir, $"{filePrefix}_left.png");
                SaveCameraView(leftCam, leftPath, applySRGB, bypassSRGBConversion);
            }

            if (rightCam != null)
            {
                string rightPath = Path.Combine(rightDir, $"{filePrefix}_right.png");
                SaveCameraView(rightCam, rightPath, applySRGB, bypassSRGBConversion);
            }
        }

        /// <summary>
        /// シーン保存用に左右目線カメラの画像 (scene_left.png, scene_right.png) を保存します。
        /// </summary>
        public static void SaveEyeViewsForScene(Camera leftCam, Camera rightCam, string sceneDir, bool applySRGB)
        {
            Directory.CreateDirectory(sceneDir);

            if (leftCam != null)
            {
                string leftPath = Path.Combine(sceneDir, "scene_left.png");
                SaveCameraView(leftCam, leftPath, applySRGB);
            }

            if (rightCam != null)
            {
                string rightPath = Path.Combine(sceneDir, "scene_right.png");
                SaveCameraView(rightCam, rightPath, applySRGB);
            }
        }

        /// <summary>
        /// 手メッシュ、仮想オブジェクト、およびカメラの Transform 情報を JSON に保存します。
        /// </summary>
        public static void SaveSceneTransformsJson(string targetDir, string conditionName, GameObject gtObj, GameObject voObj, Camera leftCam, Camera rightCam, Camera sceneCam)
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
        public static bool LoadAndApplySceneTransformsJson(string jsonPath, GameObject gtObj, GameObject voObj, Camera leftCam, Camera rightCam, Camera sceneCam, out string outConditionName)
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
        public static void SaveEvaluationParamsJson(string targetDir, string conditionName, float density, string densityUnit, float threshold, string mode, int sectors, int maxZeros, GameObject gtObj, GameObject voObj)
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
