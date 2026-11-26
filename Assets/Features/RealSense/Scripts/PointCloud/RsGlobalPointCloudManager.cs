using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Core.Logging;

/// <summary>
/// RealSenseカメラデバイスの点群を統合し、
/// 全体に対するPCA（主成分分析）やフィルタの制御を行うグローバルマネージャクラス。
/// </summary>
[AppLoggable("RealSense (Pipeline)")]
public partial class RsGlobalPointCloudManager : MonoBehaviour
{
    public static RsGlobalPointCloudManager Instance { get; private set; }

    public enum OutputMode
    {
        MergeAll,      // すべてのカメラの点群を統合
        SingleCamera,  // 特定の1台のカメラの点群のみ出力
        None           // 出力しない
    }

    public enum PCAMode
    {
        Individual, // 各カメラ個別にPCAを計算
        Integrated, // 統合された点群を用いてPCAを計算
        None        // PCAを行わない
    }

    [Header("Settings")]
    [Tooltip("複数の点群を1つのバッファに結合するためのComputeShader")]
    public ComputeShader mergeComputeShader;
    [Tooltip("結合する点群の最大数")]
    public int maxTotalPoints = 3000000;

    [Header("Debug Options")]
    [Tooltip("出力モードの設定（すべて統合、カメラ指定、表示なし）")]
    public OutputMode outputMode = OutputMode.MergeAll;

    [Tooltip("SingleCameraモードで表示するカメラのインデックス")]
    public int debugCameraIndex = 0;

    [Header("PCA Settings")]
    [Tooltip("PCAモードの選択")]
    public PCAMode pcaMode = PCAMode.Integrated;

    [Header("References")]
    [Tooltip("管理対象とする点群レンダラーのリスト。空の場合は子オブジェクトから取得します。")]
    public List<RsPointCloudRenderer> renderers = new List<RsPointCloudRenderer>();

    private ComputeBuffer _globalBuffer;    // HCD 接触判定・オクルージョン共通の統合バッファ
    private CommandBuffer _mergeCommandBuffer;
    private int _kernelMerge = -1;
    private RsGlobalRendererRegistry _registry;

    private IReadOnlyList<RsPointCloudRenderer> _activeRenderersCache => _registry.ActiveRenderers;

    public int CurrentTotalCount { get; private set; } = 0;
    public int OcclusionTotalCount => outputMode == OutputMode.MergeAll ? CurrentTotalCount : 0;

    public bool IsIntegratedPCAMode => pcaMode == PCAMode.Integrated;

    public bool IsPCADisabled => pcaMode == PCAMode.None;

    private void Awake()
    {
        Instance = this;
        _registry = new RsGlobalRendererRegistry(transform, renderers);
        _globalBuffer = new ComputeBuffer(maxTotalPoints, STRIDE);
        _kernelMerge = mergeComputeShader != null ? mergeComputeShader.FindKernel("MergePoints") : -1;
        _mergeCommandBuffer = new CommandBuffer { name = GlobalMergeSampleName };
    }

    private void LateUpdate()
    {
        _registry.RefreshActiveCache();

        if (pcaMode == PCAMode.None)
        {
            for (int i = 0; i < _activeRenderersCache.Count; i++)
            {
                _activeRenderersCache[i].IsGlobalRangeFilterEnabled = false;
            }
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
    }

    /// <summary>
    /// HCD 接触判定用の統合点群データ（元座標）を取得します。
    /// </summary>
    public ComputeBuffer GetGlobalBuffer() => _globalBuffer;

    /// <summary>
    /// オクルージョン用の統合点群データを取得します。
    /// </summary>
    public ComputeBuffer GetOcclusionGlobalBuffer() => _globalBuffer;

    private RsGlobalRendererRegistry EnsureRegistry()
        => _registry ?? (_registry = new RsGlobalRendererRegistry(transform, renderers));

    public IEnumerable<RsPointCloudRenderer> GetChildRenderers() => EnsureRegistry().GetRenderersSnapshot();
    public RsPointCloudRenderer GetFirstRenderer() => EnsureRegistry().GetFirstRenderer();
    public void ApplyToAllRenderers(Action<RsPointCloudRenderer> action) => EnsureRegistry().ApplyToAll(action);
    public void ToggleAllRangeFilters() => ApplyToAllRenderers(r => r.IsGlobalRangeFilterEnabled = !r.IsGlobalRangeFilterEnabled);
    public bool AreAllRangeFiltersEnabled() => EnsureRegistry().AreAllRangeFiltersEnabled();
    public bool AreAnyRangeFiltersEnabled() => EnsureRegistry().AreAnyRangeFiltersEnabled();
    public void SetAllRangeFilters(bool enabled) => ApplyToAllRenderers(r => r.IsGlobalRangeFilterEnabled = enabled);
    public int GetRendererCount() => EnsureRegistry().GetRenderersSnapshot().Count;

    private void OnDestroy()
    {
        // 確保されているグローバルバッファおよび CommandBuffer を解放します
        _globalBuffer?.Release();
        _globalBuffer = null;
        _mergeCommandBuffer?.Release();
        _mergeCommandBuffer = null;
    }
}
