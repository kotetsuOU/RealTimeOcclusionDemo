using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Experimental.Rendering;

public partial class PCDRenderPass : ScriptableRenderPass
{
    private const string PROFILER_TAG = "PCDRendering";

    // Cache shader property IDs to avoid string lookups per frame
    private static class ShaderIDs
    {
        public static readonly int PointCount = Shader.PropertyToID("_PointCount");
        public static readonly int ScreenParams = Shader.PropertyToID("_ScreenParams");
        public static readonly int ViewMatrix = Shader.PropertyToID("_ViewMatrix");
        public static readonly int ProjectionMatrix = Shader.PropertyToID("_ProjectionMatrix");
        public static readonly int InverseProjectionMatrix = Shader.PropertyToID("_InverseProjectionMatrix");
        public static readonly int DensityThreshold_e = Shader.PropertyToID("_DensityThreshold_e");
        public static readonly int NeighborhoodParam_p_prime = Shader.PropertyToID("_NeighborhoodParam_p_prime");
        public static readonly int GradientThreshold_g_th = Shader.PropertyToID("_GradientThreshold_g_th");
        public static readonly int OcclusionThreshold = Shader.PropertyToID("_OcclusionThreshold");
        public static readonly int OcclusionFadeWidth = Shader.PropertyToID("_OcclusionFadeWidth");

        public static readonly int ColorMap = Shader.PropertyToID("_ColorMap");
        public static readonly int DepthMap = Shader.PropertyToID("_DepthMap");
        public static readonly int ColorMap_RW = Shader.PropertyToID("_ColorMap_RW");
        public static readonly int DepthMap_RW = Shader.PropertyToID("_DepthMap_RW");
        public static readonly int ViewPositionMap = Shader.PropertyToID("_ViewPositionMap");
        public static readonly int ViewPositionMap_RW = Shader.PropertyToID("_ViewPositionMap_RW");
        public static readonly int GridZMinMap = Shader.PropertyToID("_GridZMinMap");
        public static readonly int GridZMinMap_RW = Shader.PropertyToID("_GridZMinMap_RW");
        public static readonly int DensityMap = Shader.PropertyToID("_DensityMap");
        public static readonly int DensityMap_RW = Shader.PropertyToID("_DensityMap_RW");
        public static readonly int GridLevelMap = Shader.PropertyToID("_GridLevelMap");
        public static readonly int GridLevelMap_RW = Shader.PropertyToID("_GridLevelMap_RW");
        public static readonly int FilteredGridLevelMap = Shader.PropertyToID("_FilteredGridLevelMap");
        public static readonly int FilteredGridLevelMap_RW = Shader.PropertyToID("_FilteredGridLevelMap_RW");
        public static readonly int NeighborhoodSizeMap = Shader.PropertyToID("_NeighborhoodSizeMap");
        public static readonly int NeighborhoodSizeMap_RW = Shader.PropertyToID("_NeighborhoodSizeMap_RW");
        
        public static readonly int DepthPyramidL1 = Shader.PropertyToID("_DepthPyramidL1");
        public static readonly int DepthPyramidL1_RW = Shader.PropertyToID("_DepthPyramidL1_RW");
        public static readonly int DepthPyramidL2 = Shader.PropertyToID("_DepthPyramidL2");
        public static readonly int DepthPyramidL2_RW = Shader.PropertyToID("_DepthPyramidL2_RW");
        public static readonly int DepthPyramidL3 = Shader.PropertyToID("_DepthPyramidL3");
        public static readonly int DepthPyramidL3_RW = Shader.PropertyToID("_DepthPyramidL3_RW");
        public static readonly int DepthPyramidL4 = Shader.PropertyToID("_DepthPyramidL4");
        public static readonly int DepthPyramidL4_RW = Shader.PropertyToID("_DepthPyramidL4_RW");
        public static readonly int CorrectedNeighborhoodSizeMap_RW = Shader.PropertyToID("_CorrectedNeighborhoodSizeMap_RW");
        public static readonly int FinalNeighborhoodSizeMap = Shader.PropertyToID("_FinalNeighborhoodSizeMap");
        
        public static readonly int OcclusionResultMap = Shader.PropertyToID("_OcclusionResultMap");
        public static readonly int OcclusionResultMap_RW = Shader.PropertyToID("_OcclusionResultMap_RW");
        public static readonly int FinalImage_RW = Shader.PropertyToID("_FinalImage_RW");
        
        public static readonly int OriginTypeMap = Shader.PropertyToID("_OriginTypeMap");
        public static readonly int OriginTypeMap_RW = Shader.PropertyToID("_OriginTypeMap_RW");
        public static readonly int OriginMap_RW = Shader.PropertyToID("_OriginMap_RW");

        public static readonly int OcclusionValueMap_RW = Shader.PropertyToID("_OcclusionValueMap_RW");
        public static readonly int RecordOcclusionDebug = Shader.PropertyToID("_RecordOcclusionDebug");

        public static readonly int MergeSrcBuffer = Shader.PropertyToID("_MergeSrcBuffer");
        public static readonly int MergeDstBuffer = Shader.PropertyToID("_MergeDstBuffer");
        public static readonly int MergeSrcOffset = Shader.PropertyToID("_MergeSrcOffset");
        public static readonly int MergeDstOffset = Shader.PropertyToID("_MergeDstOffset");
        public static readonly int MergeCopyCount = Shader.PropertyToID("_MergeCopyCount");
        public static readonly int PointBuffer = Shader.PropertyToID("_PointBuffer");

        public static readonly int UseVirtualDepth = Shader.PropertyToID("_UseVirtualDepth");
        public static readonly int VirtualDepthMap = Shader.PropertyToID("_VirtualDepthMap");
        public static readonly int CameraColorTexture = Shader.PropertyToID("_CameraColorTexture");
    }

    private ComputeShader pointCloudCompute; // The core compute shader defining the occlusion pipeline
    private Material m_BlendMaterial;        // Material used for blending the resulting image onto the screen
    private bool _enableAlphaBlend;          // Whether the final point cloud result should be alpha blended
    private PCDRendererFeature.PCDRenderSettings _settings; // Current settings corresponding to the feature inspector values

    // Kernel IDs corresponding to individual compute shader functions
    private int _kernelClear, _kernelProject, _kernelCalcGridZMin, _kernelCalcDensity,
                _kernelCalcGridLevel, _kernelGridMedianFilter,
                _kernelCalcNeighborhoodSize,
                _kernelBuildDepthPyramidL1, _kernelBuildDepthPyramidL2,
                _kernelBuildDepthPyramidL3, _kernelBuildDepthPyramidL4,
                _kernelApplyGradient,
                _kernelOcclusion, _kernelInterpolate,
                _kernelMerge, _kernelInitFromCamera;

    // Output and debug maps
    private RTHandle _originDebugMapHandle;
    private RTHandle _occlusionValueMapHandle;
    private bool _isInitialized = false;
    private const int STRIDE = 28; // Represents size of one point data: sizeof(float)*3 + sizeof(float)*3 + sizeof(uint)

    // --- Buffer Manager ---
    private PCDPointBufferManager _bufferManager;

    public PCDRenderPass(ComputeShader computeShader, PCDRendererFeature.PCDRenderSettings settings, Material blendMaterial, bool enableAlphaBlend)
    {
        this.pointCloudCompute = computeShader;
        this._settings = settings;

        this.m_BlendMaterial = blendMaterial;
        this._enableAlphaBlend = enableAlphaBlend;

        _bufferManager = new PCDPointBufferManager(); // Initialize the data manager for static meshes and points
    }

    /// <summary> Updates renderer settings externally (e.g., via script or inspector changes). </summary>
    public void UpdateSettings(PCDRendererFeature.PCDRenderSettings settings)
    {
        this._settings = settings;
    }

    /// <summary> Toggles the rendering of the origin debug map. </summary>
    public void SetDebugFlag(bool enableDebugMap)
    {
        this._settings.enableOriginDebugMap = enableDebugMap;
    }

    /// <summary> Allows injecting an external compute buffer directly. </summary>
    public void SetExternalBuffer(ComputeBuffer buffer, int count)
    {
        _bufferManager.SetExternalBuffer(buffer, count);
    }

    /// <summary> Sets the point cloud data from an internal PCV_Data object. </summary>
    public void SetPointCloudData(PCV_Data data)
    {
        _bufferManager.SetPointCloudData(data);
    }

    /// <summary> Registers a static Unity Mesh to interact with point cloud occlusion. </summary>
    public void AddStaticMesh(Mesh mesh, Transform transform, PCDProcessingMode mode)
    {
        _bufferManager.AddStaticMesh(mesh, transform, mode);
    }

    /// <summary> Unregisters a tracked static Unity Mesh. </summary>
    public void RemoveStaticMesh(Mesh mesh, Transform transform)
    {
        _bufferManager.RemoveStaticMesh(mesh, transform);
    }

    /// <summary> Fetches kernel index IDs from the compute shader configuration. </summary>
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

        if (_settings.enableGradientCorrection)
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
        internal ComputeShader computeShader;
        internal int pointCount;
        internal Vector4 screenParams;
        internal Matrix4x4 viewMatrix;
        internal Matrix4x4 projectionMatrix;
        
        internal PCDRendererFeature.PCDRenderSettings settings;

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
        internal TextureHandle occlusionValueMap;
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

    /// <summary> Returns the origin debug map if it's generated, otherwise null. </summary>
    public Texture GetOriginDebugMap()
    {
        if (_settings.enableOriginDebugMap && _originDebugMapHandle != null)
        {
            return _originDebugMapHandle;
        }
        return null;
    }

    /// <summary> Determines if the occlusion pass pipeline should be bypassed this frame. </summary>
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

        // Skip rendering if there is no point cloud data and no meshes to inject (or if only generating background depths).
        bool noPointCloudData = !hasExternalData && !hasInternalData && !hasPointCloudMeshes;
        bool depthMapOnlyMode = hasDepthMapMeshes && noPointCloudData;

        return depthMapOnlyMode;
    }

    /// <summary> Releases resources and references properly to prevent memory leaks. </summary>
    public void Cleanup()
    {
        _bufferManager.Cleanup();

        _originDebugMapHandle?.Release();
        _originDebugMapHandle = null;
        
        _occlusionValueMapHandle?.Release();
        _occlusionValueMapHandle = null;

        _isInitialized = false;
    }
}