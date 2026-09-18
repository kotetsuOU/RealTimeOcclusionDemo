using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// RenderGraph に対して、オクルージョン計算用の Compute Shader パス (UnsafePass) を
/// 登録・構築するビルダークラスです。
/// </summary>
internal class PCDComputePassBuilder
{
    public PCDRenderGraphHandles EnqueueComputePass(
        RenderGraph renderGraph,
        PCDContextBuilder.PreComputeData preData,
        PCDRendererFeature.PCDRenderSettings settings,
        PCDResourcePool resources,
        PCDPointBufferManager bufferManager,
        ComputeBuffer staticMeshCounterBuffer,
        PCDKernelRegistry kernels,
        ComputeShader computeShader,
        IPCDPipelineStage[] stages)
    {
        var outHandles = new PCDRenderGraphHandles();
        const string PROFILER_TAG = "PCDRendering";

        using (var builder = renderGraph.AddUnsafePass<PCDPipelineContext>(PROFILER_TAG, out var ctx))
        {
            builder.AllowGlobalStateModification(true);

            // --- コンテキストの構築 ---
            ctx.ComputeShader = computeShader;
            ctx.Settings = settings;
            ctx.Kernels = kernels;
            ctx.Resources = resources;
            ctx.ScreenWidth = preData.ScreenWidth;
            ctx.ScreenHeight = preData.ScreenHeight;
            ctx.ScreenParams = new Vector4(preData.ScreenWidth, preData.ScreenHeight, 0, 0);
            
            ctx.ViewMatrix = preData.ViewMatrix;
            ctx.ProjectionMatrix = preData.ProjectionMatrix;
            ctx.InverseProjectionMatrix = preData.InverseProjectionMatrix;

            int gs = (int)settings.gridSize;
            if (gs == 0) gs = 16;
            ctx.ThreadGroupsX = (preData.ScreenWidth + 7) / 8;
            ctx.ThreadGroupsY = (preData.ScreenHeight + 7) / 8;
            ctx.GridGroupsX = (preData.ScreenWidth + gs - 1) / gs;
            ctx.GridGroupsY = (preData.ScreenHeight + gs - 1) / gs;

            ctx.HasVirtualDepth = preData.HasVirtualDepth;
            ctx.HasVirtualObjects = preData.HasVirtualObjects;
            ctx.IsHalfMirrorEnabled = preData.IsHalfMirrorEnabled;
            ctx.StaticMeshCounterBuffer = staticMeshCounterBuffer;

            // --- バッファの設定 ---
            bool useExternal = bufferManager.UseExternalBuffer && bufferManager.ExternalPointBuffer != null;
            ctx.UseExternal = useExternal;
            if (useExternal)
            {
                ctx.ExternalBuffer = bufferManager.ExternalPointBuffer;
                ctx.ExternalCount = bufferManager.ExternalPointCount >= 0 ? bufferManager.ExternalPointCount : bufferManager.ExternalPointBuffer.count;
                ctx.InternalBuffer = bufferManager.PointBuffer;
                ctx.InternalCount = bufferManager.PointCount;
                ctx.CombinedBuffer = bufferManager.CombinedBuffer;
            }
            ctx.PointBuffer = preData.ActiveBuffer;
            ctx.PointCount = preData.ActiveCount;

            // --- 仮想深度テクスチャ ---
            if (preData.HasVirtualDepth)
            {
                ctx.VirtualDepthTexture = preData.ResourceData.cameraDepthTexture;
            }
            else
            {
                var virtualDepthFallbackDesc = new TextureDesc(1, 1)
                {
                    colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RFloat, false)
                };
                ctx.VirtualDepthTexture = renderGraph.CreateTexture(virtualDepthFallbackDesc);
            }
            builder.UseTexture(ctx.VirtualDepthTexture, AccessFlags.Read);

            // --- カメラカラーテクスチャ ---
            if (preData.HasVirtualDepth && preData.ResourceData.activeColorTexture.IsValid())
            {
                ctx.CameraColorTexture = preData.ResourceData.activeColorTexture;
            }
            else
            {
                var cameraColorFallbackDesc = new TextureDesc(1, 1)
                {
                    colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGBFloat, false)
                };
                ctx.CameraColorTexture = renderGraph.CreateTexture(cameraColorFallbackDesc);
            }
            builder.UseTexture(ctx.CameraColorTexture, AccessFlags.Read);

            // --- RenderGraph テクスチャインポート ---
            if (settings.recordIntegratedDepthMap)
            {
                outHandles.integratedDepthMap = renderGraph.ImportTexture(resources.IntegratedDepthMap);
            }

            if (settings.recordNeighborhoodMap)
            {
                outHandles.neighborhoodMap = renderGraph.ImportTexture(resources.NeighborhoodMap);
            }

            outHandles.neighborCountMap = renderGraph.ImportTexture(resources.NeighborCountMap);

            if (settings.recordOcclusionDebugMap || settings.recordPixelTagMap)
            {
                outHandles.occlusionValueMap = renderGraph.ImportTexture(resources.OcclusionValueMap);
            }

            bool isDebugDisplay = settings.enablePixelTagMap || settings.enableOcclusionMap
                || (settings.debugPatternId >= 0 && settings.debugPatternId < 256)
                || (settings.debugSectorId >= 0 && settings.debugSectorId <= 9);
            if (isDebugDisplay)
            {
                outHandles.debugDisplayMap = renderGraph.ImportTexture(resources.DebugDisplayMap);
            }

            bool useHoleFilling = settings.holeFillingMethod != PCDRendererFeature.PCD_HoleFillingMethod.None;
            outHandles.finalImage = renderGraph.ImportTexture(useHoleFilling ? resources.FinalImage : resources.OcclusionResultMap);

            // --- Compute 実行関数の登録 ---
            builder.SetRenderFunc((PCDPipelineContext passCtx, UnsafeGraphContext graphCtx) =>
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(graphCtx.cmd);
                foreach (var stage in stages)
                {
                    if (stage.ShouldExecute(passCtx))
                        stage.Execute(cmd, passCtx);
                }
            });

            // デバッグデータを非同期読込する場合はカリングを無効化
            if (settings.recordOcclusionDebugMap || settings.recordPixelTagMap || settings.recordIntegratedDepthMap || settings.recordNeighborhoodMap || settings.recordNeighborCountMap)
            {
                builder.AllowPassCulling(false);
            }
        }

        return outHandles;
    }
}
