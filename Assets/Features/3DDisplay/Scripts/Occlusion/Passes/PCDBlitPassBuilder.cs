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
    /// <summary>
    /// 診断用: 整数画素座標 Load() による無損失転送モードの有効化フラグ。
    /// false の場合は URP デフォルトの通常バイリニア Blitter.BlitTexture を実行します。
    /// </summary>
    public static bool LosslessBlitMode = false;
    private static Material s_losslessBlitMaterial;

    private static Material GetLosslessBlitMaterial()
    {
        if (s_losslessBlitMaterial == null)
        {
            Shader shader = Shader.Find("Hidden/SICESI/LosslessPointBlit");
            if (shader != null)
            {
                s_losslessBlitMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
        }
        return s_losslessBlitMaterial;
    }

    private class BlitPassData
    {
        internal TextureHandle sourceImage;
        internal TextureHandle cameraTarget;
        internal bool enablePixelTagMap;
        internal bool enableOcclusionMap;
        internal bool useDirectGpuImageBuffer;
        internal RTHandle directGpuImageMap;
        internal bool useLosslessBlit;
        internal Material losslessMaterial;
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
            data.useLosslessBlit = LosslessBlitMode;
            data.losslessMaterial = LosslessBlitMode ? GetLosslessBlitMaterial() : null;

            bool isDebugDisplay = data.enablePixelTagMap || data.enableOcclusionMap
                || (settings.debugPatternId >= 0 && settings.debugPatternId < 256)
                || (settings.debugSectorId >= 0 && settings.debugSectorId <= 9);
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
                if (passData.useLosslessBlit && passData.losslessMaterial != null)
                {
                    passData.losslessMaterial.SetFloat("_FlipX", 0.0f);
                    Blitter.BlitTexture(context.cmd, passData.sourceImage, new Vector4(1, 1, 0, 0), passData.losslessMaterial, 0);
                }
                else
                {
                    Blitter.BlitTexture(context.cmd, passData.sourceImage, new Vector2(1, 1), 0.0f, false);
                }
            });
        }
    }
}
