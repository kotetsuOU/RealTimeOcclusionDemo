using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// SICE SI 評価実験において、カメラ映像のキャプチャ（PNG保存）およびステレオ視点保存を担当する専任ユーティリティクラス。
    /// （※ Transform や評価パラメータの JSON 入出力は SICESI_SceneMetadataSerializer に分離されています）
    /// </summary>
    public static class SICESI_ScreenCaptureUtil
    {
        #region 後方互換性エイリアス (SICESI_SceneMetadataSerializer)

        public class SerializableTransformData : SICESI_SceneMetadataSerializer.SerializableTransformData { }
        public class SceneTransformsData : SICESI_SceneMetadataSerializer.SceneTransformsData { }
        public class EvaluationParamsData : SICESI_SceneMetadataSerializer.EvaluationParamsData { }

        #endregion

        /// <summary>
        /// 指定カメラの描画結果からピクセルを読み出して Texture2D として直接取得します（ディスク保存なし）。
        /// 取得した Texture2D は不要になった際に呼び出し側で DestroyImmediate / Destroy する必要があります。
        /// </summary>
        public static Texture2D CaptureCameraTexture(Camera cam, bool applySRGBConversion, bool bypassSRGBConversion = false)
        {
            if (cam == null) return null;

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

            screenshot.Apply();
            RenderTexture.active = prevActive;
            return screenshot;
        }

        /// <summary>
        /// 指定カメラの描画結果からピクセルを読み出してPNG保存します。
        /// </summary>
        public static void SaveCameraView(Camera cam, string destinationPath, bool applySRGBConversion, bool bypassSRGBConversion = false)
        {
            if (cam == null) return;

            Texture2D screenshot = CaptureCameraTexture(cam, applySRGBConversion, bypassSRGBConversion);
            if (screenshot == null) return;

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
        /// 俯瞰カメラなどの通常 URP カメラから、3DDisplay や画面表示に依存せず独立してレンダリング結果を取得・PNG 保存します。
        /// オクルージョンや 3DDisplay 投影を介さず、純粋な Unity 空間の配置を単一の通常画像として出力します。
        /// </summary>
        public static void SaveCameraViewDirect(Camera cam, string destinationPath, int defaultWidth = 3840, int defaultHeight = 2160)
        {
            if (cam == null) return;

            int width = cam.targetTexture != null ? cam.targetTexture.width : (cam.pixelWidth > 0 ? cam.pixelWidth : defaultWidth);
            int height = cam.targetTexture != null ? cam.targetTexture.height : (cam.pixelHeight > 0 ? cam.pixelHeight : defaultHeight);
            if (width <= 0) width = defaultWidth;
            if (height <= 0) height = defaultHeight;

            RenderTexture prevTarget = cam.targetTexture;
            RenderTexture prevActive = RenderTexture.active;

            RenderTexture tempRt = null;
            RenderTexture renderTarget = prevTarget;

            if (renderTarget == null)
            {
                tempRt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cam.targetTexture = tempRt;
                cam.Render();
                renderTarget = tempRt;
            }

            RenderTexture.active = renderTarget;
            Texture2D screenshot = new Texture2D(renderTarget.width, renderTarget.height, TextureFormat.RGB24, false);
            screenshot.ReadPixels(new Rect(0, 0, renderTarget.width, renderTarget.height), 0, 0);
            screenshot.Apply();

            RenderTexture.active = prevActive;
            if (tempRt != null)
            {
                cam.targetTexture = prevTarget;
                RenderTexture.ReleaseTemporary(tempRt);
            }

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
        public static void CaptureStereoViews(Camera leftCam, Camera rightCam, string targetDir, string filePrefix, bool applySRGB = false, bool bypassSRGBConversion = false)
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

        #region 後方互換用 JSON 委譲メソッド (SICESI_SceneMetadataSerializer への委譲)

        /// <summary>
        /// 手メッシュ、仮想オブジェクト、およびカメラの Transform 情報を JSON に保存します。
        /// </summary>
        public static void SaveSceneTransformsJson(string targetDir, string conditionName, GameObject gtObj, GameObject voObj, Camera leftCam, Camera rightCam, Camera sceneCam)
            => SICESI_SceneMetadataSerializer.SaveSceneTransformsJson(targetDir, conditionName, gtObj, voObj, leftCam, rightCam, sceneCam);

        /// <summary>
        /// JSON から Transform 情報を復元・適用します。
        /// </summary>
        public static bool LoadAndApplySceneTransformsJson(string jsonPath, GameObject gtObj, GameObject voObj, Camera leftCam, Camera rightCam, Camera sceneCam, out string outConditionName)
            => SICESI_SceneMetadataSerializer.LoadAndApplySceneTransformsJson(jsonPath, gtObj, voObj, leftCam, rightCam, sceneCam, out outConditionName);

        /// <summary>
        /// 撮影時の正確な評価設定値を JSON として記録します。
        /// </summary>
        public static void SaveEvaluationParamsJson(string targetDir, string conditionName, float density, string densityUnit, float threshold, string mode, int sectors, int maxZeros, GameObject gtObj, GameObject voObj, float tAve = -1f, float tLoc = -1f)
            => SICESI_SceneMetadataSerializer.SaveEvaluationParamsJson(targetDir, conditionName, density, densityUnit, threshold, mode, sectors, maxZeros, gtObj, voObj, tAve, tLoc);

        #endregion
    }
}
