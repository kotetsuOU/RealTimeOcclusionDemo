using Intel.RealSense;
using System;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
public class RsPointCloudRenderer : MonoBehaviour
{
    [Header("Dependencies & Settings")]
    public RsDeviceController rsDeviceController;
    public RsProcessingPipe processingPipe;
    [SerializeField] private ComputeShader pointCloudFilterShader;
    [SerializeField] private ComputeShader pointCloudTransformerShader;

    [Header("PointCloud Settings")]
    [SerializeField] public float maxPlaneDistance = 0.1f;
    public Color pointCloudColor = new Color(241f / 255f, 187f / 255f, 147f / 255f, 1f);
    [SerializeField, HideInInspector] private string exportFileName = "currentGlobalVertices.txt";

    [Header("Performance Logging Settings")]
    public string logFilePrefix = "PointCloudPerfLog";
    public long startFrame = 200;
    public long endFrame = 1400;
    public bool appendLog = false;

    [Header("Debug")]
    public bool showDebugMatrix = false;

    private RealSenseDataProvider _dataProvider;
    private PointCloudCompute _compute;
    private PerformanceLogger _logger;
    private readonly Stopwatch _stopwatch = new Stopwatch();

    private Vector3[] _rawVertices;
    private Vector3[] _globalVertices;
    private ComputeBuffer _rawVerticesBuffer;
    private int _frameCounter = 0;

    private MaterialPropertyBlock _props;
    private MeshRenderer _renderer;

    private RsIntegratedPointCloud _integratedPointCloud;
    private bool _useIntegratedPointCloud = false;

    public bool IsGlobalRangeFilterEnabled { get; set; } = true;
    public Vector3 EstimatedPoint { get; private set; } = Vector3.zero;
    public Vector3 EstimatedDir { get; private set; } = Vector3.forward;
    public bool IsPerformanceLogging => UnityEngine.Application.isPlaying && _logger != null && _logger.IsLogging;

    void Start()
    {
        _dataProvider = new RealSenseDataProvider(processingPipe);
        _logger = new PerformanceLogger();

        _renderer = GetComponent<MeshRenderer>();
        _props = new MaterialPropertyBlock();

        processingPipe.OnStart += OnStartStreaming;
    }

    private void OnStartStreaming(PipelineProfile profile)
    {
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
            UnityEngine.Debug.Log("[RsPointCloudRenderer] Using RsPointCloud via RealSenseDataProvider");
        }

        int rsLength = width * height;
        if (rsLength == 0)
        {
            UnityEngine.Debug.LogError("[RsPointCloudRenderer] Failed to get depth stream dimensions");
            return;
        }

        _compute = new PointCloudCompute(pointCloudFilterShader, pointCloudTransformerShader, rsDeviceController.RealSenseScanRange, rsDeviceController.FrameWidth, maxPlaneDistance);

        _compute.InitializeBuffers(rsLength, transform.localToWorldMatrix);

        _globalVertices = new Vector3[rsLength];

        if (!_useIntegratedPointCloud)
        {
            _rawVertices = new Vector3[rsLength];
            _rawVerticesBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3);
        }

        /*
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;
        */
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
                UnityEngine.Debug.Log("[RsPointCloudRenderer] Connected to RsIntegratedPointCloud");
                return;
            }
        }
    }

    private void OnIntegratedPointCloudUpdated()
    {
    }

    void LateUpdate()
    {
        if (_logger.IsLogging) _stopwatch.Restart();

        if (showDebugMatrix)
        {
            UnityEngine.Debug.Log($"[RsPointCloudRenderer] LocalToWorld Matrix:\n{transform.localToWorldMatrix}");
        }

        // Update matrix in compute shader if transform changed
        if (transform.hasChanged)
        {
            _compute?.UpdateLocalToWorldMatrix(transform.localToWorldMatrix);
            transform.hasChanged = false;
        }

        _frameCounter++;
        ComputeBuffer argsBuffer = null;

        if (_useIntegratedPointCloud)
        {
            argsBuffer = ProcessIntegratedPointCloud();
        }
        else
        {
            if (_dataProvider.PollForFrame(out var points))
            {
                using (points)
                {
                    argsBuffer = ProcessFrame(points);
                }
            }
        }

        if (_stopwatch.IsRunning) _stopwatch.Stop();

        if (argsBuffer != null)
        {
            UpdateProceduralMesh(argsBuffer);
        }
    }

    private ComputeBuffer ProcessIntegratedPointCloud()
    {
        if (_integratedPointCloud == null || _integratedPointCloud.PointCloudBuffer == null)
            return null;

        int pointCount = _integratedPointCloud.LastPointCount;
        if (pointCount == 0) return null;

        ComputeBuffer sourceBuffer = _integratedPointCloud.PointCloudBuffer;

        long discardedCount = 0;
        long totalCount = pointCount;
        ComputeBuffer argsBuffer;

        if (IsGlobalRangeFilterEnabled)
        {
            var result = _compute.FilterAndEstimateLine(sourceBuffer, EstimatedPoint, EstimatedDir, pointCount);
            EstimatedPoint = result.point;
            EstimatedDir = result.dir;
            discardedCount = result.discardedCount;
            totalCount = result.sampledCount;

            argsBuffer = _compute.GetArgsBuffer();
        }
        else
        {
            argsBuffer = _compute.TransformIndirect(sourceBuffer, pointCount);
            discardedCount = 0;
        }

        if (_logger.IsLogging)
        {
            _stopwatch.Stop();
            _logger.LogFrame(_frameCounter, _stopwatch.Elapsed.TotalMilliseconds, discardedCount, totalCount, IsGlobalRangeFilterEnabled);
        }

        return argsBuffer;
    }

    private ComputeBuffer ProcessFrame(Points points)
    {
        if (points.VertexData == IntPtr.Zero) return null;

        points.CopyVertices(_rawVertices);

        if (_rawVerticesBuffer != null)
        {
            _rawVerticesBuffer.SetData(_rawVertices);
        }

        long discardedCount = 0;
        long totalCount = _rawVertices.Length;
        ComputeBuffer argsBuffer;

        if (IsGlobalRangeFilterEnabled)
        {
            var result = _compute.FilterAndEstimateLine(_rawVerticesBuffer, EstimatedPoint, EstimatedDir);
            EstimatedPoint = result.point;
            EstimatedDir = result.dir;
            discardedCount = result.discardedCount;
            totalCount = result.sampledCount;

            argsBuffer = _compute.GetArgsBuffer();
        }
        else
        {
            argsBuffer = _compute.TransformIndirect(_rawVerticesBuffer);
            discardedCount = 0;
        }

        if (_logger.IsLogging)
        {
            _stopwatch.Stop();
            _logger.LogFrame(_frameCounter, _stopwatch.Elapsed.TotalMilliseconds, discardedCount, totalCount, IsGlobalRangeFilterEnabled);
        }
        return argsBuffer;
    }

    private void UpdateProceduralMesh(ComputeBuffer argsBuffer)
    {
        if (_compute == null || _renderer == null || _renderer.sharedMaterial == null)
        {
            return;
        }

        _props.SetBuffer("_Vertices", _compute.GetFilteredVerticesBuffer());
        _props.SetColor("_Color", pointCloudColor);

        Bounds bounds = new Bounds(transform.position, Vector3.one * 30f);

        Graphics.DrawProceduralIndirect(
            _renderer.material,
            bounds,
            MeshTopology.Points,
            argsBuffer,
            0,
            null,
            _props,
            UnityEngine.Rendering.ShadowCastingMode.Off,
            true,
            gameObject.layer
        );
    }

    private void OnDestroy()
    {
        Dispose();
    }

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

    public void StartPerformanceLog()
    {
        _logger?.StartLogging(this.logFilePrefix, this.appendLog, this.startFrame, this.endFrame);
    }

    public void StopPerformanceLog() => _logger?.StopLogging();

    public Vector3[] GetFilteredVertices()
    {
        return new Vector3[0];
    }
}