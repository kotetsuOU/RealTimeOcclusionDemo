using System;
using System.Collections.Generic;
using UnityEngine;

public class RsPointCloudGroupController : MonoBehaviour
{
    #region Public Methods

    public IEnumerable<RsPointCloudRenderer> GetChildRenderers()
    {
        foreach (Transform child in transform)
        {
            var renderer = child.GetComponent<RsPointCloudRenderer>();
            if (renderer != null)
            {
                yield return renderer;
            }
        }
    }

    public RsPointCloudRenderer GetFirstRenderer()
    {
        foreach (Transform child in transform)
        {
            var renderer = child.GetComponent<RsPointCloudRenderer>();
            if (renderer != null)
            {
                return renderer;
            }
        }
        return null;
    }

    public void ApplyToAllRenderers(Action<RsPointCloudRenderer> action)
    {
        if (action == null) return;

        foreach (var renderer in GetChildRenderers())
        {
            action.Invoke(renderer);
        }
    }

    public void ToggleAllRangeFilters()
    {
        ApplyToAllRenderers(r => r.IsGlobalRangeFilterEnabled = !r.IsGlobalRangeFilterEnabled);
    }

    public void StartAllPerformanceLogs(bool append = false)
    {
        ApplyToAllRenderers(r =>
        {
            r.appendLog = append;
            r.StartPerformanceLog();
        });
    }

    public void StopAllPerformanceLogs()
    {
        ApplyToAllRenderers(r => r.StopPerformanceLog());
    }

    public bool IsAnyPerformanceLogging()
    {
        var first = GetFirstRenderer();
        return first != null && first.IsPerformanceLogging;
    }

    public int GetRendererCount()
    {
        int count = 0;
        foreach (var _ in GetChildRenderers())
        {
            count++;
        }
        return count;
    }

    #endregion
}
