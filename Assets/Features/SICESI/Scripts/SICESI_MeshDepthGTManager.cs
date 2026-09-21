using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// カメラ行列を用いて仮想オブジェクトと手メッシュの生深度を直接CommandBufferでラスタライズし、
    /// GPU上で直接数値比較することで、カラー描画時のアンチエイリアシングや色混ざり、
    /// および既存PCDパイプラインの状態に一切依存しない「真のGround Truthマスク (Mesh Depth GT)」を生成するクラスです。
    /// </summary>
    [DisallowMultipleComponent]
    [AppLoggable("SICESI")]
    public class SICESI_MeshDepthGTManager : MonoBehaviour, IAppLoggable
    {
        [Header("Compute Shader Reference")]
        [Tooltip("深度直接比較を行うコンピュートシェーダー (SICESI_MeshDepthGT.compute)")]
        public ComputeShader meshDepthComputeShader;

        [Header("Depth Capture Shader Reference")]
        [Tooltip("メッシュの生深度を描画するシェーダー (Hidden/SICESI/MeshDepthCapture)")]
        public Shader meshDepthCaptureShader;

        [Header("Transform Alignment Options")]
        [Tooltip("水平反転 (SRDisplay ハーフミラー鏡像整合。PCD パイプラインの最終出力と座標系を一致させる)")]
        public bool flipX = true;

        [Tooltip("垂直反転 (GL.GetGPUProjectionMatrix(proj, true) 使用時は不要)")]
        public bool flipY = false;

        // 深度ラスタライズ用マテリアル
        private Material _depthCaptureMaterial;

        // 比較用生深度レンダーテクスチャ
        private RenderTexture _voDepthRT;
        private RenderTexture _handDepthRT;

        // 出力用マスクレンダーテクスチャ
        private RenderTexture _gtVisibleMaskRT;
        private RenderTexture _gtOccludedMaskRT;
        private RenderTexture _voSilhouetteRT;
        private RenderTexture _handSilhouetteRT;

        private int _allocatedWidth = -1;
        private int _allocatedHeight = -1;

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            // AppLogger 登録用
        }

        private void OnDestroy()
        {
            ReleaseRenderTextures();
            if (_depthCaptureMaterial != null)
            {
                Destroy(_depthCaptureMaterial);
                _depthCaptureMaterial = null;
            }
        }

        private void ReleaseRenderTextures()
        {
            void ReleaseRT(ref RenderTexture rt)
            {
                if (rt != null)
                {
                    if (RenderTexture.active == rt) RenderTexture.active = null;
                    rt.Release();
                    Destroy(rt);
                    rt = null;
                }
            }

            ReleaseRT(ref _voDepthRT);
            ReleaseRT(ref _handDepthRT);
            ReleaseRT(ref _gtVisibleMaskRT);
            ReleaseRT(ref _gtOccludedMaskRT);
            ReleaseRT(ref _voSilhouetteRT);
            ReleaseRT(ref _handSilhouetteRT);

            _allocatedWidth = -1;
            _allocatedHeight = -1;
        }

        private void EnsureRenderTextures(int width, int height)
        {
            if (_allocatedWidth == width && _allocatedHeight == height && _voDepthRT != null)
                return;

            ReleaseRenderTextures();

            _allocatedWidth = width;
            _allocatedHeight = height;

            // 生深度テクスチャ用: RFloat (32bit float、深度値 1.0=近, 0.0=遠/背景)
            // ※ ComputeShader では Texture2D (SRV) として読むため enableRandomWrite は不要
            var descDepth = new RenderTextureDescriptor(width, height, RenderTextureFormat.RFloat, 24)
            {
                enableRandomWrite = false
            };
            _voDepthRT = new RenderTexture(descDepth) { name = "MeshDepth_VO_Depth" };
            _voDepthRT.Create();

            _handDepthRT = new RenderTexture(descDepth) { name = "MeshDepth_Hand_Depth" };
            _handDepthRT.Create();

            // 出力マスク用: ARGB32 (PNG 保存用)
            var descMask = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0)
            {
                enableRandomWrite = true
            };
            _gtVisibleMaskRT = new RenderTexture(descMask) { name = "MeshDepth_GTVisibleMask" };
            _gtVisibleMaskRT.Create();

            _gtOccludedMaskRT = new RenderTexture(descMask) { name = "MeshDepth_GTOccludedMask" };
            _gtOccludedMaskRT.Create();

            _voSilhouetteRT = new RenderTexture(descMask) { name = "MeshDepth_VOSilhouette" };
            _voSilhouetteRT.Create();

            _handSilhouetteRT = new RenderTexture(descMask) { name = "MeshDepth_HandSilhouette" };
            _handSilhouetteRT.Create();
        }

        private void EnsureResources()
        {
            if (meshDepthCaptureShader == null)
            {
                meshDepthCaptureShader = Shader.Find("Hidden/SICESI/MeshDepthCapture");
#if UNITY_EDITOR
                if (meshDepthCaptureShader == null)
                {
                    meshDepthCaptureShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>("Assets/Features/SICESI/Shaders/SICESI_MeshDepthCapture.shader");
                }
#endif
            }

            if (_depthCaptureMaterial == null && meshDepthCaptureShader != null)
            {
                _depthCaptureMaterial = new Material(meshDepthCaptureShader) { name = "SICESI_DepthCapture_Mat" };
            }

            if (meshDepthComputeShader == null)
            {
                meshDepthComputeShader = Resources.Load<ComputeShader>("SICESI_MeshDepthGT");
#if UNITY_EDITOR
                if (meshDepthComputeShader == null)
                {
                    meshDepthComputeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Features/SICESI/ComputeShaders/SICESI_MeshDepthGT.compute");
                }
#endif
            }
        }

        /// <summary>
        /// 指定カメラの視点・投影行列を用いて、特定 GameObject (および子階層) の生深度を RenderTexture にラスタライズします。
        /// </summary>
        private void RenderObjectDepth(Camera targetCamera, GameObject targetObj, RenderTexture outputRT)
        {
            if (targetCamera == null || targetObj == null || outputRT == null || _depthCaptureMaterial == null)
                return;

            CommandBuffer cmd = new CommandBuffer();
            cmd.name = $"DepthCapture_{targetObj.name}";

            cmd.SetRenderTarget(outputRT);
            cmd.SetViewport(new Rect(0, 0, outputRT.width, outputRT.height));
            // 背景クリア: カラーを (0, 0, 0, 1) = 深度 0.0 (最遠値・背景)、深度バッファを Far値 (Reversed-Z: 0.0, Standard-Z: 1.0) でクリア
            float clearDepth = SystemInfo.usesReversedZBuffer ? 0.0f : 1.0f;
            cmd.ClearRenderTarget(true, true, Color.black, clearDepth);

            // カメラの View 行列と GPU 補正済み Projection 行列を設定
            // ※ RenderTexture に描画するため GL.GetGPUProjectionMatrix の第2引数は true (RT用Y反転補正)
            Matrix4x4 viewMatrix = targetCamera.worldToCameraMatrix;
            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(targetCamera.projectionMatrix, true);

            // PCDRendererFeature が実際に点群投影に使用した最新の同一行列があれば同期適用
            if (PCDRendererFeature.TryGetCameraMatrices(targetCamera, out Matrix4x4 pcdView, out Matrix4x4 pcdProj))
            {
                viewMatrix = pcdView;
                // pcdProj は GL.GetGPUProjectionMatrix(proj, false) で生成されている (スクリーン描画用)
                // RT 描画用に m11 (Y軸スケール) の符号を反転して Y-flip を適用する
                gpuProj = pcdProj;
                gpuProj.m11 = -gpuProj.m11;
            }

            cmd.SetViewProjectionMatrices(viewMatrix, gpuProj);

            var renderers = targetObj.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r != null && r.enabled && r.gameObject.activeInHierarchy)
                {
                    for (int sub = 0; sub < r.sharedMaterials.Length; sub++)
                    {
                        cmd.DrawRenderer(r, _depthCaptureMaterial, sub);
                    }
                }
            }

            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
        }

        /// <summary>
        /// 指定されたカメラにおいて、仮想オブジェクトと手メッシュの生深度マップを直接ラスタライズ・数値比較し、
        /// 真のGT可視マスク、GT遮蔽マスク、仮想オブジェクトシルエット、手メッシュシルエットを生成・保存します。
        /// </summary>
        public IEnumerator GenerateMeshDepthGTRoutine(
            Camera targetCamera,
            string eyeTag,
            string outputDirectory,
            GameObject virtualObject,
            GameObject groundTruthObject,
            GameObject pointCloudObject,
            Action<bool, string> onComplete = null,
            string groundTruthCaptureLayer = "PCD")
        {
            if (targetCamera == null)
            {
                string errMsg = "[SICESI_MeshDepthGTManager] targetCamera が null です。";
                AppLogger.LogError(this, "SICESI", errMsg);
                onComplete?.Invoke(false, errMsg);
                yield break;
            }

            EnsureResources();

            if (meshDepthComputeShader == null || _depthCaptureMaterial == null)
            {
                string errMsg = "[SICESI_MeshDepthGTManager] ComputeShader または DepthCaptureShader が見つかりません。";
                AppLogger.LogError(this, "SICESI", errMsg);
                onComplete?.Invoke(false, errMsg);
                yield break;
            }

            int width = targetCamera.pixelWidth > 0 ? targetCamera.pixelWidth : Screen.width;
            int height = targetCamera.pixelHeight > 0 ? targetCamera.pixelHeight : Screen.height;

            EnsureRenderTextures(width, height);

            // 元のアクティブ状態を退避
            bool prevVoActive = virtualObject != null && virtualObject.activeSelf;
            bool prevGtActive = groundTruthObject != null && groundTruthObject.activeSelf;
            bool prevPtActive = pointCloudObject != null && pointCloudObject.activeSelf;

            try
            {
                // =============================================================
                // Step 1: 仮想オブジェクト単独の生深度を直接ラスタライズ
                // =============================================================
                AppLogger.Log(this, "SICESI", $"[MeshDepthGT] {eyeTag}: 仮想オブジェクトの生深度をラスタライズ中...");
                if (virtualObject != null)
                {
                    virtualObject.SetActive(true);
                    RenderObjectDepth(targetCamera, virtualObject, _voDepthRT);
                }
                else
                {
                    // VOが無い場合は全画面背景クリア (深度 0.0)
                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = _voDepthRT;
                    GL.Clear(true, true, Color.black, 0.0f);
                    RenderTexture.active = prev;
                }

                // =============================================================
                // Step 2: 手メッシュ単独の生深度を直接ラスタライズ
                // =============================================================
                AppLogger.Log(this, "SICESI", $"[MeshDepthGT] {eyeTag}: 手メッシュの生深度をラスタライズ中...");
                if (groundTruthObject != null)
                {
                    groundTruthObject.SetActive(true);
                    RenderObjectDepth(targetCamera, groundTruthObject, _handDepthRT);
                }
                else
                {
                    // 手メッシュが無い場合は全画面背景クリア (深度 0.0)
                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = _handDepthRT;
                    GL.Clear(true, true, Color.black, 0.0f);
                    RenderTexture.active = prev;
                }

                // =============================================================
                // Step 3: GPUコンピュートシェーダーによる深度直接比較
                // =============================================================
                AppLogger.Log(this, "SICESI", $"[MeshDepthGT] {eyeTag}: GPU深度直接比較を実行中 (FlipX={flipX}, FlipY={flipY})...");
                int kernel = meshDepthComputeShader.FindKernel("CompareMeshDepth");

                meshDepthComputeShader.SetVector("_ScreenParams", new Vector4(width, height, 1f / width, 1f / height));
                meshDepthComputeShader.SetInt("_FlipX", flipX ? 1 : 0);
                meshDepthComputeShader.SetInt("_FlipY", flipY ? 1 : 0);

                meshDepthComputeShader.SetTexture(kernel, "_VO_DepthMap", _voDepthRT);
                meshDepthComputeShader.SetTexture(kernel, "_Hand_DepthMap", _handDepthRT);

                meshDepthComputeShader.SetTexture(kernel, "_GTVisibleMask_RW", _gtVisibleMaskRT);
                meshDepthComputeShader.SetTexture(kernel, "_GTOccludedMask_RW", _gtOccludedMaskRT);
                meshDepthComputeShader.SetTexture(kernel, "_VOSilhouette_RW", _voSilhouetteRT);
                meshDepthComputeShader.SetTexture(kernel, "_HandSilhouette_RW", _handSilhouetteRT);

                int threadGroupsX = Mathf.CeilToInt(width / 8.0f);
                int threadGroupsY = Mathf.CeilToInt(height / 8.0f);
                meshDepthComputeShader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

                // =============================================================
                // Step 4: 結果マスクの PNG 保存
                // =============================================================
                Directory.CreateDirectory(outputDirectory);

                string tagSuffix = eyeTag.ToLower(); // "left" or "right"
                string visiblePath = Path.Combine(outputDirectory, $"gt_depth_visible_mask_{tagSuffix}.png");
                string occludedPath = Path.Combine(outputDirectory, $"gt_depth_occluded_mask_{tagSuffix}.png");
                string voPath = Path.Combine(outputDirectory, $"vo_silhouette_{tagSuffix}.png");
                string handPath = Path.Combine(outputDirectory, $"hand_silhouette_{tagSuffix}.png");

                // 既存の gt_visible_mask 互換パス (解析スクリプトが自動的に真のGTを使用するように配置)
                string legacyVisiblePath = Path.Combine(outputDirectory, $"gt_visible_mask_{tagSuffix}.png");
                string legacyGtPath = Path.Combine(outputDirectory, $"gt_{tagSuffix}.png");

                SaveRenderTextureToPNG(_gtVisibleMaskRT, visiblePath);
                SaveRenderTextureToPNG(_gtOccludedMaskRT, occludedPath);
                SaveRenderTextureToPNG(_voSilhouetteRT, voPath);
                SaveRenderTextureToPNG(_handSilhouetteRT, handPath);

                // 互換性担保: 既存パスにもコピー保存
                if (File.Exists(visiblePath))
                {
                    File.Copy(visiblePath, legacyVisiblePath, true);
                    File.Copy(visiblePath, legacyGtPath, true);
                }

                AppLogger.Log(this, "SICESI", $"[+] {eyeTag} 真のGTマスク保存完了: {visiblePath}");
                onComplete?.Invoke(true, visiblePath);
            }
            finally
            {
                // 元のアクティブ状態を 100% 復元
                if (virtualObject != null) virtualObject.SetActive(prevVoActive);
                if (groundTruthObject != null) groundTruthObject.SetActive(prevGtActive);
                if (pointCloudObject != null) pointCloudObject.SetActive(prevPtActive);
            }

            yield break;
        }

        private void SaveRenderTextureToPNG(RenderTexture rt, string destinationPath)
        {
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();

            RenderTexture.active = prevActive;

            byte[] pngBytes = tex.EncodeToPNG();
            Destroy(tex);

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            File.WriteAllBytes(destinationPath, pngBytes);
        }
    }
}
