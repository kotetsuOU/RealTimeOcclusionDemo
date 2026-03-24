using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 複数のRealSenseカメラや入力デバイスからの点群を一つに統合し、
/// 全体に対するPCA（主成分分析）やカメラ・フィルタのオンオフ制御を行うグローバルマネージャ。
/// </summary>
public class RsGlobalPointCloudManager : MonoBehaviour
{
    public static RsGlobalPointCloudManager Instance { get; private set; }

    public enum OutputMode
    {
        MergeAll,      // 全てのカメラ点群を統合する
        SingleCamera,  // 特定の一つのカメラ点群のみ出力する
        None           // 出力しない
    }

    public enum PCAMode
    {
        Individual, // 各カメラ側で個別にPCA計算を行う
        Integrated, // 全カメラの点群を統合した状態でPCA計算を行う
        None        // PCAを行わない
    }

    [Header("Settings")]
    [Tooltip("各カメラの点群を1つのバッファにまとめるためのComputeShader")]
    public ComputeShader mergeComputeShader;
    [Tooltip("統合後の点群の最大許容数")]
    public int maxTotalPoints = 3000000;

    [Header("Debug Options")]
    [Tooltip("出力モードを選択（全て統合、単一カメラ、出力なし）")]
    public OutputMode outputMode = OutputMode.MergeAll;

    [Tooltip("SingleCameraモード時に表示するカメラのインデックス")]
    public int debugCameraIndex = 0;

    [Header("PCA Settings")]
    [Tooltip("PCA推定モード：Individual=各カメラ個別、Integrated=統合後、None=なし")]
    public PCAMode pcaMode = PCAMode.Integrated;

    [Header("References")]
    [Tooltip("管理対象となる各PCレンダラーのリスト。空の場合は自動で子オブジェクトから取得します。")]
    public List<RsPointCloudRenderer> renderers = new List<RsPointCloudRenderer>();

    private ComputeBuffer _globalBuffer;
    private int _kernelMerge;
    // float3 pos(12) + float3 col(12) + uint type(4) = 28 bytes
    private const int STRIDE = 28;

    private Vector3 _integratedLinePoint = Vector3.zero;
    private Vector3 _integratedLineDir = Vector3.forward;
    private readonly List<RsSamplingResult> _samplingResults = new List<RsSamplingResult>();

    private readonly Dictionary<RsPointCloudRenderer, RsSamplingResult> _cachedSamplingResults = 
        new Dictionary<RsPointCloudRenderer, RsSamplingResult>();

    [Header("Debug Statistics")]
    [Tooltip("パフォーマンス等の統計情報の追跡を有効にするかどうか")]
    [SerializeField] private bool _statsEnabled = true;
    [Tooltip("PCAやキャッシュの統計をファイルへ非同期で書き出すか")]
    [SerializeField] private bool _asyncLoggingEnabled = false;

    [Tooltip("GPU計算のプロファイル情報をCSVへ書き出すか")]
    [SerializeField] private bool _gpuProfilerEnabled = false;
    
    private int _pcaCallsPerSec = 0;
    private int _pcaCacheHitsPerSec = 0;
    private int _pcaCacheMissesPerSec = 0;
    private int _pcaCallsCounter = 0;
    private int _pcaCacheHitsCounter = 0;
    private int _pcaCacheMissesCounter = 0;
    private float _lastStatsResetTime = 0f;
    
    private RsAsyncStatsLogger _asyncLogger;
    private RsGpuProfiler _gpuProfiler;

    public string GpuProfilerFilePath => _gpuProfiler != null ? _gpuProfiler.FilePath : string.Empty;

    public int CurrentTotalCount { get; private set; } = 0;

    public Vector3 IntegratedLinePoint => _integratedLinePoint;

    public Vector3 IntegratedLineDir => _integratedLineDir;

    public bool IsIntegratedPCAMode => pcaMode == PCAMode.Integrated;

    public bool IsPCADisabled => pcaMode == PCAMode.None;

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

        ApplyGpuProfilerState();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        ApplyGpuProfilerState();
    }

    private void ApplyGpuProfilerState()
    {
        if (_gpuProfilerEnabled)
        {
            if (_gpuProfiler == null)
            {
                _gpuProfiler = new RsGpuProfiler();
            }
        }
        else
        {
            _gpuProfiler?.Dispose();
            _gpuProfiler = null;
        }
    }

    private void LateUpdate()
    {
        _gpuProfiler?.BeginProfile();

        UpdateDebugStats();

        if (pcaMode == PCAMode.None)
        {
            ApplyToAllRenderers(r => r.IsGlobalRangeFilterEnabled = false);
        }
        
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

        _gpuProfiler?.EndProfile(Time.frameCount, CurrentTotalCount, _globalBuffer);
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

    /// <summary>
    /// 全ての管理対象カメラ（レンダラー）の点群を一つのグローバルバッファに統合する処理
    /// </summary>
    private void ProcessMergeAll()
    {
        int currentTotalCount = 0;

        foreach (var renderer in GetChildRenderers())
        {
            if (renderer == null) continue;

            // コピーをキューに積み、コピーされた頂点数を加算する
            int copiedCount = DispatchCopy(renderer, currentTotalCount);
            currentTotalCount += copiedCount;

            // 最大点数を超えた場合は後続の処理を打ち切る
            if (currentTotalCount >= maxTotalPoints) break;
        }

        CurrentTotalCount = currentTotalCount;
    }

    /// <summary>
    /// 指定された単一カメラの点群のみをグローバルバッファへコピーする処理
    /// </summary>
    private void ProcessSingleCamera()
    {
        var activeRenderers = new List<RsPointCloudRenderer>();
        foreach (var renderer in GetChildRenderers())
        {
            activeRenderers.Add(renderer);
        }

        if (debugCameraIndex < 0 || debugCameraIndex >= activeRenderers.Count)
        {
            CurrentTotalCount = 0;
            return;
        }

        var targetRenderer = activeRenderers[debugCameraIndex];

        int copiedCount = DispatchCopy(targetRenderer, 0);

        CurrentTotalCount = copiedCount;
    }

    /// <summary>
    /// 各レンダラーの点群バッファから、統合バッファ(globalBuffer)へオフセット位置からコピーする。
    /// CommandBufferを利用してGPU上で並列コピーを実行する。
    /// </summary>
    private int DispatchCopy(RsPointCloudRenderer renderer, int dstOffset)
    {
        if (renderer == null) return 0;

        ComputeBuffer srcBuffer = renderer.GetPCDSourceBuffer();
        int count = renderer.GetPCDSourceCount();

        if (srcBuffer == null || count <= 0) return 0;

        // 最大許容数を超えないようにクリップ
        if (dstOffset + count > maxTotalPoints)
        {
            count = maxTotalPoints - dstOffset;
            if (count <= 0) return 0;
        }

        // コピー用のComputeShaderにパラメータを設定
        mergeComputeShader.SetBuffer(_kernelMerge, "_SourceBuffer", srcBuffer);
        mergeComputeShader.SetBuffer(_kernelMerge, "_DestinationBuffer", _globalBuffer);
        mergeComputeShader.SetInt("_CopyCount", count);
        mergeComputeShader.SetInt("_DstOffset", dstOffset);
        mergeComputeShader.SetVector("_Color", renderer.pointCloudColor);

        int threadGroups = Mathf.CeilToInt(count / 256.0f);

        var cmd = new CommandBuffer { name = "RsPointCloud.GlobalMerge" };
        string sampleName = $"RsPointCloud.GlobalMerge.Dispatch/{renderer.gameObject.name}";
        cmd.BeginSample(sampleName);
        cmd.DispatchCompute(mergeComputeShader, _kernelMerge, threadGroups, 1, 1);
        cmd.EndSample(sampleName);
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();

        return count;
    }

    /// <summary>
    /// 各レンダラーから得られたサンプリング結果から、全体でのPCA（主成分分析）の軸を計算する
    /// </summary>
    private void ComputeIntegratedPCA()
    {
        _pcaCallsCounter++;
        _samplingResults.Clear();

        foreach (var renderer in GetChildRenderers())
        {
            if (renderer == null) continue;

            // 最新のPCAサンプリング結果取得を試みる
            if (renderer.TryGetLatestSamplingResult(out var samplingResult))
            {
                _samplingResults.Add(samplingResult);
                _cachedSamplingResults[renderer] = samplingResult;
            }
            else if (_cachedSamplingResults.TryGetValue(renderer, out var cached) && cached.IsValid)
            {
                // 新しい結果がない場合は、キャッシュされた前回の結果を利用する
                _samplingResults.Add(cached);
                _pcaCacheHitsCounter++;
            }
            else
            {
                _pcaCacheMissesCounter++;
            }
        }

        // 統合された全サンプリング結果群から新しい直線（軸と中心）を推定しキャッシュする
        if (_samplingResults.Count > 0)
        {
            var (point, dir) = RsPointCloudCompute.EstimateLineFromMergedSamples(_samplingResults);
            _integratedLinePoint = point;
            _integratedLineDir = dir;
        }
    }

    /// <summary>
    /// 統合PCAにより推定された主要な直線の点と方向ベクトルを取得する
    /// </summary>
    public (Vector3 point, Vector3 dir) GetLineEstimation()
    {
        return (_integratedLinePoint, _integratedLineDir);
    }

    /// <summary>
    /// 結合された全ての点群データが格納されるグローバルバッファを取得する
    /// </summary>
    public ComputeBuffer GetGlobalBuffer()
    {
        return _globalBuffer;
    }

    /// <summary>
    /// 管理対象となる全ての RsPointCloudRenderer を取得するイテレータ。
    /// リストが設定されていればそれを、設定されていなければ子オブジェクトを検索して返す。
    /// </summary>
    public IEnumerable<RsPointCloudRenderer> GetChildRenderers()
    {
        if (renderers != null && renderers.Count > 0)
        {
            foreach (var renderer in renderers)
            {
                if (renderer != null)
                {
                    yield return renderer;
                }
            }

            yield break;
        }

        foreach (Transform child in transform)
        {
            var renderer = child.GetComponent<RsPointCloudRenderer>();
            if (renderer != null)
            {
                yield return renderer;
            }
        }
    }

    /// <summary>
    /// 管理対象となっている最初の RsPointCloudRenderer を取得する
    /// </summary>
    public RsPointCloudRenderer GetFirstRenderer()
    {
        foreach (var renderer in GetChildRenderers())
        {
            return renderer;
        }

        return null;
    }

    /// <summary>
    /// 全ての管理対象レンダラーに対して、指定したアクションを一括で実行する
    /// </summary>
    public void ApplyToAllRenderers(Action<RsPointCloudRenderer> action)
    {
        if (action == null) return;

        foreach (var renderer in GetChildRenderers())
        {
            action.Invoke(renderer);
        }
    }

    /// <summary>
    /// 全カメラの範囲フィルター(GlobalRangeFilter)の有効/無効状態を切り替える
    /// </summary>
    public void ToggleAllRangeFilters()
    {
        ApplyToAllRenderers(r => r.IsGlobalRangeFilterEnabled = !r.IsGlobalRangeFilterEnabled);
    }

    /// <summary>
    /// 全てのカメラで範囲フィルターが有効になっているかどうかを判定する
    /// </summary>
    public bool AreAllRangeFiltersEnabled()
    {
        bool hasRenderer = false;

        foreach (var renderer in GetChildRenderers())
        {
            hasRenderer = true;
            if (!renderer.IsGlobalRangeFilterEnabled)
            {
                return false;
            }
        }

        return hasRenderer;
    }

    /// <summary>
    /// 一つでも範囲フィルターが有効になっているカメラが存在するかどうかを判定する
    /// </summary>
    public bool AreAnyRangeFiltersEnabled()
    {
        foreach (var renderer in GetChildRenderers())
        {
            if (renderer.IsGlobalRangeFilterEnabled)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 全てのカメラの範囲フィルター状態を一括で設定する
    /// </summary>
    public void SetAllRangeFilters(bool enabled)
    {
        ApplyToAllRenderers(r => r.IsGlobalRangeFilterEnabled = enabled);
    }

    /// <summary>
    /// 全てのカメラでパフォーマンスの計測（ロギング）を開始する
    /// </summary>
    public void StartAllPerformanceLogs(bool append = false)
    {
        ApplyToAllRenderers(r =>
        {
            r.appendLog = append;
            r.StartPerformanceLog();
        });
    }

    /// <summary>
    /// 全てのカメラのパフォーマンス計測（ロギング）を停止する
    /// </summary>
    public void StopAllPerformanceLogs()
    {
        ApplyToAllRenderers(r => r.StopPerformanceLog());
    }

    /// <summary>
    /// いずれかのレンダラーでパフォーマンス計測が実行中かどうか調べる
    /// </summary>
    public bool IsAnyPerformanceLogging()
    {
        var first = GetFirstRenderer();
        return first != null && first.IsPerformanceLogging;
    }

    /// <summary>
    /// 管理対象となっているレンダラーの総数を取得する
    /// </summary>
    public int GetRendererCount()
    {
        int count = 0;
        foreach (var _ in GetChildRenderers())
        {
            count++;
        }

        return count;
    }

    private void OnDestroy()
    {
        // 確保しているグローバルバッファ等のネイティブリソースを解放
        _globalBuffer?.Release();
        _asyncLogger?.Dispose();
        _gpuProfiler?.Dispose();
    }
}