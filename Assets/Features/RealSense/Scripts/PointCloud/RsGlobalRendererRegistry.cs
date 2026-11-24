using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// <see cref="RsGlobalPointCloudManager"/> が管理する <see cref="RsPointCloudRenderer"/> の収集・キャッシュ・一括操作を担うヘルパー。
/// 毎フレームの探索アロケーションを抑えつつ、リスト指定・子要素・シーン全体の優先順位でレンダラーを解決します。
/// </summary>
public sealed class RsGlobalRendererRegistry
{
    private readonly Transform _ownerTransform;
    private readonly List<RsPointCloudRenderer> _configuredRenderers;
    private readonly List<RsPointCloudRenderer> _activeCache = new List<RsPointCloudRenderer>();

    private readonly List<RsPointCloudRenderer> _sceneScanCache = new List<RsPointCloudRenderer>();
    private int _lastSceneScanFrame = -100;
    private const int SceneScanIntervalFrames = 30;

    public IReadOnlyList<RsPointCloudRenderer> ActiveRenderers => _activeCache;

    public RsGlobalRendererRegistry(Transform ownerTransform, List<RsPointCloudRenderer> configuredRenderers)
    {
        _ownerTransform = ownerTransform;
        _configuredRenderers = configuredRenderers;
    }

    /// <summary>
    /// アクティブなレンダラーキャッシュを最新の状態に更新する
    /// </summary>
    public void RefreshActiveCache()
    {
        _activeCache.Clear();

        if (_configuredRenderers != null && _configuredRenderers.Count > 0)
        {
            for (int i = 0; i < _configuredRenderers.Count; i++)
            {
                var r = _configuredRenderers[i];
                if (IsValidActive(r))
                {
                    _activeCache.Add(r);
                }
            }

            if (_activeCache.Count > 0) return;
        }

        if (_ownerTransform != null)
        {
            for (int i = 0; i < _ownerTransform.childCount; i++)
            {
                var r = _ownerTransform.GetChild(i).GetComponent<RsPointCloudRenderer>();
                if (IsValidActive(r))
                {
                    _activeCache.Add(r);
                }
            }

            if (_activeCache.Count > 0) return;
        }

        // シーン全体探索は負荷が高いため、一定フレーム間隔でのみ再スキャンし、間はキャッシュを再利用する
        if (Time.frameCount - _lastSceneScanFrame >= SceneScanIntervalFrames)
        {
            _lastSceneScanFrame = Time.frameCount;
            _sceneScanCache.Clear();
#if UNITY_2023_1_OR_NEWER
            var allRenderers = UnityEngine.Object.FindObjectsByType<RsPointCloudRenderer>(FindObjectsSortMode.None);
#else
            var allRenderers = UnityEngine.Object.FindObjectsOfType<RsPointCloudRenderer>();
#endif
            for (int i = 0; i < allRenderers.Length; i++)
            {
                var r = allRenderers[i];
                if (IsValidActive(r) && (_ownerTransform == null || r.transform.parent != _ownerTransform))
                {
                    _sceneScanCache.Add(r);
                }
            }
        }

        for (int i = 0; i < _sceneScanCache.Count; i++)
        {
            var r = _sceneScanCache[i];
            if (IsValidActive(r) && (_ownerTransform == null || r.transform.parent != _ownerTransform))
            {
                _activeCache.Add(r);
            }
        }
    }

    public IReadOnlyList<RsPointCloudRenderer> GetRenderersSnapshot()
    {
        RefreshActiveCache();
        return _activeCache;
    }

    public RsPointCloudRenderer GetFirstRenderer()
    {
        RefreshActiveCache();
        return _activeCache.Count > 0 ? _activeCache[0] : null;
    }

    public void ApplyToAll(Action<RsPointCloudRenderer> action)
    {
        if (action == null) return;
        RefreshActiveCache();
        for (int i = 0; i < _activeCache.Count; i++)
        {
            action.Invoke(_activeCache[i]);
        }
    }

    public bool AreAllRangeFiltersEnabled()
    {
        RefreshActiveCache();
        if (_activeCache.Count == 0) return false;

        for (int i = 0; i < _activeCache.Count; i++)
        {
            if (!_activeCache[i].IsGlobalRangeFilterEnabled)
                return false;
        }
        return true;
    }

    public bool AreAnyRangeFiltersEnabled()
    {
        RefreshActiveCache();
        for (int i = 0; i < _activeCache.Count; i++)
        {
            if (_activeCache[i].IsGlobalRangeFilterEnabled)
                return true;
        }
        return false;
    }

    private static bool IsValidActive(RsPointCloudRenderer renderer)
    {
        return renderer != null && renderer.gameObject.activeInHierarchy && renderer.enabled;
    }
}
