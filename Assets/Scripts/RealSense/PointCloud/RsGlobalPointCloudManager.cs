using System.Collections.Generic;
using UnityEngine;

public class RsGlobalPointCloudManager : MonoBehaviour
{
    public static RsGlobalPointCloudManager Instance { get; private set; }

    public enum OutputMode
    {
        MergeAll,
        SingleCamera,
        None
    }

    public enum PCAMode
    {
        Individual,
        Integrated
    }

    [Header("Settings")]
    public ComputeShader mergeComputeShader;
    public int maxTotalPoints = 3000000;

    [Header("Debug Options")]
    [Tooltip("出力モードを選択")]
    public OutputMode outputMode = OutputMode.MergeAll;

    [Tooltip("SingleCameraモード時に表示するカメラのインデックス")]
    public int debugCameraIndex = 0;

    [Header("PCA Settings")]
    [Tooltip("PCA推定モード：Individual=各カメラ個別、Integrated=統合後")]
    public PCAMode pcaMode = PCAMode.Integrated;

    [Header("References")]
    public List<RsPointCloudRenderer> renderers = new List<RsPointCloudRenderer>();

    private ComputeBuffer _globalBuffer;
    private int _kernelMerge;
    private const int STRIDE = 28; // float3 pos(12) + float3 col(12) + uint type(4)

    private Vector3 _integratedLinePoint = Vector3.zero;
    private Vector3 _integratedLineDir = Vector3.forward;
    private readonly List<RsSamplingResult> _samplingResults = new List<RsSamplingResult>();
    
    private readonly Dictionary<RsPointCloudRenderer, RsSamplingResult> _cachedSamplingResults = 
        new Dictionary<RsPointCloudRenderer, RsSamplingResult>();

    [Header("Debug Statistics")]
    [Tooltip("Enable stats tracking (exposed via public properties)")]
    [SerializeField] private bool _statsEnabled = true;
    [Tooltip("Enable async file logging (no main thread impact)")]
    [SerializeField] private bool _asyncLoggingEnabled = false;
    
    private int _pcaCallsPerSec = 0;
    private int _pcaCacheHitsPerSec = 0;
    private int _pcaCacheMissesPerSec = 0;
    private int _pcaCallsCounter = 0;
    private int _pcaCacheHitsCounter = 0;
    private int _pcaCacheMissesCounter = 0;
    private float _lastStatsResetTime = 0f;
    
    private RsAsyncStatsLogger _asyncLogger;

    public int CurrentTotalCount { get; private set; } = 0;

    public Vector3 IntegratedLinePoint => _integratedLinePoint;

    public Vector3 IntegratedLineDir => _integratedLineDir;

    public bool IsIntegratedPCAMode => pcaMode == PCAMode.Integrated;

    private void Awake()
    {
        Instance = this;
        _globalBuffer = new ComputeBuffer(maxTotalPoints, STRIDE);
        _kernelMerge = mergeComputeShader.FindKernel("MergePoints");
        
        if (_asyncLoggingEnabled)
        {
            _asyncLogger = new RsAsyncStatsLogger("GlobalPCMStats.csv");
            Debug.Log($"[GlobalPCM] Async logging enabled: {_asyncLogger.GetLogFilePath()}");
        }
    }

    private void LateUpdate()
    {
        UpdateDebugStats();
        
        switch (outputMode)
        {
            case OutputMode.MergeAll:
                ProcessMergeAll();
                break;
            case OutputMode.SingleCamera:
                ProcessSingleCamera();
                break;
            case OutputMode.None:
                CurrentTotalCount = 0;
                break;
        }

        if (pcaMode == PCAMode.Integrated)
        {
            ComputeIntegratedPCA();
        }
        
        if (_statsEnabled)
        {
            LogDebugStats();
        }
    }
    
    private void UpdateDebugStats()
    {
        float currentTime = Time.realtimeSinceStartup;
        if (currentTime - _lastStatsResetTime >= 1f)
        {
            _pcaCallsPerSec = _pcaCallsCounter;
            _pcaCacheHitsPerSec = _pcaCacheHitsCounter;
            _pcaCacheMissesPerSec = _pcaCacheMissesCounter;
            
            _pcaCallsCounter = 0;
            _pcaCacheHitsCounter = 0;
            _pcaCacheMissesCounter = 0;
            _lastStatsResetTime = currentTime;
        }
    }
    
    private void LogDebugStats()
    {
        if (_asyncLogger != null)
        {
            _asyncLogger.LogGlobalManagerStats(_pcaCallsPerSec, _pcaCacheHitsPerSec, _pcaCacheMissesPerSec);
            
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var stats = renderer.GetComputeStats();
                if (stats != null)
                {
                    _asyncLogger.LogComputeStats(
                        renderer.gameObject.name,
                        stats.FilterCallsPerSec,
                        stats.CountReadbackSkippedPerSec,
                        stats.SamplesReadbackSkippedPerSec);
                }
            }
        }
    }
    
    public int PcaCallsPerSec => _pcaCallsPerSec;
    public int PcaCacheHitsPerSec => _pcaCacheHitsPerSec;
    public int PcaCacheMissesPerSec => _pcaCacheMissesPerSec;

    private void ProcessMergeAll()
    {
        int currentTotalCount = 0;

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;

            int copiedCount = DispatchCopy(renderer, currentTotalCount);
            currentTotalCount += copiedCount;

            if (currentTotalCount >= maxTotalPoints) break;
        }

        CurrentTotalCount = currentTotalCount;
    }

    private void ProcessSingleCamera()
    {
        if (debugCameraIndex < 0 || debugCameraIndex >= renderers.Count)
        {
            CurrentTotalCount = 0;
            return;
        }

        var targetRenderer = renderers[debugCameraIndex];

        int copiedCount = DispatchCopy(targetRenderer, 0);

        CurrentTotalCount = copiedCount;
    }

    private int DispatchCopy(RsPointCloudRenderer renderer, int dstOffset)
    {
        if (renderer == null) return 0;

        ComputeBuffer srcBuffer = renderer.GetRawBuffer();
        int count = renderer.GetLastVertexCount();

        if (srcBuffer == null || count <= 0) return 0;

        if (dstOffset + count > maxTotalPoints)
        {
            count = maxTotalPoints - dstOffset;
            if (count <= 0) return 0;
        }

        mergeComputeShader.SetBuffer(_kernelMerge, "_SourceBuffer", srcBuffer);
        mergeComputeShader.SetBuffer(_kernelMerge, "_DestinationBuffer", _globalBuffer);
        mergeComputeShader.SetInt("_CopyCount", count);
        mergeComputeShader.SetInt("_DstOffset", dstOffset);
        mergeComputeShader.SetVector("_Color", renderer.pointCloudColor);

        int threadGroups = Mathf.CeilToInt(count / 256.0f);
        mergeComputeShader.Dispatch(_kernelMerge, threadGroups, 1, 1);

        return count;
    }

    private void ComputeIntegratedPCA()
    {
        _pcaCallsCounter++;
        _samplingResults.Clear();

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;

            if (renderer.TryGetLatestSamplingResult(out var samplingResult))
            {
                _samplingResults.Add(samplingResult);
                _cachedSamplingResults[renderer] = samplingResult;
            }
            else if (_cachedSamplingResults.TryGetValue(renderer, out var cached) && cached.IsValid)
            {
                _samplingResults.Add(cached);
                _pcaCacheHitsCounter++;
            }
            else
            {
                _pcaCacheMissesCounter++;
            }
        }

        if (_samplingResults.Count > 0)
        {
            var (point, dir) = RsPointCloudCompute.EstimateLineFromMergedSamples(_samplingResults);
            _integratedLinePoint = point;
            _integratedLineDir = dir;
        }
    }

    public (Vector3 point, Vector3 dir) GetLineEstimation()
    {
        return (_integratedLinePoint, _integratedLineDir);
    }

    public ComputeBuffer GetGlobalBuffer()
    {
        return _globalBuffer;
    }

    private void OnDestroy()
    {
        _globalBuffer?.Release();
        _asyncLogger?.Dispose();
    }
}
