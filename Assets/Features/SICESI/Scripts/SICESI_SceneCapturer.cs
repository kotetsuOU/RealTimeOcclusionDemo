using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// 各配置の説明用シーン画像を保存するための専門キャプチャークラス。
    /// 評価用データ取得後に、指定した撮影用Cameraを用いて手メッシュを一時的に肌色Materialへ変更し、
    /// オフスクリーンRenderTextureへ描画してPNGおよびメタデータJSONとして保存します。
    /// 撮影後は try/finally により、元のMaterial・レンダラー状態・カメラ設定を100%確実に復元します。
    /// </summary>
    [DisallowMultipleComponent]
    [AppLoggable("SICESI")]
    public class SICESI_SceneCapturer : MonoBehaviour, IAppLoggable
    {
        [Header("Scene Capture References")]
        [Tooltip("配置説明画像を保存する撮影用Camera (評価用カメラと独立して設定可能)")]
        public Camera sceneCaptureCamera;

        [Header("Capture Resolution")]
        [Tooltip("保存画像の横解像度")]
        public int sceneCaptureWidth = 1920;

        [Tooltip("保存画像の縦解像度")]
        public int sceneCaptureHeight = 1080;

        [Header("Output Settings")]
        [Tooltip("保存先ルートディレクトリ (未指定時はコントローラーの設定を使用)")]
        public string outputRoot;

        private Material _skinMaterialCache;

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            // AppLogger 登録用
        }

        private void OnDestroy()
        {
            if (_skinMaterialCache != null)
            {
                Destroy(_skinMaterialCache);
                _skinMaterialCache = null;
            }
        }

        /// <summary>
        /// 撮影用の一時肌色マテリアルを生成またはキャッシュから取得します。
        /// </summary>
        private Material GetOrCreateSkinMaterial()
        {
            if (_skinMaterialCache == null)
            {
                Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                _skinMaterialCache = new Material(unlitShader)
                {
                    name = "SICESI_Skin_Mat",
                    color = new Color(0.92f, 0.76f, 0.65f, 1.0f) // 日本人の肌色近似
                };
                if (_skinMaterialCache.HasProperty("_BaseColor"))
                {
                    _skinMaterialCache.SetColor("_BaseColor", new Color(0.92f, 0.76f, 0.65f, 1.0f));
                }
            }
            return _skinMaterialCache;
        }

        /// <summary>
        /// 手メッシュの状態バックアップ用クラス
        /// </summary>
        private class HandRenderStateBackup
        {
            public Dictionary<Renderer, Material[]> OriginalSharedMaterials = new Dictionary<Renderer, Material[]>();
            public Dictionary<Renderer, MaterialPropertyBlock> PropertyBlocks = new Dictionary<Renderer, MaterialPropertyBlock>();
            public Dictionary<GameObject, int> OriginalLayers = new Dictionary<GameObject, int>();
            public Dictionary<GameObject, bool> OriginalActiveStates = new Dictionary<GameObject, bool>();
        }

        /// <summary>
        /// カメラの状態バックアップ用クラス
        /// </summary>
        private class CameraStateBackup
        {
            public bool OriginalEnabled;
            public RenderTexture OriginalTargetTexture;
            public Rect OriginalRect;
            public Color OriginalBackgroundColor;
            public CameraClearFlags OriginalClearFlags;
            public int OriginalCullingMask;
        }

        /// <summary>
        /// 指定ディレクトリに配置説明画像 scene.png と撮影メタデータを保存するコルーチン。
        /// 手メッシュ (handObject / groundTruthObject) を一時的に肌色に変更して撮影し、完了後に復元します。
        /// </summary>
        public IEnumerator CaptureSceneRoutine(string targetDirectory, string sceneId = "Scene_1", GameObject handObject = null, GameObject virtualObject = null, Action<bool, string> onComplete = null)
        {
            if (sceneCaptureCamera == null)
            {
                string errMsg = "[SICESI_SceneCapturer] sceneCaptureCamera が設定されていません。撮影をスキップします。";
                AppLogger.LogWarning(this, "SICESI", errMsg);
                onComplete?.Invoke(false, errMsg);
                yield break;
            }

            Directory.CreateDirectory(targetDirectory);
            string pngPath = Path.Combine(targetDirectory, "scene.png");
            string metaPath = Path.Combine(targetDirectory, "scene_capture_metadata.json");

            AppLogger.Log(this, "SICESI", $"=== シーン説明画像の一時肌色撮影を開始: {pngPath} ===");

            // 1. 手メッシュ (groundTruthObject) の Renderer 設定を退避
            var handBackup = new HandRenderStateBackup();
            Renderer[] handRenderers = handObject != null ? handObject.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();

            foreach (var r in handRenderers)
            {
                if (r == null) continue;
                handBackup.OriginalSharedMaterials[r] = (Material[])r.sharedMaterials.Clone();

                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                handBackup.PropertyBlocks[r] = mpb;
            }

            // 2. 撮影用カメラの状態を退避
            var camBackup = new CameraStateBackup
            {
                OriginalEnabled = sceneCaptureCamera.enabled,
                OriginalTargetTexture = sceneCaptureCamera.targetTexture,
                OriginalRect = sceneCaptureCamera.rect,
                OriginalBackgroundColor = sceneCaptureCamera.backgroundColor,
                OriginalClearFlags = sceneCaptureCamera.clearFlags,
                OriginalCullingMask = sceneCaptureCamera.cullingMask
            };

            RenderTexture prevActiveRT = RenderTexture.active;
            RenderTexture captureRT = null;

            try
            {
                // 3. 手メッシュへ撮影用肌色 Material を一時適用 (全スロット対応)
                Material skinMat = GetOrCreateSkinMaterial();
                foreach (var r in handRenderers)
                {
                    if (r == null) continue;
                    int matCount = r.sharedMaterials.Length;
                    Material[] skinMats = new Material[Mathf.Max(1, matCount)];
                    for (int m = 0; m < skinMats.Length; m++)
                    {
                        skinMats[m] = skinMat;
                    }
                    r.sharedMaterials = skinMats;
                    r.SetPropertyBlock(null); // PropertyBlock による色上書きを一時クリア
                }

                // 4. オフスクリーン描画用 RenderTexture の作成
                int width = Mathf.Max(128, sceneCaptureWidth);
                int height = Mathf.Max(128, sceneCaptureHeight);
                captureRT = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                sceneCaptureCamera.targetTexture = captureRT;
                sceneCaptureCamera.enabled = true;

                // 5. 1フレーム進めて URP 描画を待機
                yield return null;
                yield return new WaitForEndOfFrame();

                // 6. RenderTexture からピクセル読み出し
                RenderTexture.active = captureRT;
                Texture2D screenTex = new Texture2D(width, height, TextureFormat.RGB24, false);
                screenTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                screenTex.Apply();
                RenderTexture.active = prevActiveRT;

                byte[] pngBytes = screenTex.EncodeToPNG();
                Destroy(screenTex);

                // 7. PNG ファイル保存
                File.WriteAllBytes(pngPath, pngBytes);
                AppLogger.Log(this, "SICESI", $"[+] 配置説明画像 (scene.png) を保存しました: {pngPath}");

                // 8. 撮影メタデータ JSON の作成・保存
                var meta = new SceneCaptureMetadata
                {
                    scene_id = sceneId,
                    timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff"),
                    image_width = width,
                    image_height = height,
                    camera = new CameraMetadata
                    {
                        name = sceneCaptureCamera.name,
                        position = sceneCaptureCamera.transform.position,
                        rotation = sceneCaptureCamera.transform.rotation,
                        rotation_euler = sceneCaptureCamera.transform.eulerAngles,
                        fov = sceneCaptureCamera.fieldOfView,
                        near_clip = sceneCaptureCamera.nearClipPlane,
                        far_clip = sceneCaptureCamera.farClipPlane,
                        projection_matrix = MatrixToList(sceneCaptureCamera.projectionMatrix),
                        world_to_camera_matrix = MatrixToList(sceneCaptureCamera.worldToCameraMatrix)
                    },
                    hand_root = handObject != null ? new TransformMetadata(handObject.transform) : null,
                    virtual_object = virtualObject != null ? new TransformMetadata(virtualObject.transform) : null
                };

                string jsonStr = JsonUtility.ToJson(meta, true);
                File.WriteAllText(metaPath, jsonStr, System.Text.Encoding.UTF8);
                AppLogger.Log(this, "SICESI", $"[+] 撮影メタデータ (scene_capture_metadata.json) を保存しました: {metaPath}");

                onComplete?.Invoke(true, pngPath);
            }
            finally
            {
                // 9. 手メッシュの sharedMaterials と PropertyBlock を確実に復元
                foreach (var kvp in handBackup.OriginalSharedMaterials)
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.sharedMaterials = kvp.Value;
                        if (handBackup.PropertyBlocks.TryGetValue(kvp.Key, out var block))
                        {
                            kvp.Key.SetPropertyBlock(block);
                        }
                    }
                }

                // 10. カメラ設定を確実に復元
                if (sceneCaptureCamera != null)
                {
                    sceneCaptureCamera.targetTexture = camBackup.OriginalTargetTexture;
                    sceneCaptureCamera.enabled = camBackup.OriginalEnabled;
                    sceneCaptureCamera.rect = camBackup.OriginalRect;
                    sceneCaptureCamera.backgroundColor = camBackup.OriginalBackgroundColor;
                    sceneCaptureCamera.clearFlags = camBackup.OriginalClearFlags;
                    sceneCaptureCamera.cullingMask = camBackup.OriginalCullingMask;
                }

                RenderTexture.active = prevActiveRT;
                if (captureRT != null)
                {
                    RenderTexture.ReleaseTemporary(captureRT);
                }

                AppLogger.Log(this, "SICESI", "=== 手メッシュ・カメラ設定の復元が完了しました ===");
            }
        }

        private static List<float> MatrixToList(Matrix4x4 m)
        {
            return new List<float>
            {
                m.m00, m.m01, m.m02, m.m03,
                m.m10, m.m11, m.m12, m.m13,
                m.m20, m.m21, m.m22, m.m23,
                m.m30, m.m31, m.m32, m.m33
            };
        }

        [Serializable]
        public class SceneCaptureMetadata
        {
            public string scene_id;
            public string timestamp;
            public int image_width;
            public int image_height;
            public CameraMetadata camera;
            public TransformMetadata hand_root;
            public TransformMetadata virtual_object;
        }

        [Serializable]
        public class CameraMetadata
        {
            public string name;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 rotation_euler;
            public float fov;
            public float near_clip;
            public float far_clip;
            public List<float> projection_matrix;
            public List<float> world_to_camera_matrix;
        }

        [Serializable]
        public class TransformMetadata
        {
            public string name;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 rotation_euler;
            public Vector3 lossy_scale;

            public TransformMetadata(Transform t)
            {
                if (t == null) return;
                name = t.name;
                position = t.position;
                rotation = t.rotation;
                rotation_euler = t.eulerAngles;
                lossy_scale = t.lossyScale;
            }
        }
    }
}
