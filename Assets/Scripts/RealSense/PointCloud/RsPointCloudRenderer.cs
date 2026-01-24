using Intel.RealSense;
using System;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
public class RsPointCloudRenderer : MonoBehaviour
{
    #region Inspector Fields

    [Header("Dependencies & Settings")]
    public RsDeviceController rsDeviceController;
    public RsProcessingPipe processingPipe;
    [SerializeField] private ComputeShader pointCloudFilterShader;
    [SerializeField] private ComputeShader pointCloudTransformerShader;

    [Header("PointCloud Settings")]
    [SerializeField] public float maxPlaneDistance = 0.1f;
    public Color pointCloudColor = new Color(241f / 255f, 187f / 255f, 147f / 255f, 1f);
    [SerializeField, HideInInspector] private string exportFileName = "currentGlobalVertices.txt";

    [Header("Debug Synthetic")]
    public bool useSyntheticData = false;
    public RsPointCloudSyntheticData.SyntheticShape syntheticShape = RsPointCloudSyntheticData.SyntheticShape.Cylinder;
    [Range(100, 100000)] public int syntheticPointCount = 10000;
    public float syntheticScale = 1.0f;

    [Header("Debug Output")]
    public bool debugFilteredPoints = false;
    [Range(1, 20)] public int debugPointCount = 5;

    [Header("Performance Logging Settings")]
    public string logFilePrefix = "PointCloudPerfLog";
    public long startFrame = 200;
    public long endFrame = 1400;
    public bool appendLog = false;

    [Header("Debug")]
    public bool showDebugMatrix = false;

    #endregion

    #region Private Fields

    private RsDataProvider _dataProvider;
    private RsPointCloudCompute _compute;
    private RsPerformanceLogger _logger;
    private RsPointCloudFrameProcessor _frameProcessor;
    private RsPointCloudVisualization _visualization;
    private readonly Stopwatch _stopwatch = new Stopwatch();

    private Vector3[] _rawVertices;
    private ComputeBuffer _rawVerticesBuffer;
    private int _frameCounter = 0;

    private RsIntegratedPointCloud _integratedPointCloud;
    private bool _useIntegratedPointCloud = false;

    #endregion

    #region Public Properties

    public bool IsGlobalRangeFilterEnabled { get; set; } = true;
    public Vector3 EstimatedPoint => _frameProcessor?.EstimatedPoint ?? Vector3.zero;
    public Vector3 EstimatedDir => _frameProcessor?.EstimatedDir ?? Vector3.forward;
    public bool IsPerformanceLogging => Application.isPlaying && _logger != null && _logger.IsLogging;

    #endregion

    #region Public Methods

    public RsSamplingResult GetLastSamplingResult()
    {
        return _compute?.LastSamplingResult ?? new RsSamplingResult();
    }

    public void StartPerformanceLog()
    {
        _logger?.StartLogging(logFilePrefix, appendLog, startFrame, endFrame);
    }

    public void StopPerformanceLog() => _logger?.StopLogging();

    public Vector3[] GetFilteredVertices()
    {
        if (_compute == null)
        {
            UnityEngine.Debug.LogWarning("[RsPointCloudRenderer] Compute instance is null.");
            return new Vector3[0];
        }

        int count = _compute.GetLastFilteredCount();
        if (count <= 0)
        {
            UnityEngine.Debug.LogWarning($"[RsPointCloudRenderer] Vertex count is {count}.");
            return new Vector3[0];
        }

        Vector3[] result = new Vector3[count];
        _compute.GetFilteredVerticesData(result, count);
        return result;
    }

    public ComputeBuffer GetRawBuffer() => _compute?.GetFilteredVerticesBuffer();

    public int GetLastVertexCount() => _compute?.GetLastFilteredCount() ?? 0;

    #endregion

    #region Unity Lifecycle

    void Start()
    {
        _logger = new RsPerformanceLogger();
        _visualization = new RsPointCloudVisualization(GetComponent<MeshRenderer>());

        if (useSyntheticData)
        {
            InitializeSyntheticData();
        }
        else
        {
            _dataProvider = new RsDataProvider(processingPipe);
            processingPipe.OnStart += OnStartStreaming;
        }
    }

    void LateUpdate()
    {
        if (_logger.IsLogging) _stopwatch.Restart();

        if (showDebugMatrix)
        {
            UnityEngine.Debug.Log($"[RsPointCloudRenderer] Current Transform Matrix:\n{transform.localToWorldMatrix}");
        }

        _frameCounter++;
        ComputeBuffer argsBuffer = ProcessCurrentFrame();

        if (_stopwatch.IsRunning) _stopwatch.Stop();

        if (argsBuffer != null)
        {
            if (debugFilteredPoints)
            {
                RsPointCloudVisualization.DebugLogFilteredPoints(_compute, debugPointCount);
            }
            _visualization.Draw(_compute.GetFilteredVerticesBuffer(), argsBuffer, pointCloudColor, gameObject.layer);
        }
    }

    void OnDestroy()
    {
        Dispose();
    }

    #endregion

    #region Initialization

    private void InitializeSyntheticData()
    {
        UnityEngine.Debug.Log("[RsPointCloudRenderer] Initializing Synthetic Data...");

        int width = 640;
        int height = 480;
        int rsLength = syntheticPointCount;

        Vector3 scanRange = new Vector3(10f, 10f, 10f);
        _compute = new RsPointCloudCompute(pointCloudFilterShader, pointCloudTransformerShader, scanRange, width, maxPlaneDistance);
        _compute.InitializeBuffers(rsLength, transform.localToWorldMatrix);

        _frameProcessor = new RsPointCloudFrameProcessor(_compute, _logger, _stopwatch);

        _rawVertices = new Vector3[rsLength];
        _rawVerticesBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3);

        var syntheticGenerator = new RsPointCloudSyntheticData(syntheticShape, syntheticPointCount, syntheticScale);
        syntheticGenerator.GenerateInto(_rawVertices);
        _rawVerticesBuffer.SetData(_rawVertices);

        UnityEngine.Debug.Log($"[RsPointCloudRenderer] Synthetic Data Initialized with {rsLength} points.");
    }

    private void OnStartStreaming(PipelineProfile profile)
    {
        if (useSyntheticData) return;

        int width = 0;
        int height = 0;

        TryConnectIntegratedPointCloud();

        if (_useIntegratedPointCloud)
        {
            using (var depth = profile.Streams.FirstOrDefault(s => s.Stream == Intel.RealSense.Stream.Depth && s.Format == Intel.RealSense.Format.Z16)?.As<VideoStreamProfile>())
            {
                if (depth != null)
                {
                    width = depth.Width;
                    height = depth.Height;
                }
            }
            UnityEngine.Debug.Log("[RsPointCloudRenderer] Using RsIntegratedPointCloud (GPU Direct Mode)");
        }
        else
        {
            _dataProvider.Start();
            width = _dataProvider.FrameWidth;
            height = _dataProvider.FrameHeight;
            UnityEngine.Debug.Log("[RsPointCloudRenderer] Using RsPointCloud via RsDataProvider");
        }

        int rsLength = width * height;
        if (rsLength == 0)
        {
            UnityEngine.Debug.LogError("[RsPointCloudRenderer] Failed to get depth stream dimensions");
            return;
        }

        _compute = new RsPointCloudCompute(pointCloudFilterShader, pointCloudTransformerShader, rsDeviceController.RealSenseScanRange, rsDeviceController.FrameWidth, maxPlaneDistance);
        _compute.InitializeBuffers(rsLength, Matrix4x4.identity);

        _frameProcessor = new RsPointCloudFrameProcessor(_compute, _logger, _stopwatch);

        if (!_useIntegratedPointCloud)
        {
            _rawVertices = new Vector3[rsLength];
            _rawVerticesBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3);
        }
    }

    private void TryConnectIntegratedPointCloud()
    {
        if (processingPipe == null || processingPipe.profile == null) return;

        foreach (var block in processingPipe.profile._processingBlocks)
        {
            if (block is RsIntegratedPointCloud integrated)
            {
                _integratedPointCloud = integrated;
                _integratedPointCloud.OnPointCloudUpdated += OnIntegratedPointCloudUpdated;
                _useIntegratedPointCloud = true;
                _integratedPointCloud.UpdateTransformMatrix(transform.localToWorldMatrix);

                UnityEngine.Debug.Log("[RsPointCloudRenderer] Connected to RsIntegratedPointCloud");
                return;
            }
        }
    }

    private void OnIntegratedPointCloudUpdated() { }

    #endregion

    #region Frame Processing

    private ComputeBuffer ProcessCurrentFrame()
    {
        if (_frameProcessor == null) return null;

        Vector3 linePoint = EstimatedPoint;
        Vector3 lineDir = EstimatedDir;

        if (RsGlobalPointCloudManager.Instance != null && RsGlobalPointCloudManager.Instance.IsIntegratedPCAMode)
        {
            (linePoint, lineDir) = RsGlobalPointCloudManager.Instance.GetLineEstimation();
        }

        if (useSyntheticData)
        {
            return _frameProcessor.ProcessSyntheticFrame(_rawVerticesBuffer, _rawVertices.Length, linePoint, lineDir, IsGlobalRangeFilterEnabled);
        }
        else if (_useIntegratedPointCloud)
        {
            if (_integratedPointCloud == null || _integratedPointCloud.PointCloudBuffer == null)
                return null;

            int pointCount = _integratedPointCloud.LastPointCount;
            if (pointCount == 0) return null;

            return _frameProcessor.ProcessIntegratedFrame(_integratedPointCloud.PointCloudBuffer, pointCount, linePoint, lineDir, IsGlobalRangeFilterEnabled, _frameCounter);
        }
        else
        {
            if (_dataProvider != null && _dataProvider.PollForFrame(out var points))
            {
                using (points)
                {
                    return _frameProcessor.ProcessRealSenseFrame(points, _rawVertices, _rawVerticesBuffer, linePoint, lineDir, IsGlobalRangeFilterEnabled, _frameCounter);
                }
            }
        }

        return null;
    }

    #endregion

    #region Cleanup

    private void Dispose()
    {
        if (_integratedPointCloud != null)
        {
            _integratedPointCloud.OnPointCloudUpdated -= OnIntegratedPointCloudUpdated;
            _integratedPointCloud = null;
        }

        processingPipe.OnStart -= OnStartStreaming;
        _dataProvider?.Dispose();
        _compute?.Dispose();
        _logger?.Dispose();

        if (_rawVerticesBuffer != null)
        {
            _rawVerticesBuffer.Release();
            _rawVerticesBuffer = null;
        }
    }

    #endregion
}