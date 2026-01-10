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

    [Header("Debug Synthetic")]
    public bool useSyntheticData = false;
    public enum SyntheticShape { Cylinder, Cube, Sphere }
    public SyntheticShape syntheticShape = SyntheticShape.Cylinder;
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
        _logger = new PerformanceLogger();
        _renderer = GetComponent<MeshRenderer>();
        _props = new MaterialPropertyBlock();

        if (useSyntheticData)
        {
            InitializeSyntheticData();
        }
        else
        {
            _dataProvider = new RealSenseDataProvider(processingPipe);
            processingPipe.OnStart += OnStartStreaming;
        }
    }

    private void InitializeSyntheticData()
    {
        UnityEngine.Debug.Log("[RsPointCloudRenderer] Initializing Synthetic Data...");

        int width = 640; // Dummy width
        int height = 480; // Dummy height
        int rsLength = syntheticPointCount;

        // Initialize Compute with dummy values
        Vector3 scanRange = new Vector3(10f, 10f, 10f);
        _compute = new PointCloudCompute(pointCloudFilterShader, pointCloudTransformerShader, scanRange, width, maxPlaneDistance);

        _compute.InitializeBuffers(rsLength, transform.localToWorldMatrix);

        _globalVertices = new Vector3[rsLength];
        _rawVertices = new Vector3[rsLength];
        _rawVerticesBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3);

        GenerateSyntheticPoints();
        _rawVerticesBuffer.SetData(_rawVertices);

        UnityEngine.Debug.Log($"[RsPointCloudRenderer] Synthetic Data Initialized with {rsLength} points.");
    }

    private void GenerateSyntheticPoints()
    {
        UnityEngine.Random.InitState(12345); // Fixed seed for reproducibility

        for (int i = 0; i < _rawVertices.Length; i++)
        {
            Vector3 pt = Vector3.zero;
            switch (syntheticShape)
            {
                case SyntheticShape.Cylinder:
                    float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    float r = syntheticScale * 0.5f; // Radius
                    float h = UnityEngine.Random.Range(0f, syntheticScale);
                    // Cylinder along Y axis
                    pt = new Vector3(Mathf.Cos(angle) * r, h, Mathf.Sin(angle) * r);
                    break;
                case SyntheticShape.Cube:
                    pt = new Vector3(
                        UnityEngine.Random.Range(-0.5f, 0.5f),
                        UnityEngine.Random.Range(-0.5f, 0.5f),
                        UnityEngine.Random.Range(-0.5f, 0.5f)
                    ) * syntheticScale;
                    break;
                case SyntheticShape.Sphere:
                    pt = UnityEngine.Random.onUnitSphere * (syntheticScale * 0.5f);
                    break;
            }
            _rawVertices[i] = pt;
        }
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
            UnityEngine.Debug.Log("[RsPointCloudRenderer] Using RsPointCloud via RealSenseDataProvider");
        }

        int rsLength = width * height;
        if (rsLength == 0)
        {
            UnityEngine.Debug.LogError("[RsPointCloudRenderer] Failed to get depth stream dimensions");
            return;
        }

        _compute = new PointCloudCompute(pointCloudFilterShader, pointCloudTransformerShader, rsDeviceController.RealSenseScanRange, rsDeviceController.FrameWidth, maxPlaneDistance);

        _compute.InitializeBuffers(rsLength, Matrix4x4.identity);

        _globalVertices = new Vector3[rsLength];

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

                // Pass initial transform only
                _integratedPointCloud.UpdateTransformMatrix(transform.localToWorldMatrix);

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
            UnityEngine.Debug.Log($"[RsPointCloudRenderer] Current Transform Matrix (Ignored by Compute):\n{transform.localToWorldMatrix}");
        }

        /*
        if (transform.hasChanged)
        {
            _compute?.UpdateLocalToWorldMatrix(transform.localToWorldMatrix);
            
            if (_useIntegratedPointCloud && _integratedPointCloud != null)
            {
                _integratedPointCloud.UpdateTransformMatrix(transform.localToWorldMatrix);
            }

            transform.hasChanged = false;
        }
        */

        _frameCounter++;
        ComputeBuffer argsBuffer = null;

        if (useSyntheticData)
        {
            argsBuffer = ProcessSyntheticFrame();
        }
        else if (_useIntegratedPointCloud)
        {
            argsBuffer = ProcessIntegratedPointCloud();
        }
        else
        {
            if (_dataProvider != null && _dataProvider.PollForFrame(out var points))
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
            if (debugFilteredPoints)
            {
                DebugLogFilteredPoints();
            }
            UpdateProceduralMesh(argsBuffer);
        }
    }

    private ComputeBuffer ProcessSyntheticFrame()
    {
        if (_compute == null || _rawVerticesBuffer == null) return null;

        long discardedCount = 0;
        long totalCount = _rawVertices.Length;
        ComputeBuffer argsBuffer;

        if (IsGlobalRangeFilterEnabled)
        {
            var result = _compute.FilterAndEstimateLine(_rawVerticesBuffer, EstimatedPoint, EstimatedDir, (int)totalCount);
            EstimatedPoint = result.point;
            EstimatedDir = result.dir;
            discardedCount = result.discardedCount;
            totalCount = result.sampledCount;

            argsBuffer = _compute.GetArgsBuffer();
        }
        else
        {
            argsBuffer = _compute.TransformIndirect(_rawVerticesBuffer, (int)totalCount);
            discardedCount = 0;
        }

        return argsBuffer;
    }

    private void DebugLogFilteredPoints()
    {
        if (_compute == null) return;

        Vector3[] debugPoints = new Vector3[debugPointCount];
        _compute.GetFilteredVerticesData(debugPoints, debugPointCount);

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"[RsPointCloudRenderer] First {debugPointCount} Filtered Points (Global):");
        for (int i = 0; i < debugPointCount; i++)
        {
            sb.AppendLine($"  [{i}]: {debugPoints[i].ToString("F4")}");
        }
        UnityEngine.Debug.Log(sb.ToString());
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

        Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 50f);

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

    public ComputeBuffer GetRawBuffer()
    {
        return _compute?.GetFilteredVerticesBuffer();
    }

    public int GetLastVertexCount()
    {
        return _compute?.GetLastFilteredCount() ?? 0;
    }
}