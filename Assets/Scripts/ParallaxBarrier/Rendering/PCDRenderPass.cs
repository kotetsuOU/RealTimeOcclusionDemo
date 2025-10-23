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

    private struct Point
    {
        public Vector3 position;
        public Vector3 color;
    }

    private class MeshTransformPair
    {
        public Mesh mesh;
        public Transform transform;
    }

    private ComputeShader pointCloudCompute;
    private float densityThreshold_e;
    private float neighborhoodParam_p_prime;
    private Material m_BlendMaterial;
    private bool _enableAlphaBlend;

    private ComputeBuffer _pointBuffer;
    private int _pointCount = 0;
    private Point[] _pointsCache;
    private bool _isDataDirty = false;

    private int _kernelClear, _kernelProject, _kernelCalcGridZMin, _kernelCalcDensity,
                _kernelCalcNeighborhoodSize, _kernelMedianFilter, _kernelOcclusion, _kernelInterpolate;

    private bool _isInitialized = false;

    private PCV_Data _dynamicData;
    private List<MeshTransformPair> _staticMeshes = new List<MeshTransformPair>();


    public PCDRenderPass(PCDRendererFeature settings, Material blendMaterial, bool enableAlphaBlend)
    {
        this.pointCloudCompute = settings.pointCloudCompute;
        this.densityThreshold_e = settings.densityThreshold_e;
        this.neighborhoodParam_p_prime = settings.neighborhoodParam_p_prime;
        this.m_BlendMaterial = blendMaterial;
        this._enableAlphaBlend = enableAlphaBlend;
    }

    public void SetPointCloudData(PCV_Data data)
    {
        if (_dynamicData != data || (data != null && _dynamicData != null && _dynamicData.PointCount != data.PointCount))
        {
            _dynamicData = data;
            _isDataDirty = true;
        }
        else if (data == null && _dynamicData != null)
        {
            _dynamicData = null;
            _isDataDirty = true;
        }
    }

    public void AddStaticMesh(Mesh mesh, Transform transform)
    {
        if (mesh != null && transform != null)
        {
            var existing = _staticMeshes.Find(p => p.mesh == mesh && p.transform == transform);
            if (existing == null)
            {
                _staticMeshes.Add(new MeshTransformPair { mesh = mesh, transform = transform });
                _isDataDirty = true;
                UnityEngine.Debug.Log($"[PCDRenderPass] Static mesh '{mesh.name}' added from Transform '{transform.name}'.");
            }
        }
    }

    public void RemoveStaticMesh(Mesh mesh, Transform transform)
    {
        var pair = _staticMeshes.Find(p => p.mesh == mesh && p.transform == transform);
        if (pair != null)
        {
            _staticMeshes.Remove(pair);
            _isDataDirty = true;
            UnityEngine.Debug.Log($"[PCDRenderPass] Static mesh '{mesh.name}' removed from Transform '{transform.name}'.");
        }
    }

    private void MergeAndCachePoints()
    {
        int dataPointCount = 0;
        if (_dynamicData != null && _dynamicData.PointCount > 0)
        {
            dataPointCount = _dynamicData.PointCount;
        }

        int totalMeshPointCount = 0;
        foreach (var pair in _staticMeshes)
        {
            if (pair.mesh == null || pair.transform == null) continue;

            if (!pair.mesh.isReadable)
            {
                UnityEngine.Debug.LogError($"[PCDRenderPass] Additional Mesh '{pair.mesh.name}' is not marked as Read/Write Enabled in Import Settings. Static mesh will be ignored.");
                continue;
            }
            totalMeshPointCount += pair.mesh.vertexCount;
        }

        _pointCount = dataPointCount + totalMeshPointCount;

        if (_pointCount == 0)
        {
            _pointsCache = null;
            return;
        }

        if (_pointsCache == null || _pointsCache.Length != _pointCount)
        {
            _pointsCache = new Point[_pointCount];
        }

        int cacheIndex = 0;

        if (dataPointCount > 0)
        {
            for (int i = 0; i < dataPointCount; i++)
            {
                _pointsCache[cacheIndex] = new Point
                {
                    position = _dynamicData.Vertices[i],
                    color = new Vector3(_dynamicData.Colors[i].r, _dynamicData.Colors[i].g, _dynamicData.Colors[i].b)
                };
                cacheIndex++;
            }
        }

        Vector3 defaultColor = new Vector3(1.0f, 1.0f, 1.0f);
        foreach (var pair in _staticMeshes)
        {
            if (pair.mesh == null || !pair.mesh.isReadable || pair.transform == null) continue;

            int meshPointCount = pair.mesh.vertexCount;
            if (meshPointCount == 0) continue;

            Vector3[] meshVertices = pair.mesh.vertices;
            Color[] meshColors = pair.mesh.colors;
            bool hasMeshColors = meshColors != null && meshColors.Length == meshPointCount;

            Matrix4x4 localToWorld = pair.transform.localToWorldMatrix;

            for (int i = 0; i < meshPointCount; i++)
            {
                Vector3 color = hasMeshColors ? new Vector3(meshColors[i].r, meshColors[i].g, meshColors[i].b) : defaultColor;

                Vector3 worldPos = localToWorld.MultiplyPoint3x4(meshVertices[i]);

                _pointsCache[cacheIndex] = new Point
                {
                    position = worldPos,
                    color = color
                };
                cacheIndex++;
            }
        }

        UnityEngine.Debug.Log($"[PCDRenderPass] Merged points - Dynamic: {dataPointCount}, Static Meshes: {totalMeshPointCount}, Total: {_pointCount}");
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
        _kernelCalcNeighborhoodSize = pointCloudCompute.FindKernel("CalculateNeighborhoodSize");
        _kernelMedianFilter = pointCloudCompute.FindKernel("MedianFilter");
        _kernelOcclusion = pointCloudCompute.FindKernel("OcclusionAndFilter");
        _kernelInterpolate = pointCloudCompute.FindKernel("Interpolate");

        _isInitialized = true;
    }

    private void UpdateComputeBuffer()
    {
        if (_pointCount == 0 || _pointsCache == null)
        {
            _pointBuffer?.Release();
            _pointBuffer = null;
            _isDataDirty = false;
            return;
        }

        if (_pointBuffer == null || !_pointBuffer.IsValid() || _pointBuffer.count != _pointCount)
        {
            _pointBuffer?.Release();
            _pointBuffer = new ComputeBuffer(_pointCount, sizeof(float) * 6);
        }

        _pointBuffer.SetData(_pointsCache);
        if (_pointCount > 0)
        {
            UnityEngine.Debug.Log($"[PCDRenderPass] ComputeBuffer updated with {_pointCount} points.");
        }
        _isDataDirty = false;
    }

    private class ComputePassData
    {
        internal int pointCount;
        internal Vector4 screenParams;
        internal Matrix4x4 viewMatrix;
        internal Matrix4x4 projectionMatrix;
        internal float densityThreshold_e;
        internal float neighborhoodParam_p_prime;
        internal int kernelClear, kernelProject, kernelCalcGridZMin, kernelCalcDensity,
                     kernelCalcNeighborhoodSize, kernelMedianFilter, kernelOcclusion, kernelInterpolate;
        internal ComputeBuffer pointBuffer;
        internal TextureHandle colorMap;
        internal TextureHandle depthMap;
        internal TextureHandle viewPositionMap;
        internal TextureHandle gridZMinMap;
        internal TextureHandle densityMap;
        internal TextureHandle neighborhoodSizeMap;
        internal TextureHandle filteredNeighborhoodSizeMap;
        internal TextureHandle occlusionResultMap;
        internal TextureHandle finalImage;
    }

    private class BlitPassData
    {
        internal Material blendMaterial;
        internal TextureHandle sourceImage;
        internal TextureHandle cameraTarget;
        internal bool enableAlphaBlend;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (!_isInitialized) Initialize();
        if (!_isInitialized)
        {
            UnityEngine.Debug.Log("PCDRenderPass is not initialized properly. Skipping rendering pass.");
            return;
        }

        if (!UnityEngine.Application.isPlaying)
        {
            return;
        }

        if (_isDataDirty)
        {
            MergeAndCachePoints();
            UpdateComputeBuffer();
        }

        if (_pointBuffer == null || _pointCount == 0)
        {
            return;
        }

        var cameraData = frameData.Get<UniversalCameraData>();
        var resourceData = frameData.Get<UniversalResourceData>();
        Camera camera = cameraData.camera;
        int screenWidth = camera.pixelWidth;
        int screenHeight = camera.pixelHeight;
        int gridWidth = Mathf.CeilToInt(screenWidth / 16.0f);
        int gridHeight = Mathf.CeilToInt(screenHeight / 16.0f);

        TextureHandle finalImageHandle;
        using (var builder = renderGraph.AddComputePass<ComputePassData>(PROFILER_TAG, out var data))
        {
            data.pointCount = _pointCount;
            data.screenParams = new Vector4(screenWidth, screenHeight, 0, 0);
            data.viewMatrix = camera.worldToCameraMatrix;
            data.projectionMatrix = camera.projectionMatrix;
            data.densityThreshold_e = densityThreshold_e;
            data.neighborhoodParam_p_prime = neighborhoodParam_p_prime;
            data.kernelClear = _kernelClear;
            data.kernelProject = _kernelProject;
            data.kernelCalcGridZMin = _kernelCalcGridZMin;
            data.kernelCalcDensity = _kernelCalcDensity;
            data.kernelCalcNeighborhoodSize = _kernelCalcNeighborhoodSize;
            data.kernelMedianFilter = _kernelMedianFilter;
            data.kernelOcclusion = _kernelOcclusion;
            data.kernelInterpolate = _kernelInterpolate;
            data.pointBuffer = _pointBuffer;

            var desc = new TextureDesc(screenWidth, screenHeight) { enableRandomWrite = true };

            // ColorMap (ARGBFloat)
            desc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGBFloat, false);
            data.colorMap = renderGraph.CreateTexture(desc);

            // DepthMap (RInt)
            desc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RInt, false);
            data.depthMap = renderGraph.CreateTexture(desc);

            // ViewPositionMap (ARGBFloat for XYZW)
            desc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGBFloat, false);
            data.viewPositionMap = renderGraph.CreateTexture(desc);

            // GridZMinMap (RInt)
            var gridDesc = new TextureDesc(gridWidth, gridHeight) { enableRandomWrite = true };
            gridDesc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RInt, false);
            data.gridZMinMap = renderGraph.CreateTexture(gridDesc);

            // DensityMap (RFloat)
            gridDesc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RFloat, false);
            data.densityMap = renderGraph.CreateTexture(gridDesc);

            // NeighborhoodSizeMaps (RInt)
            desc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.RInt, false);
            data.neighborhoodSizeMap = renderGraph.CreateTexture(desc);
            data.filteredNeighborhoodSizeMap = renderGraph.CreateTexture(desc);

            // Result Maps (ARGBFloat)
            desc.colorFormat = GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGBFloat, false);
            data.occlusionResultMap = renderGraph.CreateTexture(desc);
            data.finalImage = renderGraph.CreateTexture(desc);

            builder.UseTexture(data.colorMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.depthMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.viewPositionMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.gridZMinMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.densityMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.neighborhoodSizeMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.filteredNeighborhoodSizeMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.occlusionResultMap, AccessFlags.ReadWrite);
            builder.UseTexture(data.finalImage, AccessFlags.ReadWrite);
            finalImageHandle = data.finalImage;

            builder.SetRenderFunc((ComputePassData passData, ComputeGraphContext context) =>
            {
                var cmd = context.cmd;
                var cs = pointCloudCompute;

                cmd.SetComputeIntParam(cs, "_PointCount", passData.pointCount);
                cmd.SetComputeVectorParam(cs, "_ScreenParams", passData.screenParams);
                cmd.SetComputeMatrixParam(cs, "_ViewMatrix", passData.viewMatrix);
                cmd.SetComputeMatrixParam(cs, "_ProjectionMatrix", passData.projectionMatrix);
                cmd.SetComputeFloatParam(cs, "_DensityThreshold_e", passData.densityThreshold_e);
                cmd.SetComputeFloatParam(cs, "_NeighborhoodParam_p_prime", passData.neighborhoodParam_p_prime);

                int sw = (int)passData.screenParams.x;
                int sh = (int)passData.screenParams.y;
                int threadGroupsX = Mathf.CeilToInt(sw / 8.0f);
                int threadGroupsY = Mathf.CeilToInt(sh / 8.0f);
                int gridGroupsX = Mathf.CeilToInt(sw / 16.0f);
                int gridGroupsY = Mathf.CeilToInt(sh / 16.0f);

                // --- 1. ClearMaps ---
                cmd.SetComputeTextureParam(cs, passData.kernelClear, "_DepthMap_RW", passData.depthMap);
                cmd.SetComputeTextureParam(cs, passData.kernelClear, "_ViewPositionMap_RW", passData.viewPositionMap); // ÅyïœçXì_Åz í«â¡
                cmd.SetComputeTextureParam(cs, passData.kernelClear, "_OcclusionResultMap_RW", passData.occlusionResultMap);
                cmd.SetComputeTextureParam(cs, passData.kernelClear, "_FinalImage_RW", passData.finalImage);
                cmd.DispatchCompute(cs, passData.kernelClear, threadGroupsX, threadGroupsY, 1);

                // --- 2. ProjectPoints ---
                cmd.SetComputeBufferParam(cs, passData.kernelProject, "_PointBuffer", passData.pointBuffer);
                cmd.SetComputeTextureParam(cs, passData.kernelProject, "_ColorMap_RW", passData.colorMap);
                cmd.SetComputeTextureParam(cs, passData.kernelProject, "_DepthMap_RW", passData.depthMap);
                cmd.SetComputeTextureParam(cs, passData.kernelProject, "_ViewPositionMap_RW", passData.viewPositionMap); // ÅyïœçXì_Åz í«â¡
                cmd.DispatchCompute(cs, passData.kernelProject, Mathf.CeilToInt(passData.pointCount / 256.0f), 1, 1);

                // --- 3. CalculateGridZMin ---
                cmd.SetComputeTextureParam(cs, passData.kernelCalcGridZMin, "_DepthMap", passData.depthMap);
                cmd.SetComputeTextureParam(cs, passData.kernelCalcGridZMin, "_GridZMinMap_RW", passData.gridZMinMap);
                cmd.DispatchCompute(cs, passData.kernelCalcGridZMin, gridGroupsX, gridGroupsY, 1);

                // --- 4. CalculateDensity ---
                cmd.SetComputeTextureParam(cs, passData.kernelCalcDensity, "_DepthMap", passData.depthMap);
                cmd.SetComputeTextureParam(cs, passData.kernelCalcDensity, "_GridZMinMap", passData.gridZMinMap);
                cmd.SetComputeTextureParam(cs, passData.kernelCalcDensity, "_DensityMap_RW", passData.densityMap);
                cmd.DispatchCompute(cs, passData.kernelCalcDensity, gridGroupsX, gridGroupsY, 1);

                // --- 5. CalculateNeighborhoodSize ---
                cmd.SetComputeTextureParam(cs, passData.kernelCalcNeighborhoodSize, "_DensityMap", passData.densityMap);
                cmd.SetComputeTextureParam(cs, passData.kernelCalcNeighborhoodSize, "_NeighborhoodSizeMap_RW", passData.neighborhoodSizeMap);
                cmd.DispatchCompute(cs, passData.kernelCalcNeighborhoodSize, threadGroupsX, threadGroupsY, 1);

                // --- 6. MedianFilter ---
                cmd.SetComputeTextureParam(cs, passData.kernelMedianFilter, "_NeighborhoodSizeMap", passData.neighborhoodSizeMap);
                cmd.SetComputeTextureParam(cs, passData.kernelMedianFilter, "_FilteredNeighborhoodSizeMap_RW", passData.filteredNeighborhoodSizeMap);
                cmd.DispatchCompute(cs, passData.kernelMedianFilter, threadGroupsX, threadGroupsY, 1);

                // --- 7. OcclusionAndFilter ---
                cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, "_ColorMap", passData.colorMap);
                cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, "_DepthMap", passData.depthMap);
                cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, "_ViewPositionMap", passData.viewPositionMap);
                cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, "_FilteredNeighborhoodSizeMap", passData.filteredNeighborhoodSizeMap);
                cmd.SetComputeTextureParam(cs, passData.kernelOcclusion, "_OcclusionResultMap_RW", passData.occlusionResultMap);
                cmd.DispatchCompute(cs, passData.kernelOcclusion, threadGroupsX, threadGroupsY, 1);

                // --- 8. Interpolate ---
                cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, "_OcclusionResultMap", passData.occlusionResultMap);
                cmd.SetComputeTextureParam(cs, passData.kernelInterpolate, "_FinalImage_RW", passData.finalImage);
                cmd.DispatchCompute(cs, passData.kernelInterpolate, threadGroupsX, threadGroupsY, 1);
            });
        }

        // --- 9. Blit Pass ---
        using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>("PCD Blit Pass", out var data))
        {
            data.blendMaterial = m_BlendMaterial;
            data.enableAlphaBlend = _enableAlphaBlend;
            data.sourceImage = finalImageHandle;
            data.cameraTarget = resourceData.activeColorTexture;

            builder.UseTexture(data.sourceImage, AccessFlags.Read);
            builder.SetRenderAttachment(data.cameraTarget, 0);

            builder.SetRenderFunc((BlitPassData passData, RasterGraphContext context) =>
            {
                if (passData.enableAlphaBlend && passData.blendMaterial != null)
                {
                    Blitter.BlitTexture(context.cmd, passData.sourceImage, new Vector4(1, 1, 0, 0), passData.blendMaterial, 0);
                }
                else
                {
                    Blitter.BlitTexture(context.cmd, passData.sourceImage, new Vector4(1, 1, 0, 0), 0.0f, false);
                }
            });
        }
    }

    public void Cleanup()
    {
        _pointBuffer?.Release();
        _pointBuffer = null;
        _isInitialized = false;
        _pointsCache = null;
        _dynamicData = null;
        _staticMeshes.Clear();
    }
}