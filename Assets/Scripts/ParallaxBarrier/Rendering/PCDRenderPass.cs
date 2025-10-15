using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PCDRenderPass : ScriptableRenderPass
{
    private const string PROFILER_TAG = "PCDRendering";

    private struct Point
    {
        public Vector3 position;
        public Vector3 color;
    }

    private ComputeShader pointCloudCompute;
    private float densityThreshold_e;
    private float neighborhoodParam_p_prime;

    private ComputeBuffer _pointBuffer;
    private int _pointCount = 0;
    private Point[] _pointsCache;
    private bool _isDataDirty = false;

    private RenderTexture _colorMap;
    private RenderTexture _depthMap;
    private RenderTexture _gridZMinMap;
    private RenderTexture _densityMap;
    private RenderTexture _neighborhoodSizeMap;
    private RenderTexture _filteredNeighborhoodSizeMap;
    private RenderTexture _occlusionResultMap;
    private RenderTexture _finalImage;

    private int _kernelClear, _kernelProject, _kernelCalcGridZMin, _kernelCalcDensity,
                _kernelCalcNeighborhoodSize, _kernelMedianFilter, _kernelOcclusion, _kernelInterpolate;

    private bool _isInitialized = false;

    public PCDRenderPass(PCDRendererFeature settings)
    {
        this.pointCloudCompute = settings.pointCloudCompute;
        this.densityThreshold_e = settings.densityThreshold_e;
        this.neighborhoodParam_p_prime = settings.neighborhoodParam_p_prime;
    }

    public void SetPointCloudData(PCV_Data data)
    {
        if (data == null || data.PointCount == 0)
        {
            _pointCount = 0;
            _pointsCache = null;
        }
        else
        {
            _pointCount = data.PointCount;
            if (_pointsCache == null || _pointsCache.Length != _pointCount)
            {
                _pointsCache = new Point[_pointCount];
            }

            for (int i = 0; i < _pointCount; i++)
            {
                _pointsCache[i] = new Point
                {
                    position = data.Vertices[i],
                    color = new Vector3(data.Colors[i].r, data.Colors[i].g, data.Colors[i].b)
                };
            }
        }
        _isDataDirty = true;
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
        if (_pointCount == 0)
        {
            _pointBuffer?.Release();
            _pointBuffer = null;
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

    private void SetupTextures(int width, int height)
    {
        ReleaseTextures();
        int gridWidth = Mathf.CeilToInt(width / 16.0f);
        int gridHeight = Mathf.CeilToInt(height / 16.0f);

        _colorMap = CreateRT(width, height, RenderTextureFormat.ARGBFloat);
        _depthMap = CreateRT(width, height, RenderTextureFormat.RInt);
        _gridZMinMap = CreateRT(gridWidth, gridHeight, RenderTextureFormat.RInt);
        _densityMap = CreateRT(gridWidth, gridHeight, RenderTextureFormat.RFloat);
        _neighborhoodSizeMap = CreateRT(width, height, RenderTextureFormat.RInt);
        _filteredNeighborhoodSizeMap = CreateRT(width, height, RenderTextureFormat.RInt);
        _occlusionResultMap = CreateRT(width, height, RenderTextureFormat.ARGBFloat);
        _finalImage = CreateRT(width, height, RenderTextureFormat.ARGBFloat);
    }

    private RenderTexture CreateRT(int width, int height, RenderTextureFormat format)
    {
        var rt = new RenderTexture(width, height, 0, format);
        rt.enableRandomWrite = true;
        rt.Create();
        return rt;
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
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
            UpdateComputeBuffer();
        }
        if (_pointBuffer == null || _pointCount == 0)
        {
            UnityEngine.Debug.Log("Point buffer is null or empty. Skipping rendering pass.");
            return;
        }

        Camera camera = renderingData.cameraData.camera;
        int screenWidth = camera.pixelWidth;
        int screenHeight = camera.pixelHeight;

        if (_colorMap == null || _colorMap.width != screenWidth || _colorMap.height != screenHeight)
        {
            SetupTextures(screenWidth, screenHeight);
        }

        CommandBuffer cmd = CommandBufferPool.Get(PROFILER_TAG);

        cmd.SetComputeIntParam(pointCloudCompute, "_PointCount", _pointCount);
        cmd.SetComputeVectorParam(pointCloudCompute, "_ScreenParams", new Vector4(screenWidth, screenHeight, 0, 0));
        cmd.SetComputeMatrixParam(pointCloudCompute, "_ViewMatrix", camera.worldToCameraMatrix);
        cmd.SetComputeMatrixParam(pointCloudCompute, "_ProjectionMatrix", camera.projectionMatrix);
        cmd.SetComputeFloatParam(pointCloudCompute, "_DensityThreshold_e", densityThreshold_e);
        cmd.SetComputeFloatParam(pointCloudCompute, "_NeighborhoodParam_p_prime", neighborhoodParam_p_prime);

        cmd.SetComputeBufferParam(pointCloudCompute, _kernelProject, "_PointBuffer", _pointBuffer);

        int threadGroupsX = Mathf.CeilToInt(screenWidth / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(screenHeight / 8.0f);
        int gridGroupsX = Mathf.CeilToInt(screenWidth / 16.0f);
        int gridGroupsY = Mathf.CeilToInt(screenHeight / 16.0f);

        cmd.SetComputeTextureParam(pointCloudCompute, _kernelClear, "_DepthMap_RW", _depthMap);
        cmd.DispatchCompute(pointCloudCompute, _kernelClear, threadGroupsX, threadGroupsY, 1);

        cmd.SetComputeTextureParam(pointCloudCompute, _kernelProject, "_ColorMap_RW", _colorMap);
        cmd.SetComputeTextureParam(pointCloudCompute, _kernelProject, "_DepthMap_RW", _depthMap);
        cmd.DispatchCompute(pointCloudCompute, _kernelProject, Mathf.CeilToInt(_pointCount / 256.0f), 1, 1);

        cmd.SetComputeTextureParam(pointCloudCompute, _kernelCalcGridZMin, "_DepthMap", _depthMap);
        cmd.SetComputeTextureParam(pointCloudCompute, _kernelCalcGridZMin, "_GridZMinMap_RW", _gridZMinMap);
        cmd.DispatchCompute(pointCloudCompute, _kernelCalcGridZMin, gridGroupsX, gridGroupsY, 1);

        cmd.SetComputeTextureParam(pointCloudCompute, _kernelCalcDensity, "_DepthMap", _depthMap);
        cmd.SetComputeTextureParam(pointCloudCompute, _kernelCalcDensity, "_GridZMinMap", _gridZMinMap);
        cmd.SetComputeTextureParam(pointCloudCompute, _kernelCalcDensity, "_DensityMap_RW", _densityMap);
        cmd.DispatchCompute(pointCloudCompute, _kernelCalcDensity, gridGroupsX, gridGroupsY, 1);

        cmd.SetComputeTextureParam(pointCloudCompute, _kernelCalcNeighborhoodSize, "_DensityMap", _densityMap);
        cmd.SetComputeTextureParam(pointCloudCompute, _kernelCalcNeighborhoodSize, "_NeighborhoodSizeMap_RW", _neighborhoodSizeMap);
        cmd.DispatchCompute(pointCloudCompute, _kernelCalcNeighborhoodSize, threadGroupsX, threadGroupsY, 1);

        cmd.SetComputeTextureParam(pointCloudCompute, _kernelMedianFilter, "_NeighborhoodSizeMap", _neighborhoodSizeMap);
        cmd.SetComputeTextureParam(pointCloudCompute, _kernelMedianFilter, "_FilteredNeighborhoodSizeMap_RW", _filteredNeighborhoodSizeMap);
        cmd.DispatchCompute(pointCloudCompute, _kernelMedianFilter, threadGroupsX, threadGroupsY, 1);

        cmd.SetComputeTextureParam(pointCloudCompute, _kernelOcclusion, "_ColorMap", _colorMap);
        cmd.SetComputeTextureParam(pointCloudCompute, _kernelOcclusion, "_DepthMap", _depthMap);
        cmd.SetComputeTextureParam(pointCloudCompute, _kernelOcclusion, "_FilteredNeighborhoodSizeMap", _filteredNeighborhoodSizeMap);
        cmd.SetComputeTextureParam(pointCloudCompute, _kernelOcclusion, "_OcclusionResultMap_RW", _occlusionResultMap);
        cmd.DispatchCompute(pointCloudCompute, _kernelOcclusion, threadGroupsX, threadGroupsY, 1);

        cmd.SetComputeTextureParam(pointCloudCompute, _kernelInterpolate, "_OcclusionResultMap", _occlusionResultMap);
        cmd.SetComputeTextureParam(pointCloudCompute, _kernelInterpolate, "_FinalImage_RW", _finalImage);
        cmd.DispatchCompute(pointCloudCompute, _kernelInterpolate, threadGroupsX, threadGroupsY, 1);

        cmd.Blit(_finalImage, renderingData.cameraData.renderer.cameraColorTargetHandle);

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public void Cleanup()
    {
        ReleaseTextures();
        _pointBuffer?.Release();
        _pointBuffer = null;
        _isInitialized = false;
        _pointsCache = null;
    }

    private void ReleaseTextures()
    {
        _colorMap?.Release();
        _depthMap?.Release();
        _gridZMinMap?.Release();
        _densityMap?.Release();
        _neighborhoodSizeMap?.Release();
        _filteredNeighborhoodSizeMap?.Release();
        _occlusionResultMap?.Release();
        _finalImage?.Release();
    }
}