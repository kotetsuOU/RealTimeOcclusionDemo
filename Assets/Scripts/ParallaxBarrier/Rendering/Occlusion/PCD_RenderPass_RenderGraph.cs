using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

public partial class PCDRenderPass
{
    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (!_isInitialized) Initialize();
        if (!_isInitialized) return;
        if (!UnityEngine.Application.isPlaying) return;

        bool shouldUseExternal = PCDRendererFeature.Instance.IsGlobalBufferMode;

        if (shouldUseExternal && RsGlobalPointCloudManager.Instance != null)
        {
            var globalBuffer = RsGlobalPointCloudManager.Instance.GetGlobalBuffer();
            var globalCount = RsGlobalPointCloudManager.Instance.CurrentTotalCount;
            _bufferManager.SetExternalBuffer(globalBuffer, globalCount);
        }
        else
        {
            _bufferManager.SetExternalBuffer(null, 0);
        }

        _bufferManager.Update();

        ComputeBuffer activeBuffer = null;
        int activeCount = 0;

        if (_bufferManager.UseExternalBuffer && _bufferManager.ExternalPointCount > 0)
        {
            if (_bufferManager.PointCount > 0)
            {
                int totalCount = _bufferManager.ExternalPointCount + _bufferManager.PointCount;
                _bufferManager.EnsureCombinedBuffer(totalCount);
                activeBuffer = _bufferManager.CombinedBuffer;
                activeCount = totalCount;
            }
            else
            {
                activeBuffer = _bufferManager.ExternalPointBuffer;
                activeCount = _bufferManager.ExternalPointCount;
            }
        }
        else
        {
            activeBuffer = _bufferManager.PointBuffer;
            activeCount = _bufferManager.PointCount;
        }

        bool hasDepthMapMeshes = _bufferManager.HasDepthMapMeshes();
        bool hasPointCloudMeshes = _bufferManager.HasPointCloudMeshes();
        bool depthMapOnlyMode = hasDepthMapMeshes && !hasPointCloudMeshes && (activeBuffer == null || activeCount == 0 || !activeBuffer.IsValid());

        if (depthMapOnlyMode)
        {
            return;
        }

        if (activeBuffer == null || activeCount == 0 || !activeBuffer.IsValid())
        {
            UnityEngine.Debug.LogWarning("[PCDRenderPass] Early return: No point cloud data");
            return;
        }

        var cameraData = frameData.Get<UniversalCameraData>();
        var resourceData = frameData.Get<UniversalResourceData>();
        Camera camera = cameraData.camera;
        int screenWidth = camera.pixelWidth;
        int screenHeight = camera.pixelHeight;
        int gridWidth = Mathf.CeilToInt(screenWidth / 16.0f);
        int gridHeight = Mathf.CeilToInt(screenHeight / 16.0f);
        int l1_Width = 1, l1_Height = 1, l2_Width = 1, l2_Height = 1, l3_Width = 1, l3_Height = 1, l4_Width = 1, l4_Height = 1;

        if (_settings.enableGradientCorrection)
        {
            l1_Width = Mathf.Max(1, Mathf.CeilToInt(screenWidth / 2.0f));
            l1_Height = Mathf.Max(1, Mathf.CeilToInt(screenHeight / 2.0f));
            l2_Width = Mathf.Max(1, Mathf.CeilToInt(l1_Width / 2.0f));
            l2_Height = Mathf.Max(1, Mathf.CeilToInt(l1_Height / 2.0f));
            l3_Width = Mathf.Max(1, Mathf.CeilToInt(l2_Width / 2.0f));
            l3_Height = Mathf.Max(1, Mathf.CeilToInt(l2_Height / 2.0f));
            l4_Width = Mathf.Max(1, Mathf.CeilToInt(l3_Width / 2.0f));
            l4_Height = Mathf.Max(1, Mathf.CeilToInt(l3_Height / 2.0f));
        }

        if (_settings.enableOriginDebugMap)
        {
            if (_originDebugMapHandle == null || _originDebugMapHandle.rt == null || _originDebugMapHandle.rt.width != screenWidth || _originDebugMapHandle.rt.height != screenHeight)
            {
                _originDebugMapHandle?.Release();
                var desc = new RenderTextureDescriptor(screenWidth, screenHeight, GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGBFloat, false), 0);
                desc.enableRandomWrite = true;
                _originDebugMapHandle = RTHandles.Alloc(desc, name: "_OriginDebugMap");
            }
        }

        TextureHandle finalImageHandle;
        TextureHandle originDebugMapHandle_RG = default;

        using (var builder = renderGraph.AddComputePass<ComputePassData>(PROFILER_TAG, out var data))
        {
            data.computeShader = pointCloudCompute;
            data.pointCount = activeCount;
            data.screenParams = new Vector4(screenWidth, screenHeight, 0, 0);
            data.viewMatrix = camera.worldToCameraMatrix;
            data.projectionMatrix = camera.projectionMatrix;
            data.settings = _settings;
            data.kernelClear = _kernelClear;
            data.kernelProject = _kernelProject;
            data.kernelCalcGridZMin = _kernelCalcGridZMin;
            data.kernelCalcDensity = _kernelCalcDensity;
            data.kernelCalcGridLevel = _kernelCalcGridLevel;
            data.kernelGridMedianFilter = _kernelGridMedianFilter;
            data.kernelCalcNeighborhoodSize = _kernelCalcNeighborhoodSize;
            data.kernelBuildDepthPyramidL1 = _kernelBuildDepthPyramidL1;
            data.kernelBuildDepthPyramidL2 = _kernelBuildDepthPyramidL2;
            data.kernelBuildDepthPyramidL3 = _kernelBuildDepthPyramidL3;
            data.kernelBuildDepthPyramidL4 = _kernelBuildDepthPyramidL4;
            data.kernelApplyGradient = _kernelApplyGradient;
            data.kernelOcclusion = _kernelOcclusion;
            data.kernelInterpolate = _kernelInterpolate;
            data.kernelMerge = _kernelMerge;
            data.kernelInitFromCamera = _kernelInitFromCamera;
            data.useExternal = _bufferManager.UseExternalBuffer;
            data.externalBuffer = _bufferManager.ExternalPointBuffer;
            data.internalBuffer = _bufferManager.PointBuffer;
            data.externalCount = _bufferManager.ExternalPointCount;
            data.internalCount = _bufferManager.PointCount;
            data.combinedBuffer = _bufferManager.CombinedBuffer;
            data.pointBuffer = activeBuffer;
            data.hasVirtualDepth = depthMapOnlyMode && resourceData.cameraDepthTexture.IsValid();
            data.depthMapOnlyMode = depthMapOnlyMode;
            data.inverseProjectionMatrix = camera.projectionMatrix.inverse;

            if (data.hasVirtualDepth || depthMapOnlyMode)
            {
                data.virtualDepthTexture = resourceData.cameraDepthTexture;
            }
            else
            {
                var virtualDepthFallbackDesc = new TextureDesc(1, 1)
                {
                    colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RFloat, false)
                };
                data.virtualDepthTexture = renderGraph.CreateTexture(virtualDepthFallbackDesc);
            }
            builder.UseTexture(data.virtualDepthTexture, AccessFlags.Read);

            if (data.hasVirtualDepth && resourceData.activeColorTexture.IsValid())
            {
                data.cameraColorTexture = resourceData.activeColorTexture;
            }
            else
            {
                var cameraColorFallbackDesc = new TextureDesc(1, 1)
                {
                    colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGBFloat, false)
                };
                data.cameraColorTexture = renderGraph.CreateTexture(cameraColorFallbackDesc);
            }
            builder.UseTexture(data.cameraColorTexture, AccessFlags.Read);

            var desc = new TextureDesc(screenWidth, screenHeight) { enableRandomWrite = true };
            desc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGBFloat, false);
            data.colorMap = renderGraph.CreateTexture(desc);
            data.viewPositionMap = renderGraph.CreateTexture(desc);
            desc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RInt, false);
            data.depthMap = renderGraph.CreateTexture(desc);
            data.neighborhoodSizeMap = renderGraph.CreateTexture(desc);
            data.correctedNeighborhoodSizeMap = renderGraph.CreateTexture(desc);
            data.originTypeMap = renderGraph.CreateTexture(desc);

            var gridDesc = new TextureDesc(gridWidth, gridHeight) { enableRandomWrite = true };
            gridDesc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RInt, false);
            data.gridZMinMap = renderGraph.CreateTexture(gridDesc);
            data.gridLevelMap = renderGraph.CreateTexture(gridDesc);
            data.filteredGridLevelMap = renderGraph.CreateTexture(gridDesc);
            gridDesc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RFloat, false);
            data.densityMap = renderGraph.CreateTexture(gridDesc);

            if (data.settings.enableGradientCorrection)
            {
                var descL1 = new TextureDesc(l1_Width, l1_Height) { enableRandomWrite = true, colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RInt, false) };
                data.depthPyramidL1 = renderGraph.CreateTexture(descL1);
                var descL2 = new TextureDesc(l2_Width, l2_Height) { enableRandomWrite = true, colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RInt, false) };
                data.depthPyramidL2 = renderGraph.CreateTexture(descL2);
                var descL3 = new TextureDesc(l3_Width, l3_Height) { enableRandomWrite = true, colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RInt, false) };
                data.depthPyramidL3 = renderGraph.CreateTexture(descL3);
                var descL4 = new TextureDesc(l4_Width, l4_Height) { enableRandomWrite = true, colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RInt, false) };
                data.depthPyramidL4 = renderGraph.CreateTexture(descL4);
            }

            desc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGBFloat, false);
            data.occlusionResultMap = renderGraph.CreateTexture(desc);
            data.finalImage = renderGraph.CreateTexture(desc);

            if (data.settings.enableOriginDebugMap)
            {
                originDebugMapHandle_RG = renderGraph.ImportTexture(_originDebugMapHandle);
                data.originDebugMap = originDebugMapHandle_RG;
            }
            else
            {
                data.originDebugMap = renderGraph.CreateTexture(desc);
            }

            builder.UseTexture(data.colorMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.depthMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.viewPositionMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.gridZMinMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.densityMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.gridLevelMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.filteredGridLevelMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.neighborhoodSizeMap, AccessFlags.ReadWrite);
            if (data.settings.enableGradientCorrection)
            {
                builder.UseTexture(data.depthPyramidL1, AccessFlags.ReadWrite);
                builder.UseTexture(data.depthPyramidL2, AccessFlags.ReadWrite);
                builder.UseTexture(data.depthPyramidL3, AccessFlags.ReadWrite);
                builder.UseTexture(data.depthPyramidL4, AccessFlags.ReadWrite);
            }
            builder.UseTexture(data.correctedNeighborhoodSizeMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.occlusionResultMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.finalImage, AccessFlags.ReadWrite);
            builder.UseTexture(data.originTypeMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.originDebugMap, AccessFlags.ReadWrite);

            finalImageHandle = data.finalImage;

            builder.SetRenderFunc((ComputePassData passData, ComputeGraphContext context) =>
            {
                ExecuteComputePass(passData, context);
            });
        }

        using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>("PCD Blit Pass", out var data))
        {
            data.blendMaterial = m_BlendMaterial;
            data.enableAlphaBlend = _enableAlphaBlend;
            data.cameraTarget = resourceData.activeColorTexture;
            data.enableOriginDebugMap = _settings.enableOriginDebugMap;

            if (data.enableOriginDebugMap)
            {
                data.sourceImage = originDebugMapHandle_RG;
                builder.UseTexture(data.sourceImage, AccessFlags.Read);
            }
            else
            {
                data.sourceImage = finalImageHandle;
                builder.UseTexture(data.sourceImage, AccessFlags.Read);
            }

            builder.SetRenderAttachment(data.cameraTarget, 0, AccessFlags.ReadWrite);
            builder.SetRenderFunc((BlitPassData passData, RasterGraphContext context) =>
            {
                ExecuteBlitPass(passData, context);
            });
        }
    }
}
