using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// オクルージョン計算済みの結果マップ（またはデバッグマップ）を
/// カメラのターゲットカラーテクスチャへ Blit 描画するパスを構築するビルダークラスです。
/// </summary>
internal class PCDBlitPassBuilder
{
    private class BlitPassData
    {
        internal TextureHandle sourceImage;
        internal TextureHandle cameraTarget;
        internal bool enablePixelTagMap;
        internal bool enableOcclusionMap;
        internal bool useDirectGpuImageBuffer;
        internal RTHandle directGpuImageMap;
    }

    public void EnqueueBlitPass(
        RenderGraph renderGraph,
        UniversalResourceData resourceData,
        PCDRendererFeature.PCDRenderSettings settings,
        PCDRenderGraphHandles handles)
    {
        using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>("PCD Blit Pass", out var data))
        {
            data.cameraTarget = resourceData.activeColorTexture;
            data.directGpuImageMap = null;
            data.enablePixelTagMap = settings.enablePixelTagMap;
            data.enableOcclusionMap = settings.enableOcclusionMap;
            data.useDirectGpuImageBuffer = false;

            bool isDebugDisplay = data.enablePixelTagMap || data.enableOcclusionMap
                || (settings.debugPatternId >= 0 && settings.debugPatternId < 256);
            if (isDebugDisplay)
            {
                data.sourceImage = handles.debugDisplayMap;
                builder.UseTexture(data.sourceImage, AccessFlags.Read);
            }
            else
            {
                data.sourceImage = handles.finalImage;
                builder.UseTexture(data.sourceImage, AccessFlags.Read);
            }

            builder.SetRenderAttachment(data.cameraTarget, 0, AccessFlags.ReadWrite);
            builder.SetRenderFunc((BlitPassData passData, RasterGraphContext context) =>
            {
                Blitter.BlitTexture(context.cmd, passData.sourceImage, new Vector2(1, 1), 0.0f, false);
            });
        }
    }
}
