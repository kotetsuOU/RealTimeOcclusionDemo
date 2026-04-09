using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 複数のRealSenseカメラや入力デバイスからの点群を一つに統合し、
/// 全体に対するPCA（主成分分析）やカメラ・フィルタのオンオフ制御を行うグローバルマネージャ。
/// スクリプト分割により、Merge(合成)、PCA(主成分分析)、Stats(統計情報) の処理は別ファイルで管理されています。
/// </summary>
public partial class RsGlobalPointCloudManager : MonoBehaviour
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

    [Header("Debug Statistics")]
    [Tooltip("パフォーマンス等の統計情報の追跡を有効にするかどうか")]
    [SerializeField] private bool _statsEnabled = true;

    [Tooltip("PCAやキャッシュの統計をファイルへ非同期で書き出すか")]
    [SerializeField] private bool _asyncLoggingEnabled = false;

    [Tooltip("GPU計算のプロファイル情報をCSVへ書き出すか")]
    [SerializeField] private bool _gpuProfilerEnabled = false;

    public string GpuProfilerFilePath => _gpuProfiler != null ? _gpuProfiler.FilePath : string.Empty;

    public int CurrentTotalCount { get; private set; } = 0;

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

    public int PcaCallsPerSec => _pcaCallsPerSec;
    public int PcaCacheHitsPerSec => _pcaCacheHitsPerSec;
    public int PcaCacheMissesPerSec => _pcaCacheMissesPerSec;

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