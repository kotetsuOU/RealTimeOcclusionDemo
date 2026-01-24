using Intel.RealSense;
using System.Diagnostics;
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

    private RsPointCloudInitializer _initializer;
    private RsPerformanceLogger _logger;
    private RsPointCloudVisualization _visualization;
    private readonly Stopwatch _stopwatch = new Stopwatch();
    private int _frameCounter = 0;

    #endregion

    #region Public Properties

    public bool IsGlobalRangeFilterEnabled { get; set; } = true;
    public Vector3 EstimatedPoint => _initializer?.FrameProcessor?.EstimatedPoint ?? Vector3.zero;
    public Vector3 EstimatedDir => _initializer?.FrameProcessor?.EstimatedDir ?? Vector3.forward;
    public bool IsPerformanceLogging => Application.isPlaying && _logger != null && _logger.IsLogging;

    #endregion

    #region Public Methods

    public RsSamplingResult GetLastSamplingResult()
    {
        return _initializer?.Compute?.LastSamplingResult ?? new RsSamplingResult();
    }

    public void StartPerformanceLog() => _logger?.StartLogging(logFilePrefix, appendLog, startFrame, endFrame);
    public void StopPerformanceLog() => _logger?.StopLogging();

    public Vector3[] GetFilteredVertices()
    {
        var compute = _initializer?.Compute;
        if (compute == null)
        {
            UnityEngine.Debug.LogWarning("[RsPointCloudRenderer] Compute instance is null.");
            return new Vector3[0];
        }

        int count = compute.GetLastFilteredCount();
        if (count <= 0)
        {
            UnityEngine.Debug.LogWarning($"[RsPointCloudRenderer] Vertex count is {count}.");
            return new Vector3[0];
        }

        Vector3[] result = new Vector3[count];
        compute.GetFilteredVerticesData(result, count);
        return result;
    }

    public ComputeBuffer GetRawBuffer() => _initializer?.Compute?.GetFilteredVerticesBuffer();
    public int GetLastVertexCount() => _initializer?.Compute?.GetLastFilteredCount() ?? 0;

    #endregion

    #region Unity Lifecycle

    void Start()
    {
        _logger = new RsPerformanceLogger();
        _visualization = new RsPointCloudVisualization(GetComponent<MeshRenderer>());
        _initializer = new RsPointCloudInitializer(processingPipe, pointCloudFilterShader, pointCloudTransformerShader);

        if (useSyntheticData)
        {
            _initializer.InitializeSynthetic(syntheticShape, syntheticPointCount, syntheticScale, maxPlaneDistance, transform.localToWorldMatrix, _logger, _stopwatch);
        }
        else
        {
            processingPipe.OnStart += OnStartStreaming;
        }
    }

    void LateUpdate()
    {
        if (!_initializer.IsInitialized) return;

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
                RsPointCloudVisualization.DebugLogFilteredPoints(_initializer.Compute, debugPointCount);
            }
            _visualization.Draw(_initializer.Compute.GetFilteredVerticesBuffer(), argsBuffer, pointCloudColor, gameObject.layer);
        }
    }

    void OnDestroy()
    {
        Dispose();
    }

    #endregion

    #region Event Handlers

    private void OnStartStreaming(PipelineProfile profile)
    {
        if (useSyntheticData) return;

        _initializer.InitializeOnStreaming(profile, rsDeviceController, maxPlaneDistance, _logger, _stopwatch);

        if (_initializer.UseIntegratedPointCloud)
        {
            _initializer.IntegratedPointCloud.OnPointCloudUpdated += OnIntegratedPointCloudUpdated;
            _initializer.UpdateIntegratedTransform(transform.localToWorldMatrix);
        }
    }

    private void OnIntegratedPointCloudUpdated() { }

    #endregion

    #region Frame Processing

    private ComputeBuffer ProcessCurrentFrame()
    {
        var processor = _initializer?.FrameProcessor;
        if (processor == null) return null;

        (Vector3 linePoint, Vector3 lineDir) = GetLineEstimation();

        if (useSyntheticData)
        {
            return processor.ProcessSyntheticFrame(
                _initializer.RawVerticesBuffer,
                _initializer.RawVertices.Length,
                linePoint, lineDir, IsGlobalRangeFilterEnabled);
        }
        
        if (_initializer.UseIntegratedPointCloud)
        {
            return ProcessIntegratedFrame(processor, linePoint, lineDir);
        }
        
        return ProcessRealSenseFrame(processor, linePoint, lineDir);
    }

    private (Vector3 point, Vector3 dir) GetLineEstimation()
    {
        if (RsGlobalPointCloudManager.Instance != null && RsGlobalPointCloudManager.Instance.IsIntegratedPCAMode)
        {
            return RsGlobalPointCloudManager.Instance.GetLineEstimation();
        }
        return (EstimatedPoint, EstimatedDir);
    }

    private ComputeBuffer ProcessIntegratedFrame(RsPointCloudFrameProcessor processor, Vector3 linePoint, Vector3 lineDir)
    {
        var integrated = _initializer.IntegratedPointCloud;
        if (integrated?.PointCloudBuffer == null) return null;

        int pointCount = integrated.LastPointCount;
        if (pointCount == 0) return null;

        return processor.ProcessIntegratedFrame(
            integrated.PointCloudBuffer, pointCount,
            linePoint, lineDir, IsGlobalRangeFilterEnabled, _frameCounter);
    }

    private ComputeBuffer ProcessRealSenseFrame(RsPointCloudFrameProcessor processor, Vector3 linePoint, Vector3 lineDir)
    {
        var dataProvider = _initializer.DataProvider;
        if (dataProvider == null || !dataProvider.PollForFrame(out var points)) return null;

        using (points)
        {
            return processor.ProcessRealSenseFrame(
                points, _initializer.RawVertices, _initializer.RawVerticesBuffer,
                linePoint, lineDir, IsGlobalRangeFilterEnabled, _frameCounter);
        }
    }

    #endregion

    #region Cleanup

    private void Dispose()
    {
        if (_initializer?.IntegratedPointCloud != null)
        {
            _initializer.IntegratedPointCloud.OnPointCloudUpdated -= OnIntegratedPointCloudUpdated;
        }

        processingPipe.OnStart -= OnStartStreaming;
        _initializer?.Dispose();
        _logger?.Dispose();
    }

    #endregion
}