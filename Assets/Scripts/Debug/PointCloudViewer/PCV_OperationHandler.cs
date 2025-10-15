using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public class PCV_OperationHandler : MonoBehaviour
{
    [SerializeField] private PCV_Settings settings;
    [SerializeField] private PCV_DataManager dataManager;

    public void ExecuteVoxelDensityFilter()
    {
        if (dataManager.CurrentData == null || dataManager.SpatialSearch == null)
        {
            UnityEngine.Debug.LogWarning("点群データがロードされていません。処理は実行不可能です。");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        int originalCount = dataManager.CurrentData.PointCount;
        UnityEngine.Debug.Log($"ボクセル密度フィルタリングを開始します。(閾値: {settings.voxelDensityThreshold})");

        var filteredData = FilterByVoxelDensity(dataManager.CurrentData, dataManager.SpatialSearch.VoxelGrid, settings.voxelDensityThreshold);

        stopwatch.Stop();
        LogFilteringResult("ボクセル密度フィルタリング", originalCount, filteredData.PointCount, stopwatch.ElapsedMilliseconds);
        dataManager.SetData(filteredData, settings.voxelSize);
    }

    public void ExecuteNoiseFilter()
    {
        if (dataManager.CurrentData == null || dataManager.SpatialSearch == null)
        {
            UnityEngine.Debug.LogWarning("点群データがロードされていません。処理は実行不可能です。");
            return;
        }

        if (settings.pointCloudFilterShader != null)
        {
            ExecuteNoiseFilteringGPU();
        }
        else
        {
            UnityEngine.Debug.LogWarning("近傍探索ノイズフィルターCompute Shaderが設定されていません。CPUで処理を実行します。");
            if (UnityEngine.Application.isPlaying)
            {
                StartCoroutine(ExecuteNoiseFilteringCPUCoroutine());
            }
            else
            {
                ExecuteNoiseFilteringCPU();
            }
        }
    }

    public void ExecuteMorphologyOperation()
    {
        if (dataManager.CurrentData == null)
        {
            UnityEngine.Debug.LogWarning("点群データがロードされていません。処理は実行不可能です。");
            return;
        }
        if (settings.morpologyOperationShader == null)
        {
            UnityEngine.Debug.LogWarning("モルフォロジー演算Compute Shaderが設定されていません。");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        int originalCount = dataManager.CurrentData.PointCount;
        UnityEngine.Debug.Log($"GPUによるモルフォロジー演算を開始します。(侵食: {settings.erosionIterations}回, 膨張: {settings.dilationIterations}回)");

        PCV_Data filteredData = PCV_MorphologyFilter.ApplyGPU(dataManager.CurrentData, settings.morpologyOperationShader, settings.voxelSize, settings.erosionIterations, settings.dilationIterations);

        stopwatch.Stop();
        LogFilteringResult("モルフォロジー演算", originalCount, filteredData.PointCount, stopwatch.ElapsedMilliseconds);
        dataManager.SetData(filteredData, settings.voxelSize);
    }

    private PCV_Data FilterByVoxelDensity(PCV_Data inputData, VoxelGrid voxelGrid, int densityThreshold)
    {
        var passedPointIndices = new HashSet<int>();

        foreach (var voxelContent in voxelGrid.Grid)
        {
            if (voxelContent.Value.Count >= densityThreshold)
            {
                foreach (int pointIndex in voxelContent.Value)
                {
                    passedPointIndices.Add(pointIndex);
                }
            }
        }

        var sortedIndices = passedPointIndices.ToList();
        sortedIndices.Sort();

        var filteredVertices = new Vector3[sortedIndices.Count];
        var filteredColors = new Color[sortedIndices.Count];

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            int originalIndex = sortedIndices[i];
            filteredVertices[i] = inputData.Vertices[originalIndex];
            filteredColors[i] = inputData.Colors[originalIndex];
        }

        return new PCV_Data(filteredVertices, filteredColors);
    }

    private void ExecuteNoiseFilteringCPU()
    {
        var stopwatch = Stopwatch.StartNew();
        int originalCount = dataManager.CurrentData.PointCount;
        UnityEngine.Debug.Log($"CPUによるノイズ除去処理を開始します。(閾値: {settings.neighborThreshold})");

        PCV_Data filteredData = PCV_NoiseFilter.FilterCPU(dataManager.CurrentData, dataManager.SpatialSearch.VoxelGrid, settings.searchRadius, settings.neighborThreshold);

        stopwatch.Stop();
        LogFilteringResult("近傍探索ノイズ除去", originalCount, filteredData.PointCount, stopwatch.ElapsedMilliseconds);
        dataManager.SetData(filteredData, settings.voxelSize);
    }

    private IEnumerator ExecuteNoiseFilteringCPUCoroutine()
    {
        var stopwatch = Stopwatch.StartNew();
        int originalCount = dataManager.CurrentData.PointCount;
        UnityEngine.Debug.Log($"CPUによるノイズ除去処理(コルーチン)を開始します。(閾値: {settings.neighborThreshold})");

        PCV_Data result = null;
        yield return PCV_NoiseFilter.FilterCPUCoroutine(dataManager.CurrentData, dataManager.SpatialSearch.VoxelGrid, settings.searchRadius, settings.neighborThreshold,
            (filteredData) => { result = filteredData; }
        );

        stopwatch.Stop();
        LogFilteringResult("近傍探索ノイズ除去", result.PointCount, result.PointCount, stopwatch.ElapsedMilliseconds);
        dataManager.SetData(result, settings.voxelSize);
    }

    private void ExecuteNoiseFilteringGPU()
    {
        var stopwatch = Stopwatch.StartNew();
        int originalCount = dataManager.CurrentData.PointCount;
        UnityEngine.Debug.Log($"GPUによる近傍探索ノイズ除去処理を開始します。(閾値: {settings.neighborThreshold})");

        PCV_Data filteredData = PCV_NoiseFilter.FilterGPU(dataManager.CurrentData, settings.pointCloudFilterShader, settings.searchRadius, settings.neighborThreshold);

        stopwatch.Stop();
        LogFilteringResult("近傍探索ノイズ除去", originalCount, filteredData.PointCount, stopwatch.ElapsedMilliseconds);
        dataManager.SetData(filteredData, settings.voxelSize);
    }

    private void LogFilteringResult(string filterName, int originalCount, int filteredCount, long elapsedMilliseconds)
    {
        UnityEngine.Debug.Log($"{filterName}処理が完了しました。処理時間: {elapsedMilliseconds} ms. 元の点数: {originalCount}, 処理後の点数: {filteredCount}");
        if (filteredCount == 0)
        {
            UnityEngine.Debug.LogWarning("全ての点が除去されました。メッシュは空になります。");
        }
    }
}