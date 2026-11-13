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
    private readonly List<RsPointCloudRenderer> _activeRenderersCache = new List<RsPointCloudRenderer>();

    public int CurrentTotalCount { get; private set; } = 0;
    public int OcclusionTotalCount => outputMode == OutputMode.MergeAll ? CurrentTotalCount : 0;

    public bool IsIntegratedPCAMode => pcaMode == PCAMode.Integrated;

    public bool IsPCADisabled => pcaMode == PCAMode.None;

    private void Awake()
    {
        Instance = this;
        _globalBuffer = new ComputeBuffer(maxTotalPoints, STRIDE);
        _kernelMerge = mergeComputeShader != null ? mergeComputeShader.FindKernel("MergePoints") : -1;
        _mergeCommandBuffer = new CommandBuffer { name = GlobalMergeSampleName };
    }

    private void LateUpdate()
    {
        RefreshActiveRenderersCache();

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
    public ComputeBuffer GetGlobalBuffer()
    {
        return _globalBuffer;
    }

    /// <summary>
    /// オクルージョン用の統合点群データを取得します。
    /// ダミー点群の3D空間X反転廃止により、現在は _globalBuffer と同一バッファを返します。
    /// </summary>
    public ComputeBuffer GetOcclusionGlobalBuffer()
    {
        return _globalBuffer;
    }

    /// <summary>
    /// 管理対象となるアクティブな RsPointCloudRenderer をキャッシュリストへ収集します。
    /// </summary>
    private void RefreshActiveRenderersCache()
    {
        _activeRenderersCache.Clear();

        if (renderers != null && renderers.Count > 0)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                if (renderer != null && renderer.gameObject.activeInHierarchy && renderer.enabled)
                {
                    _activeRenderersCache.Add(renderer);
                }
            }

            if (_activeRenderersCache.Count > 0) return;
        }

        for (int i = 0; i < transform.childCount; i++)
        {
            var renderer = transform.GetChild(i).GetComponent<RsPointCloudRenderer>();
            if (renderer != null && renderer.gameObject.activeInHierarchy && renderer.enabled)
            {
                _activeRenderersCache.Add(renderer);
            }
        }

        if (_activeRenderersCache.Count > 0) return;

#if UNITY_2023_1_OR_NEWER
        var allRenderers = FindObjectsByType<RsPointCloudRenderer>(FindObjectsSortMode.None);
#else
        var allRenderers = FindObjectsOfType<RsPointCloudRenderer>();
#endif
        for (int i = 0; i < allRenderers.Length; i++)
        {
            var renderer = allRenderers[i];
            if (renderer != null && renderer.gameObject.activeInHierarchy && renderer.enabled && renderer.transform.parent != transform)
            {
                _activeRenderersCache.Add(renderer);
            }
        }
    }

    /// <summary>
    /// 管理対象となるすべての RsPointCloudRenderer を取得するイテレータ。
    /// リストが設定されていればそれを、設定されていなければ直下の子要素およびシーン全体から取得して返します。
    /// </summary>
    public IEnumerable<RsPointCloudRenderer> GetChildRenderers()
    {
        bool hasValidRendererInList = false;
        if (renderers != null && renderers.Count > 0)
        {
            foreach (var renderer in renderers)
            {
                if (renderer != null && renderer.gameObject.activeInHierarchy && renderer.enabled)
                {
                    hasValidRendererInList = true;
                    yield return renderer;
                }
            }

            if (hasValidRendererInList) yield break;
        }

        // 直下の子オブジェクトを検索
        bool hasChildRenderer = false;
        foreach (Transform child in transform)
        {
            var renderer = child.GetComponent<RsPointCloudRenderer>();
            if (renderer != null && renderer.gameObject.activeInHierarchy && renderer.enabled)
            {
                hasChildRenderer = true;
                yield return renderer;
            }
        }

        if (hasChildRenderer) yield break;

        // シーン全体の全 RsPointCloudRenderer (RsDummyPointCloudRenderer 含む) を自動探索
#if UNITY_2023_1_OR_NEWER
        var allRenderers = FindObjectsByType<RsPointCloudRenderer>(FindObjectsSortMode.None);
#else
        var allRenderers = FindObjectsOfType<RsPointCloudRenderer>();
#endif
        foreach (var renderer in allRenderers)
        {
            if (renderer != null && renderer.gameObject.activeInHierarchy && renderer.enabled && renderer.transform.parent != transform)
            {
                yield return renderer;
            }
        }
    }

    /// <summary>
    /// 管理対象となっている最初の RsPointCloudRenderer を取得します。
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
    /// すべての管理対象レンダラーに対して、指定したアクションを一括で実行します。
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
    /// 全カメラの範囲フィルター (GlobalRangeFilter) の有効/無効を切り替えます。
    /// </summary>
    public void ToggleAllRangeFilters()
    {
        ApplyToAllRenderers(r => r.IsGlobalRangeFilterEnabled = !r.IsGlobalRangeFilterEnabled);
    }

    /// <summary>
    /// すべてのカメラの範囲フィルターが有効になっているかどうかを判定します。
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
    /// いずれかのカメラの範囲フィルターが有効になっているかどうかを判定します。
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
    /// すべてのカメラの範囲フィルター状態を一括で設定します。
    /// </summary>
    public void SetAllRangeFilters(bool enabled)
    {
        ApplyToAllRenderers(r => r.IsGlobalRangeFilterEnabled = enabled);
    }

    /// <summary>
    /// 管理対象となっているレンダラーの総数を取得します。
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
        // 確保されているグローバルバッファおよび CommandBuffer を解放します
        _globalBuffer?.Release();
        _globalBuffer = null;
        _mergeCommandBuffer?.Release();
        _mergeCommandBuffer = null;
    }
}
