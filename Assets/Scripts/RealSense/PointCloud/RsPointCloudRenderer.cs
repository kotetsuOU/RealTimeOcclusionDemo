using Intel.RealSense;
using System;
using System.Diagnostics;
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
    [Tooltip("ログ記録を開始するフレーム番号")]
    public long startFrame = 200;
    [Tooltip("ログ記録を終了するフレーム番号")]
    public long endFrame = 1400;
    [Tooltip("既存のログファイルに追記するかどうか")]
    public bool appendLog = false;

    private RealSenseDataProvider _dataProvider;
    private PointCloudCompute _compute;
    private PerformanceLogger _logger;
    private readonly Stopwatch _stopwatch = new Stopwatch();

    private Vector3[] _rawVertices;
    private Vector3[] _globalVertices;
    private ComputeBuffer _rawVerticesBuffer;
    private int _frameCounter = 0;
    private int _finalVertexCount = 0;

    private MaterialPropertyBlock _props;
    private MeshRenderer _renderer;

    public bool IsGlobalRangeFilterEnabled { get; set; } = true;
    public Vector3 EstimatedPoint { get; private set; } = Vector3.zero;
    public Vector3 EstimatedDir { get; private set; } = Vector3.forward;
    public bool IsPerformanceLogging => UnityEngine.Application.isPlaying && _logger != null && _logger.IsLogging;

    void Start()
    {
        _dataProvider = new RealSenseDataProvider(processingPipe);
        _logger = new PerformanceLogger();
        _dataProvider.Start();

        _renderer = GetComponent<MeshRenderer>();
        _props = new MaterialPropertyBlock();

        processingPipe.OnStart += OnStartStreaming;
    }

    private void OnStartStreaming(PipelineProfile profile)
    {
        int width = _dataProvider.FrameWidth;
        int height = _dataProvider.FrameHeight;
        int rsLength = width * height;

        _compute = new PointCloudCompute(pointCloudFilterShader, pointCloudTransformerShader, rsDeviceController.RealSenseScanRange, rsDeviceController.FrameWidth, maxPlaneDistance);

        _compute.InitializeBuffers(rsLength, transform.localToWorldMatrix);

        _rawVertices = new Vector3[rsLength];
        _globalVertices = new Vector3[rsLength];

        _rawVerticesBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3);

        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    void LateUpdate()
    {
        if (_logger.IsLogging) _stopwatch.Restart();

        _frameCounter++;

        if (_dataProvider.PollForFrame(out var points))
        {
            using (points)
            {
                ProcessFrame(points);
            }
        }

        if (_stopwatch.IsRunning) _stopwatch.Stop();

        if (_finalVertexCount <= 0 || _compute == null)
        {
            return;
        }

        UpdateProceduralMesh(_finalVertexCount);
    }

    private void ProcessFrame(Points points)
    {
        if (points.VertexData != IntPtr.Zero)
        {
            points.CopyVertices(_rawVertices);

            if (_rawVerticesBuffer != null)
            {
                _rawVerticesBuffer.SetData(_rawVertices);
            }

            long discardedCount;
            int finalVertexCount;
            long totalCount = 0;

            if (IsGlobalRangeFilterEnabled)
            {
                var result = _compute.FilterAndEstimateLine(_rawVerticesBuffer, EstimatedPoint, EstimatedDir);
                finalVertexCount = result.finalCount;
                EstimatedPoint = result.point;
                EstimatedDir = result.dir;
                discardedCount = result.discardedCount;
                totalCount = result.sampledCount;
            }
            else
            {
                finalVertexCount = _compute.Transform(_rawVerticesBuffer);
                discardedCount = 0;
                totalCount = _rawVertices.Length;
            }

            _finalVertexCount = finalVertexCount;

            if (_logger.IsLogging)
            {
                _stopwatch.Stop();
                _logger.LogFrame(_frameCounter, _stopwatch.Elapsed.TotalMilliseconds, discardedCount, totalCount, IsGlobalRangeFilterEnabled);
            }
        }
    }

    private void UpdateProceduralMesh(int vertexCount)
    {
        if (vertexCount == 0 || _compute == null || _renderer == null || _renderer.sharedMaterial == null)
        {
            return;
        }

        ComputeBuffer verticesBuffer = _compute.GetFilteredVerticesBuffer();
        if (verticesBuffer == null)
        {
            UnityEngine.Debug.LogWarning("Filtered vertices buffer is null.");
            return;
        }

        _props.SetBuffer("_Vertices", verticesBuffer);
        _props.SetColor("_Color", pointCloudColor);

        Bounds bounds = new Bounds(transform.position, Vector3.one * 30f);

        Graphics.DrawProcedural(
            _renderer.material,
            bounds,
            MeshTopology.Points,
            vertexCount,
            1,
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
        processingPipe.OnStart -= OnStartStreaming;
        _dataProvider?.Dispose();
        _compute?.Dispose();
        _logger?.Dispose();
        _rawVerticesBuffer?.Release();
        _rawVerticesBuffer = null;
    }

    public void StartPerformanceLog()
    {
        _logger?.StartLogging(this.logFilePrefix, this.appendLog, this.startFrame, this.endFrame);
    }

    public void StopPerformanceLog() => _logger?.StopLogging();

    public Vector3[] GetFilteredVertices()
    {
        if (_finalVertexCount > 0 && _compute != null)
        {
            _compute.GetFilteredVerticesData(_globalVertices, _finalVertexCount);
        }

        Vector3[] result = new Vector3[_finalVertexCount];
        Array.Copy(_globalVertices, result, _finalVertexCount);
        return result;
    }
}