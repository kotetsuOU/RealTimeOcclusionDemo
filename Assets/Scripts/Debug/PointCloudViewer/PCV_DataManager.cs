using UnityEngine;
using System;

public class PCV_DataManager : MonoBehaviour
{
    public PCV_Data CurrentData { get; private set; }
    public PCV_SpatialSearch SpatialSearch { get; private set; }

    public event Action<PCV_Data> OnDataUpdated;

    public void LoadAndSetData(FileSettings[] fileSettings, float voxelSize)
    {
        PCV_Data loadedData = PCV_Loader.LoadFromFiles(fileSettings);
        SetData(loadedData, voxelSize);

        if (loadedData != null && loadedData.PointCount > 0)
        {
            UnityEngine.Debug.Log($"点群が {loadedData.PointCount} 点で再構築されました。");
        }
        else
        {
            UnityEngine.Debug.LogWarning("読み込む点群データが存在しません。");
        }
    }

    public void SetData(PCV_Data newData, float voxelSize)
    {
        CurrentData = newData;
        if (CurrentData != null && CurrentData.PointCount > 0)
        {
            SpatialSearch = new PCV_SpatialSearch(CurrentData, voxelSize);
        }
        else
        {
            SpatialSearch = null;
        }
        OnDataUpdated?.Invoke(CurrentData);
    }
}