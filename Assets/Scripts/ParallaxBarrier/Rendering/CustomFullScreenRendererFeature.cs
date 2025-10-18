using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class CustomFullcreenRendererFeature : ScriptableRendererFeature
{
    public Shader shader;
    [SerializeField] private Color color1 = Color.white;
    [SerializeField] private Color color2 = Color.black;

    private Material material;
    private CustomFullScreenPass renderPass = null;

    public override void Create()
    {
        if (shader != null)
        {
            material = CoreUtils.CreateEngineMaterial(shader);
            renderPass = new CustomFullScreenPass(material);
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Game && renderPass != null)
        {
            renderPass.color1 = color1;
            renderPass.color2 = color2;

            renderer.EnqueuePass(renderPass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
        base.Dispose(disposing);
    }
}