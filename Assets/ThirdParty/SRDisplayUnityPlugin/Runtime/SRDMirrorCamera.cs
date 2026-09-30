using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using SRD.Utils;

namespace SRD.Core
{
    /// <summary>
    /// SRDの実カメラに対し、鏡面反射の幾何モデル・カリング・行列修正を適用するコンポーネント。
    /// デバッグログ出力は独立した SRDMirrorDebugLogger コンポーネントへ完全に移譲しています。
    /// </summary>
    [ExecuteAlways]
    public class SRDMirrorCamera : MonoBehaviour
    {
        [Header("Mirror Options")]
        [Tooltip("鏡像処理を有効にする")]
        public bool enableMirror = true;

        private SRDManager srdManager;

        private Dictionary<Camera, Matrix4x4> _originalViewMatrices = new Dictionary<Camera, Matrix4x4>();
        private Dictionary<Camera, Matrix4x4> _originalCullMatrices = new Dictionary<Camera, Matrix4x4>();
        private Dictionary<Camera, int> _originalCullingMasks = new Dictionary<Camera, int>();
        private Dictionary<Camera, bool> _originalInvertCulling = new Dictionary<Camera, bool>();
        private Dictionary<Camera, CameraClearFlags> _originalClearFlags = new Dictionary<Camera, CameraClearFlags>();
        private Dictionary<Camera, Color> _originalBackgroundColors = new Dictionary<Camera, Color>();

        private void EnsureDebugLogger()
        {
            if (GetComponent<SRDMirrorDebugLogger>() == null)
            {
                gameObject.AddComponent<SRDMirrorDebugLogger>();
            }
        }

        void Awake()
        {
            EnsureDebugLogger();

#if UNITY_2019_1_OR_NEWER
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
#endif
        }

        void Start()
        {
            srdManager = GetComponentInParent<SRDManager>();
            if (srdManager == null)
            {
                srdManager = FindFirstObjectByType<SRDManager>();
            }

            EnsureDebugLogger();
        }

        void Update()
        {
            SRDStereoCompositer.FlipRenderTextureX = enableMirror;
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || !enableMirror || srdManager == null) return;

            Camera[] cameras = GetComponentsInChildren<Camera>(true);
            foreach (var cam in cameras)
            {
                if (cam == null || !cam.enabled) continue;
                if (!IsTargetEyeCamera(cam)) continue;

                // URP のフレームレンダリング開始前に行列 (worldToCameraMatrix, cullingMatrix) を確実に更新
                // ※ GL.invertCulling はここでは触らず、描画直前の OnBeginCameraRendering でのみ設定します
                ApplyMirrorMatricesToCamera(cam);
            }
        }

        private void OnValidate()
        {
            if (!enableMirror)
            {
                RestoreAllCameras();
            }
        }

        void OnDestroy()
        {
#if UNITY_2019_1_OR_NEWER
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
#endif
            RestoreAllCameras();
        }

        void OnDisable()
        {
            RestoreAllCameras();
        }

        private bool IsTargetEyeCamera(Camera cam)
        {
            if (cam == null) return false;
            string camName = cam.name;
            return camName.IndexOf("LeftEyeCamera", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   camName.IndexOf("RightEyeCamera", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RestoreAllCameras()
        {
            foreach (var kvp in _originalViewMatrices)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.worldToCameraMatrix = kvp.Value;
                    kvp.Key.ResetStereoViewMatrices();
                }
            }
            foreach (var kvp in _originalCullMatrices)
            {
                if (kvp.Key != null) kvp.Key.ResetCullingMatrix();
            }
            foreach (var kvp in _originalCullingMasks)
            {
                if (kvp.Key != null) kvp.Key.cullingMask = kvp.Value;
            }
            foreach (var kvp in _originalClearFlags)
            {
                if (kvp.Key != null) kvp.Key.clearFlags = kvp.Value;
            }
            foreach (var kvp in _originalBackgroundColors)
            {
                if (kvp.Key != null) kvp.Key.backgroundColor = kvp.Value;
            }

            GL.invertCulling = false;
            SRDStereoCompositer.FlipRenderTextureX = false;

            _originalViewMatrices.Clear();
            _originalCullMatrices.Clear();
            _originalCullingMasks.Clear();
            _originalInvertCulling.Clear();
            _originalClearFlags.Clear();
            _originalBackgroundColors.Clear();
        }

#if UNITY_2019_1_OR_NEWER
        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (cam == null) return;

            // 1. 対象の SRD 左右目カメラの場合: 描画の瞬間のみ GL.invertCulling = true を適用
            if (IsTargetEyeCamera(cam))
            {
                if (Application.isPlaying && enableMirror && srdManager != null)
                {
                    // 描画直前の最終確認として行列およびカリング反転を適用
                    ApplyMirrorMatricesToCamera(cam);
                    GL.invertCulling = true;
                }
                else
                {
                    GL.invertCulling = false;
                }
            }
            else
            {
                // 2. 非対象カメラ (通常 URP カメラ, SceneView, Inspector Preview など):
                // カリング反転が誤って適用されないよう確実に false を保証
                if (GL.invertCulling)
                {
                    GL.invertCulling = false;
                }
            }
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (cam == null) return;

            if (IsTargetEyeCamera(cam))
            {
                // 対象カメラのレンダリングが終了した瞬間、無条件で GL.invertCulling を false に即時復帰
                GL.invertCulling = false;
            }
            else
            {
                // 非対象カメラ終了時も常にカリング反転を確実に false に維持
                if (GL.invertCulling)
                {
                    GL.invertCulling = false;
                }
            }
        }
#endif

        private void ApplyMirrorMatricesToCamera(Camera cam)
        {
            // バックアップ (未バックアップ時のみ)
            if (!_originalViewMatrices.ContainsKey(cam)) _originalViewMatrices[cam] = cam.worldToCameraMatrix;
            if (!_originalCullMatrices.ContainsKey(cam)) _originalCullMatrices[cam] = cam.cullingMatrix;
            if (!_originalCullingMasks.ContainsKey(cam)) _originalCullingMasks[cam] = cam.cullingMask;
            if (!_originalClearFlags.ContainsKey(cam)) _originalClearFlags[cam] = cam.clearFlags;
            if (!_originalBackgroundColors.ContainsKey(cam)) _originalBackgroundColors[cam] = cam.backgroundColor;

            // 1. View Matrix の設定 (Case B Mirrored Basis)
            cam.worldToCameraMatrix = CalculateCaseBViewMatrix(cam, isRigid: false);

            // 2. Projection Matrix の設定
            // SRDEyeViewRenderer が設定したオフアキシス投影を尊重し、改変しない。

            // 3. cullingMatrix の強制上書き（CPUカリング用）
            cam.cullingMatrix = cam.projectionMatrix * CalculateCaseBViewMatrix(cam, isRigid: true);
        }

        private Matrix4x4 CalculateCaseBViewMatrix(Camera cam, bool isRigid)
        {
            Vector3 localPos = srdManager.transform.InverseTransformPoint(cam.transform.position);
            localPos.x = -localPos.x;
            Vector3 mirroredWorldPos = srdManager.transform.TransformPoint(localPos);

            Vector3 srdRight = srdManager.transform.right;
            Vector3 mirroredForward = Vector3.Reflect(cam.transform.forward, srdRight).normalized;
            Vector3 mirroredUp = Vector3.Reflect(cam.transform.up, srdRight).normalized;
            Vector3 mirroredRight = isRigid ? Vector3.Cross(mirroredUp, mirroredForward).normalized : Vector3.Reflect(cam.transform.right, srdRight).normalized;

            Matrix4x4 C_mirrored = Matrix4x4.identity;
            C_mirrored.SetColumn(0, new Vector4(mirroredRight.x, mirroredRight.y, mirroredRight.z, 0f));
            C_mirrored.SetColumn(1, new Vector4(mirroredUp.x, mirroredUp.y, mirroredUp.z, 0f));
            C_mirrored.SetColumn(2, new Vector4(mirroredForward.x, mirroredForward.y, mirroredForward.z, 0f));
            C_mirrored.SetColumn(3, new Vector4(mirroredWorldPos.x, mirroredWorldPos.y, mirroredWorldPos.z, 1f));

            return Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * C_mirrored.inverse;
        }
    }
}
