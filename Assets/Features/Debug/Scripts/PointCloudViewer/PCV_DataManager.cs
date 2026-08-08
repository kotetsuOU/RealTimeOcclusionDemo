using System;
using UnityEngine;
using System.Collections.Generic;
using Core.Logging;
#if UNITY_EDITOR
using UnityEditor; 
#endif

[AppLoggable("PCV (PointCloudViewer)")]
public class PCV_DataManager : MonoBehaviour
{
    public PCV_Data CurrentData { get; private set; }
    public event Action<PCV_Data> OnDataUpdated;

    public void LoadAndSetData(FileSettings[] fileSettings)
    {
        PCV_Data loadedData = PCV_Loader.LoadFromFiles(fileSettings);
        SetData(loadedData);

        if (loadedData != null && loadedData.PointCount > 0)
        {
            AppLogger.Log(this, PCV_LogTriggers.TagDataManager, $"点群が {loadedData.PointCount} 点で再構築されました。");
        }
        else
        {
            AppLogger.Log(this, PCV_LogTriggers.TagDataManager, "読み込む点群データが存在しません。");
        }
    }

    public void SetData(PCV_Data newData)
    {
        ReleaseAllBuffers();

        CurrentData = newData;

        OnDataUpdated?.Invoke(CurrentData);
    }

    private void OnDestroy()
    {
        ReleaseAllBuffers();
#if UNITY_EDITOR
        AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
    }

    private void OnDisable()
    {
        ReleaseAllBuffers();
#if UNITY_EDITOR
        AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
    }

    private void ReleaseAllBuffers()
    {
    }

#if UNITY_EDITOR
    private void OnEnable()
    {
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private void OnBeforeAssemblyReload()
    {
        ReleaseAllBuffers();
    }

    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
        {
            ReleaseAllBuffers();
        }
    }
#endif
}
