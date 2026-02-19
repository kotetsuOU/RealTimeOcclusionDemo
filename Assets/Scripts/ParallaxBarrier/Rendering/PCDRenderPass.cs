using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

public class PCDRenderPass : ScriptableRenderPass
{
    private const string PROFILER_TAG = "PCDRendering";

    private ComputeShader pointCloudCompute;
    private float densityThreshold_e;
    private float neighborhoodParam_p_prime;
    private Material m_BlendMaterial;
    private bool _enableAlphaBlend;
    private bool _enableGradientCorrection;
    private float _gradientThreshold_g_th;
    private float _occlusionThreshold;
    private bool _enableOriginDebugMap;

    private int _kernelClear, _kernelProject, _kernelCalcGridZMin, _kernelCalcDensity,
                _kernelCalcGridLevel, _kernelGridMedianFilter,
                _kernelCalcNeighborhoodSize,
                _kernelBuildDepthPyramidL1, _kernelBuildDepthPyramidL2,
                _kernelBuildDepthPyramidL3, _kernelBuildDepthPyramidL4,
                _kernelApplyGradient,
                _kernelOcclusion, _kernelInterpolate,
                _kernelMerge, _kernelInitFromCamera;

    private RTHandle _originDebugMapHandle;
    private bool _isInitialized = false;
    private const int STRIDE = 28; // sizeof(float)*3 + sizeof(float)*3 + sizeof(uint)
    
    // --- Buffer Manager ---
    private PCDPointBufferManager _bufferManager;

    public PCDRenderPass(PCDRendererFeature settings, Material blendMaterial, bool enableAlphaBlend)
    {
        this.pointCloudCompute = settings.pointCloudCompute;
        this.densityThreshold_e = settings.densityThreshold_e;
        this.neighborhoodParam_p_prime = settings.neighborhoodParam_p_prime;

        this._enableGradientCorrection = settings.enableGradientCorrection;
        this._gradientThreshold_g_th = settings.gradientThreshold_g_th;
        this._occlusionThreshold = settings.occlusionThreshold;
        this._enableOriginDebugMap = settings.enableOriginDebugMap;

        this.m_BlendMaterial = blendMaterial;
        this._enableAlphaBlend = enableAlphaBlend;
        
        _bufferManager = new PCDPointBufferManager();
    }

    public void SetDebugFlag(bool enableDebugMap)
    {
        this._enableOriginDebugMap = enableDebugMap;
    }

    public void SetExternalBuffer(ComputeBuffer buffer, int count)
    {
        _bufferManager.SetExternalBuffer(buffer, count);
    }

    public void SetPointCloudData(PCV_Data data)
    {
        _bufferManager.SetPointCloudData(data);
    }

    public void AddStaticMesh(Mesh mesh, Transform transform, PCDProcessingMode mode)
    {
        _bufferManager.AddStaticMesh(mesh, transform, mode);
    }

    public void RemoveStaticMesh(Mesh mesh, Transform transform)
    {
        _bufferManager.RemoveStaticMesh(mesh, transform);
    }

    private void Initialize()
    {
        if (pointCloudCompute == null)
        {
            UnityEngine.Debug.LogError("Compute Shader is null. Initialization failed.");
            _isInitialized = false;
            return;
        }

        _kernelClear = pointCloudCompute.FindKernel("ClearMaps");
        _kernelProject = pointCloudCompute.FindKernel("ProjectPoints");
        _kernelCalcGridZMin = pointCloudCompute.FindKernel("CalculateGridZMin");
        _kernelCalcDensity = pointCloudCompute.FindKernel("CalculateDensity");
        _kernelCalcGridLevel = pointCloudCompute.FindKernel("CalculateGridLevel");
        _kernelGridMedianFilter = pointCloudCompute.FindKernel("GridMedianFilter");
        _kernelCalcNeighborhoodSize = pointCloudCompute.FindKernel("CalculateNeighborhoodSize");

        if (_enableGradientCorrection)
        {
            _kernelBuildDepthPyramidL1 = pointCloudCompute.FindKernel("BuildDepthPyramidL1");
            _kernelBuildDepthPyramidL2 = pointCloudCompute.FindKernel("BuildDepthPyramidL2");
            _kernelBuildDepthPyramidL3 = pointCloudCompute.FindKernel("BuildDepthPyramidL3");
            _kernelBuildDepthPyramidL4 = pointCloudCompute.FindKernel("BuildDepthPyramidL4");
            _kernelApplyGradient = pointCloudCompute.FindKernel("ApplyAdaptiveGradientCorrection");
        }

        _kernelOcclusion = pointCloudCompute.FindKernel("OcclusionAndFilter");
        _kernelInterpolate = pointCloudCompute.FindKernel("Interpolate");
        _kernelMerge = pointCloudCompute.FindKernel("MergeBuffer");
        _kernelInitFromCamera = pointCloudCompute.FindKernel("InitFromCamera");

        _isInitialized = true;
    }

    private class ComputePassData
    {
        internal int pointCount;
        internal Vector4 screenParams;
        internal Matrix4x4 viewMatrix;
        internal Matrix4x4 projectionMatrix;
        internal float densityThreshold_e;
        internal float neighborhoodParam_p_prime;

        internal bool enableGradientCorrection;
        internal float gradientThreshold_g_th;
        internal float occlusionThreshold;
        internal bool enableOriginDebugMap;

        internal int kernelClear, kernelProject, kernelCalcGridZMin, kernelCalcDensity,
                     kernelCalcGridLevel, kernelGridMedianFilter,
                     kernelCalcNeighborhoodSize,
                     kernelBuildDepthPyramidL1, kernelBuildDepthPyramidL2,
                     kernelBuildDepthPyramidL3, kernelBuildDepthPyramidL4,
                     kernelApplyGradient,
                     kernelOcclusion, kernelInterpolate,
                     kernelMerge, kernelInitFromCamera;

        // Buffers for Copy
        internal bool useExternal;
        internal ComputeBuffer externalBuffer;
        internal ComputeBuffer internalBuffer;
        internal int externalCount;
        internal int internalCount;
        internal ComputeBuffer combinedBuffer; // Target buffer
        internal ComputeBuffer pointBuffer;

        internal TextureHandle colorMap;
        internal TextureHandle depthMap;
        internal TextureHandle virtualDepthTexture;
        internal TextureHandle cameraColorTexture;
        internal bool hasVirtualDepth;
        internal bool depthMapOnlyMode;
        internal Matrix4x4 inverseProjectionMatrix;
        internal TextureHandle viewPositionMap;
        internal TextureHandle gridZMinMap;
        internal TextureHandle densityMap;
        internal TextureHandle gridLevelMap;
        internal TextureHandle filteredGridLevelMap;
        internal TextureHandle neighborhoodSizeMap;
        internal TextureHandle depthPyramidL1;
        internal TextureHandle depthPyramidL2;
        internal TextureHandle depthPyramidL3;
        internal TextureHandle depthPyramidL4;
        internal TextureHandle correctedNeighborhoodSizeMap;
        internal TextureHandle occlusionResultMap;
        internal TextureHandle finalImage;
        internal TextureHandle originTypeMap;
        internal TextureHandle originDebugMap;
    }

    private class BlitPassData
    {
        internal Material blendMaterial;
        internal TextureHandle sourceImage;
        internal TextureHandle cameraTarget;
        internal bool enableAlphaBlend;
        internal bool enableOriginDebugMap;
    }

    public Texture GetOriginDebugMap()
    {
        if (_enableOriginDebugMap && _originDebugMapHandle != null)
        {
            return _originDebugMapHandle;
        }
        return null;
    }

    public bool ShouldSkipRendering()
    {
        // Check for external buffer
        bool hasExternalData = _bufferManager.UseExternalBuffer && _bufferManager.ExternalPointBuffer != null && _bufferManager.ExternalPointBuffer.IsValid() && _bufferManager.ExternalPointCount > 0;
        
        // Check for internal buffer
        bool hasInternalData = _bufferManager.PointBuffer != null && _bufferManager.PointBuffer.IsValid() && _bufferManager.PointCount > 0;
        
        // Check if we have DepthMap mode meshes
        bool hasDepthMapMeshes = _bufferManager.HasDepthMapMeshes();
        
        // Check if we have PointCloud mode meshes
        bool hasPointCloudMeshes = _bufferManager.HasPointCloudMeshes();
        
        // If we have no point cloud data at all, and only DepthMap meshes, skip rendering
        bool noPointCloudData = !hasExternalData && !hasInternalData && !hasPointCloudMeshes;
        bool depthMapOnlyMode = hasDepthMapMeshes && noPointCloudData;
        
        return depthMapOnlyMode;
    }

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

        if (_bufferManager.UseExternalBuffer)
        {
            int totalCount = _bufferManager.ExternalPointCount + _bufferManager.PointCount;
            if (totalCount > 0)
            {
                _bufferManager.EnsureCombinedBuffer(totalCount);
                activeBuffer = _bufferManager.CombinedBuffer;
                activeCount = totalCount;
            }
        }
        else
        {
            activeBuffer = _bufferManager.PointBuffer;
            activeCount = _bufferManager.PointCount;
        }

        // Check if we have DepthMap mode meshes
        bool hasDepthMapMeshes = _bufferManager.HasDepthMapMeshes();
        bool hasPointCloudMeshes = _bufferManager.HasPointCloudMeshes();
        
        // DepthMapOnlyMode: No point cloud data at all, only DepthMap meshes
        // In this case, URP renders the meshes normally, we don't need PCD processing
        bool depthMapOnlyMode = hasDepthMapMeshes && !hasPointCloudMeshes && 
                                (activeBuffer == null || activeCount == 0 || !activeBuffer.IsValid());

        // DepthMapOnlyMode: Skip all PCD processing and let URP render normally
        if (depthMapOnlyMode)
        {
            // No point cloud data to process, just let URP render the DepthMap meshes
            return;
        }

        if (activeBuffer == null || activeCount == 0 || !activeBuffer.IsValid())
        {
            // No point cloud data and no DepthMap meshes, nothing to render
            UnityEngine.Debug.LogWarning("[PCDRenderPass] Early return: No point cloud data");
            return;
        }
        
        // At this point, we have point cloud data (RealSense or static mesh points)
        // If hasDepthMapMeshes is true, we'll use URP's depth texture for occlusion

        var cameraData = frameData.Get<UniversalCameraData>();
        var resourceData = frameData.Get<UniversalResourceData>();
        
        // Check if URP depth texture is available for virtual depth occlusion
        bool hasValidDepthTexture = resourceData.cameraDepthTexture.IsValid();
        
        Camera camera = cameraData.camera;
        int screenWidth = camera.pixelWidth;
        int screenHeight = camera.pixelHeight;
        int gridWidth = Mathf.CeilToInt(screenWidth / 16.0f);
        int gridHeight = Mathf.CeilToInt(screenHeight / 16.0f);

        int l1_Width = 1, l1_Height = 1, l2_Width = 1, l2_Height = 1,
            l3_Width = 1, l3_Height = 1, l4_Width = 1, l4_Height = 1;

        if (_enableGradientCorrection)
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

        if (_enableOriginDebugMap)
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
            data.pointCount = activeCount;
            data.screenParams = new Vector4(screenWidth, screenHeight, 0, 0);
            data.viewMatrix = camera.worldToCameraMatrix;
            data.projectionMatrix = camera.projectionMatrix;
            data.densityThreshold_e = densityThreshold_e;
            data.neighborhoodParam_p_prime = neighborhoodParam_p_prime;

            data.enableGradientCorrection = _enableGradientCorrection;
            data.gradientThreshold_g_th = _gradientThreshold_g_th;
            data.occlusionThreshold = _occlusionThreshold;
            data.enableOriginDebugMap = _enableOriginDebugMap;

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

            // Enable VirtualDepth when there are DepthMap mode meshes (use URP depth for occlusion)
            data.hasVirtualDepth = hasDepthMapMeshes && resourceData.cameraDepthTexture.IsValid();
            data.depthMapOnlyMode = depthMapOnlyMode;
            data.inverseProjectionMatrix = camera.projectionMatrix.inverse;
            
            if (data.hasVirtualDepth || depthMapOnlyMode)
            {
                data.virtualDepthTexture = resourceData.cameraDepthTexture;
                builder.UseTexture(data.virtualDepthTexture, AccessFlags.Read);
            }
            
            // For DepthMapOnly mode, we also need camera color texture
            if (depthMapOnlyMode && resourceData.activeColorTexture.IsValid())
            {
                data.cameraColorTexture = resourceData.activeColorTexture;
                builder.UseTexture(data.cameraColorTexture, AccessFlags.Read);
            }

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

            if (data.enableGradientCorrection)
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

            if (data.enableOriginDebugMap)
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
            if (data.enableGradientCorrection)
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
                var cmd = context.cmd;
                var cs = pointCloudCompute;

                // Enable VirtualDepth for hybrid mode (PointCloud + DepthMap meshes coexisting)
                // In DepthMapOnlyMode, we don't need VirtualDepth check since all depth comes from camera
                bool useVirtualDepth = passData.hasVirtualDepth && !passData.depthMapOnlyMode;
                cmd.SetComputeIntParam(cs, "_UseVirtualDepth", useVirtualDepth ? 1 : 0);
                
                // Always bind a texture to avoid "Property not set" errors, even if unused
                if (passData.hasVirtualDepth || passData.depthMapOnlyMode)
                {
                    cmd.SetComputeTextureParam(cs, passData.kernelCalcGridZMin, "_VirtualDepthMap", passData.virtualDepthTexture);
                    cmd.SetComputeTextureParam(cs, passData.kernelCalcDensity, "_VirtualDepthMap", passData.virtualDepthTexture);
                    cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, "_VirtualDepthMap", passData.virtualDepthTexture);
                }
                else
                {
                    // Bind a dummy texture (e.g., depthMap itself) to satisfy the shader binding requirement
                    // This is safe because _UseVirtualDepth is 0, so the shader won't sample it
                    cmd.SetComputeTextureParam(cs, passData.kernelCalcGridZMin, "_VirtualDepthMap", passData.depthMap);
                    cmd.SetComputeTextureParam(cs, passData.kernelCalcDensity, "_VirtualDepthMap", passData.depthMap);
                    cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, "_VirtualDepthMap", passData.depthMap);
                }

                // --- 1. Buffer Merge using Compute Shader ---
                // GPU Copy (External + Internal -> Combined)
                if (passData.useExternal && passData.combinedBuffer != null)
                {
                    // Copy External (RealSense)
                    if (passData.externalBuffer != null && passData.externalCount > 0)
                    {
                        cmd.SetComputeBufferParam(cs, passData.kernelMerge, "_MergeSrcBuffer", passData.externalBuffer);
                        cmd.SetComputeBufferParam(cs, passData.kernelMerge, "_MergeDstBuffer", passData.combinedBuffer);
                        cmd.SetComputeIntParam(cs, "_MergeSrcOffset", 0);
                        cmd.SetComputeIntParam(cs, "_MergeDstOffset", 0);
                        cmd.SetComputeIntParam(cs, "_MergeCopyCount", passData.externalCount);
                        int groups = Mathf.CeilToInt(passData.externalCount / 256.0f);
                        cmd.DispatchCompute(cs, passData.kernelMerge, groups, 1, 1);
                    }

                    // Copy Internal (Static Mesh)
                    if (passData.internalBuffer != null && passData.internalCount > 0)
                    {
                        cmd.SetComputeBufferParam(cs, passData.kernelMerge, "_MergeSrcBuffer", passData.internalBuffer);
                        cmd.SetComputeBufferParam(cs, passData.kernelMerge, "_MergeDstBuffer", passData.combinedBuffer);
                        cmd.SetComputeIntParam(cs, "_MergeSrcOffset", 0);
                        cmd.SetComputeIntParam(cs, "_MergeDstOffset", passData.externalCount);
                        cmd.SetComputeIntParam(cs, "_MergeCopyCount", passData.internalCount);
                        int groups = Mathf.CeilToInt(passData.internalCount / 256.0f);
                        cmd.DispatchCompute(cs, passData.kernelMerge, groups, 1, 1);
                    }
                }
                // ------------------------------------------------

                cmd.SetComputeIntParam(cs, "_PointCount", passData.pointCount);
                cmd.SetComputeVectorParam(cs, "_ScreenParams", passData.screenParams);
                cmd.SetComputeMatrixParam(cs, "_ViewMatrix", passData.viewMatrix);
                cmd.SetComputeMatrixParam(cs, "_ProjectionMatrix", passData.projectionMatrix);
                cmd.SetComputeFloatParam(cs, "_DensityThreshold_e", passData.densityThreshold_e);
                cmd.SetComputeFloatParam(cs, "_NeighborhoodParam_p_prime", passData.neighborhoodParam_p_prime);
                cmd.SetComputeFloatParam(cs, "_GradientThreshold_g_th", passData.gradientThreshold_g_th);
                cmd.SetComputeFloatParam(cs, "_OcclusionThreshold", passData.occlusionThreshold);

                int sw = (int)passData.screenParams.x;
                int sh = (int)passData.screenParams.y;
                int threadGroupsX = Mathf.CeilToInt(sw / 8.0f);
                int threadGroupsY = Mathf.CeilToInt(sh / 8.0f);
                int gridGroupsX = Mathf.CeilToInt(sw / 16.0f);
                int gridGroupsY = Mathf.CeilToInt(sh / 16.0f);

                // 1. ClearMaps
                cmd.SetComputeTextureParam(cs, passData.kernelClear, "_ColorMap_RW", passData.colorMap);
                cmd.SetComputeTextureParam(cs, passData.kernelClear, "_DepthMap_RW", passData.depthMap);
                cmd.SetComputeTextureParam(cs, passData.kernelClear, "_ViewPositionMap_RW", passData.viewPositionMap);
                cmd.SetComputeTextureParam(cs, passData.kernelClear, "_OcclusionResultMap_RW", passData.occlusionResultMap);
                cmd.SetComputeTextureParam(cs, passData.kernelClear, "_FinalImage_RW", passData.finalImage);
                cmd.SetComputeTextureParam(cs, passData.kernelClear, "_OriginMap_RW", passData.originDebugMap);
                cmd.SetComputeTextureParam(cs, passData.kernelClear, "_OriginTypeMap_RW", passData.originTypeMap);
                cmd.DispatchCompute(cs, passData.kernelClear, threadGroupsX, threadGroupsY, 1);

                // 2. ProjectPoints or InitFromCamera (DepthMapOnly mode)
                if (passData.depthMapOnlyMode)
                {
                    // DepthMapOnly mode: Initialize from URP camera textures instead of projecting points
                    cmd.SetComputeMatrixParam(cs, "_InverseProjectionMatrix", passData.inverseProjectionMatrix);
                    cmd.SetComputeTextureParam(cs, passData.kernelInitFromCamera, "_VirtualDepthMap", passData.virtualDepthTexture);
                    cmd.SetComputeTextureParam(cs, passData.kernelInitFromCamera, "_CameraColorTexture", passData.cameraColorTexture);
                    cmd.SetComputeTextureParam(cs, passData.kernelInitFromCamera, "_DepthMap_RW", passData.depthMap);
                    cmd.SetComputeTextureParam(cs, passData.kernelInitFromCamera, "_ColorMap_RW", passData.colorMap);
                    cmd.SetComputeTextureParam(cs, passData.kernelInitFromCamera, "_ViewPositionMap_RW", passData.viewPositionMap);
                    cmd.SetComputeTextureParam(cs, passData.kernelInitFromCamera, "_OriginTypeMap_RW", passData.originTypeMap);
                    cmd.DispatchCompute(cs, passData.kernelInitFromCamera, threadGroupsX, threadGroupsY, 1);
                }
                else
                {
                    // Normal PointCloud mode: Project points to screen
                    cmd.SetComputeBufferParam(cs, passData.kernelProject, "_PointBuffer", passData.pointBuffer);
                    cmd.SetComputeTextureParam(cs, passData.kernelProject, "_ColorMap_RW", passData.colorMap);
                    cmd.SetComputeTextureParam(cs, passData.kernelProject, "_DepthMap_RW", passData.depthMap);
                    cmd.SetComputeTextureParam(cs, passData.kernelProject, "_ViewPositionMap_RW", passData.viewPositionMap);
                    cmd.SetComputeTextureParam(cs, passData.kernelProject, "_OriginTypeMap_RW", passData.originTypeMap);
                    cmd.DispatchCompute(cs, passData.kernelProject, Mathf.CeilToInt(passData.pointCount / 256.0f), 1, 1);
                }

                // 3. CalculateGridZMin
                cmd.SetComputeTextureParam(cs, passData.kernelCalcGridZMin, "_DepthMap", passData.depthMap);
                cmd.SetComputeTextureParam(cs, passData.kernelCalcGridZMin, "_GridZMinMap_RW", passData.gridZMinMap);
                cmd.DispatchCompute(cs, passData.kernelCalcGridZMin, gridGroupsX, gridGroupsY, 1);

                // 4. CalculateDensity
                cmd.SetComputeTextureParam(cs, passData.kernelCalcDensity, "_DepthMap", passData.depthMap);
                cmd.SetComputeTextureParam(cs, passData.kernelCalcDensity, "_GridZMinMap", passData.gridZMinMap);
                cmd.SetComputeTextureParam(cs, passData.kernelCalcDensity, "_DensityMap_RW", passData.densityMap);
                cmd.DispatchCompute(cs, passData.kernelCalcDensity, gridGroupsX, gridGroupsY, 1);

                // 5. CalculateGridLevel
                int gridKernelGroupsX = Mathf.CeilToInt(gridGroupsX / 16.0f);
                int gridKernelGroupsY = Mathf.CeilToInt(gridGroupsY / 16.0f);
                cmd.SetComputeTextureParam(cs, passData.kernelCalcGridLevel, "_DensityMap", passData.densityMap);
                cmd.SetComputeTextureParam(cs, passData.kernelCalcGridLevel, "_GridLevelMap_RW", passData.gridLevelMap);
                cmd.DispatchCompute(cs, passData.kernelCalcGridLevel, gridKernelGroupsX, gridKernelGroupsY, 1);

                // 6. GridMedianFilter
                cmd.SetComputeTextureParam(cs, passData.kernelGridMedianFilter, "_GridLevelMap", passData.gridLevelMap);
                cmd.SetComputeTextureParam(cs, passData.kernelGridMedianFilter, "_FilteredGridLevelMap_RW", passData.filteredGridLevelMap);
                cmd.DispatchCompute(cs, passData.kernelGridMedianFilter, gridKernelGroupsX, gridKernelGroupsY, 1);

                // 7. CalculateNeighborhoodSize
                cmd.SetComputeTextureParam(cs, passData.kernelCalcNeighborhoodSize, "_FilteredGridLevelMap", passData.filteredGridLevelMap);
                cmd.SetComputeTextureParam(cs, passData.kernelCalcNeighborhoodSize, "_NeighborhoodSizeMap_RW", passData.neighborhoodSizeMap);
                cmd.DispatchCompute(cs, passData.kernelCalcNeighborhoodSize, threadGroupsX, threadGroupsY, 1);

                // 8. Gradient Correction Path
                if (passData.enableGradientCorrection)
                {
                    // 8a. Build Z-Min Depth Pyramid
                    int l1_tgX = Mathf.CeilToInt(l1_Width / 8.0f);
                    int l1_tgY = Mathf.CeilToInt(l1_Height / 8.0f);
                    cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL1, "_DepthMap", passData.depthMap);
                    cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL1, "_DepthPyramidL1_RW", passData.depthPyramidL1);
                    cmd.DispatchCompute(cs, passData.kernelBuildDepthPyramidL1, l1_tgX, l1_tgY, 1);

                    int l2_tgX = Mathf.CeilToInt(l2_Width / 8.0f);
                    int l2_tgY = Mathf.CeilToInt(l2_Height / 8.0f);
                    cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL2, "_DepthPyramidL1", passData.depthPyramidL1);
                    cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL2, "_DepthPyramidL2_RW", passData.depthPyramidL2);
                    cmd.DispatchCompute(cs, passData.kernelBuildDepthPyramidL2, l2_tgX, l2_tgY, 1);

                    int l3_tgX = Mathf.CeilToInt(l3_Width / 8.0f);
                    int l3_tgY = Mathf.CeilToInt(l3_Height / 8.0f);
                    cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL3, "_DepthPyramidL2", passData.depthPyramidL2);
                    cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL3, "_DepthPyramidL3_RW", passData.depthPyramidL3);
                    cmd.DispatchCompute(cs, passData.kernelBuildDepthPyramidL3, l3_tgX, l3_tgY, 1);

                    int l4_tgX = Mathf.CeilToInt(l4_Width / 8.0f);
                    int l4_tgY = Mathf.CeilToInt(l4_Height / 8.0f);
                    cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL4, "_DepthPyramidL3", passData.depthPyramidL3);
                    cmd.SetComputeTextureParam(cs, passData.kernelBuildDepthPyramidL4, "_DepthPyramidL4_RW", passData.depthPyramidL4);
                    cmd.DispatchCompute(cs, passData.kernelBuildDepthPyramidL4, l4_tgX, l4_tgY, 1);

                    // 8b+8c. ApplyAdaptiveGradientCorrection
                    cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, "_NeighborhoodSizeMap", passData.neighborhoodSizeMap);
                    cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, "_DepthPyramidL1", passData.depthPyramidL1);
                    cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, "_DepthPyramidL2", passData.depthPyramidL2);
                    cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, "_DepthPyramidL3", passData.depthPyramidL3);
                    cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, "_DepthPyramidL4", passData.depthPyramidL4);
                    cmd.SetComputeTextureParam(cs, passData.kernelApplyGradient, "_CorrectedNeighborhoodSizeMap_RW", passData.correctedNeighborhoodSizeMap);
                    cmd.DispatchCompute(cs, passData.kernelApplyGradient, threadGroupsX, threadGroupsY, 1);
                }

                // 9. OcclusionAndFilter
                cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, "_ColorMap", passData.colorMap);
                cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, "_DepthMap", passData.depthMap);
                cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, "_ViewPositionMap", passData.viewPositionMap);
                cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, "_OriginTypeMap", passData.originTypeMap);
                cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, "_OriginTypeMap_RW", passData.originTypeMap);

                if (passData.enableGradientCorrection)
                {
                    cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, "_FinalNeighborhoodSizeMap", passData.correctedNeighborhoodSizeMap);
                }
                else
                {
                    cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, "_FinalNeighborhoodSizeMap", passData.neighborhoodSizeMap);
                }

                cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, "_OcclusionResultMap_RW", passData.occlusionResultMap);
                cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, "_OriginMap_RW", passData.originDebugMap);
                cmd.DispatchCompute(cs, passData.kernelOcclusion, threadGroupsX, threadGroupsY, 1);

                // 10. Interpolate
                cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, "_OcclusionResultMap", passData.occlusionResultMap);
                cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, "_OriginTypeMap", passData.originTypeMap);
                cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, "_FinalImage_RW", passData.finalImage);
                cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, "_OriginMap_RW", passData.originDebugMap);
                cmd.DispatchCompute(cs, passData.kernelInterpolate, threadGroupsX, threadGroupsY, 1);
            });
        }

        // --- Blit Pass ---
        using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>("PCD Blit Pass", out var data))
        {
            data.blendMaterial = m_BlendMaterial;
            data.enableAlphaBlend = _enableAlphaBlend;
            data.cameraTarget = resourceData.activeColorTexture;

            data.enableOriginDebugMap = _enableOriginDebugMap;

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

            // Use Load action to preserve existing camera color (URP rendered meshes)
            builder.SetRenderAttachment(data.cameraTarget, 0, AccessFlags.ReadWrite);

            builder.SetRenderFunc((BlitPassData passData, RasterGraphContext context) =>
            {
                // Always use alpha blend when we have DepthMap meshes or when enableAlphaBlend is true
                // This allows URP-rendered content (virtual objects) to show through where PCD result is transparent
                if (passData.blendMaterial != null && !passData.enableOriginDebugMap)
                {
                    Blitter.BlitTexture(context.cmd, passData.sourceImage, new Vector4(1, 1, 0, 0), passData.blendMaterial, 0);
                    return;
                }

                Blitter.BlitTexture(context.cmd, passData.sourceImage, new Vector4(1, 1, 0, 0), 0.0f, false);
            });
        }
    }

    public void Cleanup()
    {
        _bufferManager.Cleanup();

        _originDebugMapHandle?.Release();
        _originDebugMapHandle = null;

        _isInitialized = false;
    }
}