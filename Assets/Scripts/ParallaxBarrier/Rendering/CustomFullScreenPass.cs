using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class CustomFullScreenPass : ScriptableRenderPass
{
    private const string PROFILER_TAG = "CustomFullScreenPass";

    private Material material;
    public Color color1 = Color.white;
    public Color color2 = Color.black;

    private class PassData
    {
        internal Material material;
        internal Color colorToUse;
        internal TextureHandle cameraTarget;
    }

    public CustomFullScreenPass(Material material)
    {
        this.material = material;
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (material == null) return;

        var resourceData = frameData.Get<UniversalResourceData>();

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(PROFILER_TAG, out var data))
        {
            data.material = material;
            data.colorToUse = UnityEngine.Application.isPlaying
                ? (Time.frameCount % 2 == 0 ? color1 : color2)
                : color1;
            data.cameraTarget = resourceData.activeColorTexture;

            builder.SetRenderAttachment(data.cameraTarget, 0, AccessFlags.Write);

            builder.SetRenderFunc((PassData passData, RasterGraphContext context) =>
            {
                passData.material.SetColor("_Color", passData.colorToUse);
                context.cmd.DrawProcedural(Matrix4x4.identity, passData.material, 0, MeshTopology.Quads, 4, 1);
            });
        }
    }
}