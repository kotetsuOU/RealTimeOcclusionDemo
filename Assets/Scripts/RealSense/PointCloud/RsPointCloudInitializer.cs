using Intel.RealSense;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

public class RsPointCloudInitializer
{
    private readonly RsProcessingPipe _processingPipe;
    private readonly ComputeShader _filterShader;
    private readonly ComputeShader _transformShader;

    private RsDataProvider _dataProvider;
    private RsPointCloudCompute _compute;
    private RsPointCloudFrameProcessor _frameProcessor;
    private RsIntegratedPointCloud _integratedPointCloud;

    private Vector3[] _rawVertices;
    private ComputeBuffer _rawVerticesBuffer;

    private bool _useIntegratedPointCloud = false;
    private bool _isInitialized = false;

    public RsDataProvider DataProvider => _dataProvider;
    public RsPointCloudCompute Compute => _compute;
    public RsPointCloudFrameProcessor FrameProcessor => _frameProcessor;
    public RsIntegratedPointCloud IntegratedPointCloud => _integratedPointCloud;
    public Vector3[] RawVertices => _rawVertices;
    public ComputeBuffer RawVerticesBuffer => _rawVerticesBuffer;
    public bool UseIntegratedPointCloud => _useIntegratedPointCloud;
    public bool IsInitialized => _isInitialized;

    public RsPointCloudInitializer(
        RsProcessingPipe processingPipe,
        ComputeShader filterShader,
        ComputeShader transformShader)
    {
        _processingPipe = processingPipe;
        _filterShader = filterShader;
        _transformShader = transformShader;
    }

    public void InitializeSynthetic(
        RsPointCloudSyntheticData.SyntheticShape shape,
        int pointCount,
        float scale,
        float maxPlaneDistance,
        Matrix4x4 localToWorldMatrix,
        RsPerformanceLogger logger,
        Stopwatch stopwatch)
    {
        UnityEngine.Debug.Log("[RsPointCloudInitializer] Initializing Synthetic Data...");

        Vector3 scanRange = new Vector3(10f, 10f, 10f);
        _compute = new RsPointCloudCompute(_filterShader, _transformShader, scanRange, 640, maxPlaneDistance);
        _compute.InitializeBuffers(pointCount, localToWorldMatrix);

        _frameProcessor = new RsPointCloudFrameProcessor(_compute, logger, stopwatch);

        _rawVertices = new Vector3[pointCount];
        _rawVerticesBuffer = new ComputeBuffer(pointCount, sizeof(float) * 3);

        var syntheticGenerator = new RsPointCloudSyntheticData(shape, pointCount, scale);
        syntheticGenerator.GenerateInto(_rawVertices);
        _rawVerticesBuffer.SetData(_rawVertices);

        _isInitialized = true;
        UnityEngine.Debug.Log($"[RsPointCloudInitializer] Synthetic Data Initialized with {pointCount} points.");
    }

    public void InitializeOnStreaming(
        PipelineProfile profile,
        RsDeviceController deviceController,
        float maxPlaneDistance,
        RsPerformanceLogger logger,
        Stopwatch stopwatch)
    {
        int width = 0;
        int height = 0;

        TryConnectIntegratedPointCloud();

        if (_useIntegratedPointCloud)
        {
            (width, height) = GetDepthDimensionsFromProfile(profile);
            UnityEngine.Debug.Log("[RsPointCloudInitializer] Using RsIntegratedPointCloud (GPU Direct Mode)");
        }
        else
        {
            _dataProvider = new RsDataProvider(_processingPipe);
            _dataProvider.Start();
            width = _dataProvider.FrameWidth;
            height = _dataProvider.FrameHeight;
            UnityEngine.Debug.Log("[RsPointCloudInitializer] Using RsPointCloud via RsDataProvider");
        }

        int rsLength = width * height;
        if (rsLength == 0)
        {
            UnityEngine.Debug.LogError("[RsPointCloudInitializer] Failed to get depth stream dimensions");
            return;
        }

        _compute = new RsPointCloudCompute(
            _filterShader,
            _transformShader,
            deviceController.RealSenseScanRange,
            deviceController.FrameWidth,
            maxPlaneDistance);
        _compute.InitializeBuffers(rsLength, Matrix4x4.identity);

        _frameProcessor = new RsPointCloudFrameProcessor(_compute, logger, stopwatch);

        if (!_useIntegratedPointCloud)
        {
            _rawVertices = new Vector3[rsLength];
            _rawVerticesBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3);
        }

        _isInitialized = true;
    }

    private (int width, int height) GetDepthDimensionsFromProfile(PipelineProfile profile)
    {
        using (var depth = profile.Streams
            .FirstOrDefault(s => s.Stream == Intel.RealSense.Stream.Depth && s.Format == Intel.RealSense.Format.Z16)
            ?.As<VideoStreamProfile>())
        {
            if (depth != null)
            {
                return (depth.Width, depth.Height);
            }
        }
        return (0, 0);
    }

    private void TryConnectIntegratedPointCloud()
    {
        if (_processingPipe == null || _processingPipe.profile == null) return;

        foreach (var block in _processingPipe.profile._processingBlocks)
        {
            if (block is RsIntegratedPointCloud integrated)
            {
                _integratedPointCloud = integrated;
                _useIntegratedPointCloud = true;
                UnityEngine.Debug.Log("[RsPointCloudInitializer] Connected to RsIntegratedPointCloud");
                return;
            }
        }
    }

    public void UpdateIntegratedTransform(Matrix4x4 matrix)
    {
        _integratedPointCloud?.UpdateTransformMatrix(matrix);
    }

    public void Dispose()
    {
        _dataProvider?.Dispose();
        _compute?.Dispose();

        if (_rawVerticesBuffer != null)
        {
            _rawVerticesBuffer.Release();
            _rawVerticesBuffer = null;
        }

        _isInitialized = false;
    }
}
