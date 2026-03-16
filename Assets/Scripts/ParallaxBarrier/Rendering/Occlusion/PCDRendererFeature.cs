using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class PCDRendererFeature : ScriptableRendererFeature
{
    public static PCDRendererFeature Instance { get; private set; }

    [System.Serializable]
    public struct PCDRenderSettings
    {
        public float densityThreshold_e;
        public float neighborhoodParam_p_prime;
        public bool enableGradientCorrection;
        public float gradientThreshold_g_th;
        [Range(0f, 1f)] public float occlusionThreshold;
        [Range(0f, 1f)] public float occlusionFadeWidth;
        public bool enableOriginDebugMap;
    }

    private class RegisteredObject
    {
        public Mesh mesh;
        public Transform transform;
        public PCDProcessingMode mode;
    }

    [Header("Required Assets")]
    public ComputeShader pointCloudCompute;

    [Header("Algorithm Parameters")]
    [Tooltip("密度計算に用いる深度のしきい値 e")]
    public float densityThreshold_e = 0.04f;

    [Tooltip("近傍領域サイズを決定するための調整パラメータ p' ")]
    public float neighborhoodParam_p_prime = 4.8f;

    [Header("Gradient Correction")]
    [Tooltip("勾配を用いた補正を有効にする")]
    public bool enableGradientCorrection = true;

    [Tooltip("勾配しきい値 g_th")]
    public float gradientThreshold_g_th = 0.05f;

    [Header("Occlusion Filtering")]
    [Tooltip("オクルージョン判定のしきい値 (論文 2.4.2節)")]
    [Range(0f, 1f)]
    public float occlusionThreshold = 0.8f;

    [Tooltip("境界を滑らかにするためのフェード幅（閾値からの減衰範囲）")]
    [Range(0f, 1f)]
    public float occlusionFadeWidth = 0.1f;

    [Header("Blending Assets")]
    [Tooltip("最終結果のアルファブレンドを有効にするか")]
    public bool enableAlphaBlend = true;
    public Material blendMaterial;

    [Header("Layer & Bounds Optimization")]
    [Tooltip("PCDを描画するための専用レイヤー")]
    public LayerMask pcdLayer;
    [Tooltip("登録時に自動的にレイヤーを変更するか")]
    public bool autoSetLayer = true;
    [Tooltip("カリング防止のためにBoundsを拡張するか")]
    public bool expandBounds = true;
    [Tooltip("拡張するBoundsのサイズ")]
    public float boundsSize = 10000f;

    [Header("Debug")]
    [Tooltip("点群(黒)と静的メッシュ(白)の由来を示すデバッグマップを有効にします")]
    public bool enableOriginDebugMap = false;

    private PCDRenderPass _scriptablePass;

    private bool _useGlobalBufferMode = false;
    public bool IsGlobalBufferMode => _useGlobalBufferMode;

    private static List<RegisteredObject> _persistentObjects = new List<RegisteredObject>();

    public void SetUseGlobalBuffer(bool enable)
    {
        _useGlobalBufferMode = enable;
    }

    private PCDRenderSettings GetSettings()
    {
        return new PCDRenderSettings
        {
            densityThreshold_e = this.densityThreshold_e,
            neighborhoodParam_p_prime = this.neighborhoodParam_p_prime,
            enableGradientCorrection = this.enableGradientCorrection,
            gradientThreshold_g_th = this.gradientThreshold_g_th,
            occlusionThreshold = this.occlusionThreshold,
            occlusionFadeWidth = this.occlusionFadeWidth,
            enableOriginDebugMap = this.enableOriginDebugMap
        };
    }

    public override void Create()
    {
        Instance = this;

        _scriptablePass = new PCDRenderPass(this.pointCloudCompute, GetSettings(), blendMaterial, enableAlphaBlend);
        _scriptablePass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        SyncPersistentObjectsToPass();
    }

    private void SyncPersistentObjectsToPass()
    {
        if (_scriptablePass == null) return;

        for (int i = _persistentObjects.Count - 1; i >= 0; i--)
        {
            var obj = _persistentObjects[i];
            if (obj.mesh != null && obj.transform != null)
            {
                _scriptablePass.AddStaticMesh(obj.mesh, obj.transform, obj.mode);
            }
            else
            {
                _persistentObjects.RemoveAt(i);
            }
        }
    }

    public void AddStaticMesh(Mesh mesh, Transform transform, PCDProcessingMode mode)
    {
        if (mesh == null || transform == null) return;

        var existing = _persistentObjects.Find(x => x.mesh == mesh && x.transform == transform);
        if (existing == null)
        {
            _persistentObjects.Add(new RegisteredObject { mesh = mesh, transform = transform, mode = mode });
        }
        else
        {
            existing.mode = mode;
        }

        ApplySettings(mesh, transform);

        _scriptablePass?.AddStaticMesh(mesh, transform, mode);
    }

    public void RemoveStaticMesh(Mesh mesh, Transform transform)
    {
        _persistentObjects.RemoveAll(x => x.mesh == mesh && x.transform == transform);
        _scriptablePass?.RemoveStaticMesh(mesh, transform);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        EnforceSettingsEveryFrame();

        if (pointCloudCompute == null || (enableAlphaBlend && !enableOriginDebugMap && blendMaterial == null))
        {
            return;
        }

        if (_scriptablePass != null)
        {
            _scriptablePass.UpdateSettings(GetSettings());
            _scriptablePass.SetDebugFlag(enableOriginDebugMap);
        }

        // Always enqueue the pass - let RecordRenderGraph decide what to do
        // The pass will handle early returns internally if needed
        renderer.EnqueuePass(_scriptablePass);
    }

    private void EnforceSettingsEveryFrame()
    {
        for (int i = _persistentObjects.Count - 1; i >= 0; i--)
        {
            var obj = _persistentObjects[i];
            if (obj.mesh == null || obj.transform == null)
            {
                _persistentObjects.RemoveAt(i);
                continue;
            }
            ApplySettings(obj.mesh, obj.transform);
        }
    }

    private void ApplySettings(Mesh mesh, Transform transform)
    {
        if (expandBounds && mesh != null)
        {
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * boundsSize);
        }

        if (autoSetLayer && transform != null)
        {
            int layerIndex = 0;
            int mask = pcdLayer.value;
            while (mask > 1) { mask >>= 1; layerIndex++; }

            if (layerIndex >= 0 && layerIndex < 32 && transform.gameObject.layer != layerIndex)
            {
                transform.gameObject.layer = layerIndex;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _scriptablePass?.Cleanup();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void SetPointCloudData(PCV_Data data)
    {
        if (_scriptablePass != null)
        {
            _scriptablePass.SetPointCloudData(data);
        }
    }

    public void SetExternalBuffer(ComputeBuffer buffer, int count)
    {
        if (_scriptablePass != null)
        {
            _scriptablePass.SetExternalBuffer(buffer, count);
        }
    }

    public Texture GetOriginDebugMap() => _scriptablePass?.GetOriginDebugMap();
}