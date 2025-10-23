using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class PCDRendererFeature : ScriptableRendererFeature
{
    public static PCDRendererFeature Instance { get; private set; }

    [Header("Required Assets")]
    public ComputeShader pointCloudCompute;

    [Header("Algorithm Parameters (from Paper)")]
    [Tooltip("密度計算に用いる深度のしきい値")]
    public float densityThreshold_e = 0.04f;

    [Tooltip("近傍領域サイズを決定するための調整パラメータ")]
    public float neighborhoodParam_p_prime = 4.8f;

    [Header("Blending Assets")]
    [Tooltip("最終結果のアルファブレンドを有効にするか")]
    public bool enableAlphaBlend = true;
    public Material blendMaterial;

    private PCDRenderPass _scriptablePass;

    public override void Create()
    {
        Instance = this;
        _scriptablePass = new PCDRenderPass(this, blendMaterial, enableAlphaBlend);
        _scriptablePass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public void SetPointCloudData(PCV_Data data)
    {
        if (_scriptablePass != null)
        {
            _scriptablePass.SetPointCloudData(data);
        }
    }

    public void AddStaticMesh(Mesh mesh, Transform transform)
    {
        if (_scriptablePass != null)
        {
            _scriptablePass.AddStaticMesh(mesh, transform);
        }
    }

    public void RemoveStaticMesh(Mesh mesh, Transform transform)
    {
        if (_scriptablePass != null)
        {
            _scriptablePass.RemoveStaticMesh(mesh, transform);
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (pointCloudCompute == null)
        {
            UnityEngine.Debug.LogWarningFormat("PCDRendererFeature: Compute Shader is not assigned. Skipping pass.");
            return;
        }

        if (enableAlphaBlend && blendMaterial == null)
        {
            UnityEngine.Debug.LogWarningFormat("PCDRendererFeature: Blend Material is not assigned (but blending is enabled). Skipping pass.");
            return;
        }

        renderer.EnqueuePass(_scriptablePass);
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
}