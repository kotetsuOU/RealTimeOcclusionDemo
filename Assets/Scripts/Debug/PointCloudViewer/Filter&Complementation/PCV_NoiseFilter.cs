using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using System;

public static class PCV_NoiseFilter
{
    public static void Execute(PCV_DataManager dataManager, PCV_Settings settings, MonoBehaviour coroutineRunner)
    {
        if (dataManager.CurrentData == null || dataManager.SpatialSearch == null || dataManager.SpatialSearch.VoxelGrid == null)
        {
            UnityEngine.Debug.LogWarning("点群データまたはVoxelGridがロードされていません。処理は実行不可能です。");
            return;
        }

        bool isGpuReady = settings.useGpuNoiseFilter &&
                          settings.pointCloudFilterShader != null &&
                          dataManager.SpatialSearch.VoxelGrid.IsGpuDataReady();

        if (isGpuReady)
        {
            ExecuteGPU(dataManager, settings);
        }
        else
        {
            if (!settings.useGpuNoiseFilter)
            {
                UnityEngine.Debug.Log("CPU実行が選択されています。CPUでノイズ除去を実行します。");
            }
            else if (settings.pointCloudFilterShader == null)
            {
                UnityEngine.Debug.LogWarning("GPU実行が選択されていますが、近傍探索ノイズフィルターCompute Shaderが設定されていません。CPUで処理を実行します。");
            }
            else if (!dataManager.SpatialSearch.VoxelGrid.IsGpuDataReady())
            {
                UnityEngine.Debug.LogWarning("VoxelGridのGPUバッファが準備できていません。CPUで処理を実行します。");
            }

            if (UnityEngine.Application.isPlaying)
            {
                coroutineRunner.StartCoroutine(ExecuteCPUCoroutine(dataManager, settings));
            }
            else
            {
                ExecuteCPU(dataManager, settings);
            }
        }
    }

    private static void ExecuteCPU(PCV_DataManager dataManager, PCV_Settings settings)
    {
        var stopwatch = Stopwatch.StartNew();
        int originalCount = dataManager.CurrentData.PointCount;
        UnityEngine.Debug.Log($"CPUによるノイズ除去処理を開始します。(閾値: {settings.neighborThreshold})");

        PCV_Data filteredData = FilterCPU(dataManager.CurrentData, dataManager.SpatialSearch.VoxelGrid, settings.searchRadius, settings.neighborThreshold);

        stopwatch.Stop();
        LogFilteringResult("近傍探索ノイズ除去", originalCount, filteredData.PointCount, stopwatch.ElapsedMilliseconds);
        dataManager.SetData(filteredData, settings.voxelSize);
    }

    private static IEnumerator ExecuteCPUCoroutine(PCV_DataManager dataManager, PCV_Settings settings)
    {
        var stopwatch = Stopwatch.StartNew();
        int originalCount = dataManager.CurrentData.PointCount;
        UnityEngine.Debug.Log($"CPUによるノイズ除去処理(コルーチン)を開始します。(閾値: {settings.neighborThreshold})");

        PCV_Data result = null;
        yield return FilterCPUCoroutine(dataManager.CurrentData, dataManager.SpatialSearch.VoxelGrid, settings.searchRadius, settings.neighborThreshold,
            (filteredData) => { result = filteredData; }
        );

        stopwatch.Stop();
        LogFilteringResult("近傍探索ノイズ除去", originalCount, result.PointCount, stopwatch.ElapsedMilliseconds);
        dataManager.SetData(result, settings.voxelSize);
    }

    private static void ExecuteGPU(PCV_DataManager dataManager, PCV_Settings settings)
    {
        var stopwatch = Stopwatch.StartNew();
        int originalCount = dataManager.CurrentData.PointCount;
        UnityEngine.Debug.Log($"GPUによる近傍探索ノイズ除去処理を開始します。(閾値: {settings.neighborThreshold})");

        PCV_Data filteredData = FilterGPU(
            dataManager.CurrentData,
            dataManager.SpatialSearch.VoxelGrid,
            settings.pointCloudFilterShader,
            settings.searchRadius,
            settings.neighborThreshold
        );

        stopwatch.Stop();
        LogFilteringResult("近傍探索ノイズ除去", originalCount, filteredData.PointCount, stopwatch.ElapsedMilliseconds);
        dataManager.SetData(filteredData, settings.voxelSize);
    }

    private static void LogFilteringResult(string filterName, int originalCount, int filteredCount, long elapsedMilliseconds)
    {
        UnityEngine.Debug.Log($"{filterName}処理が完了しました。処理時間: {elapsedMilliseconds} ms. 元の点数: {originalCount}, 処理後の点数: {filteredCount}");
        if (filteredCount == 0)
        {
            UnityEngine.Debug.LogWarning("全ての点が除去されました。メッシュは空になります。");
        }
    }

    public static PCV_Data FilterCPU(PCV_Data data, VoxelGrid voxelGrid, float searchRadius, int threshold)
    {
        if (data == null || data.PointCount == 0 || voxelGrid == null)
        {
            return new PCV_Data(new List<Vector3>(), new List<Color>());
        }

        var filteredVertices = new List<Vector3>();
        var filteredColors = new List<Color>();

        for (int i = 0; i < data.PointCount; i++)
        {
            List<int> neighbors = voxelGrid.FindNeighbors(i, searchRadius);
            if (neighbors.Count >= threshold)
            {
                filteredVertices.Add(data.Vertices[i]);
                filteredColors.Add(data.Colors[i]);
            }
        }
        return new PCV_Data(filteredVertices, filteredColors);
    }

    public static IEnumerator FilterCPUCoroutine(PCV_Data data, VoxelGrid voxelGrid, float searchRadius, int threshold, Action<PCV_Data> onComplete)
    {
        if (data == null || data.PointCount == 0 || voxelGrid == null)
        {
            onComplete?.Invoke(new PCV_Data(new List<Vector3>(), new List<Color>()));
            yield break;
        }

        var filteredVertices = new List<Vector3>();
        var filteredColors = new List<Color>();
        int pointsPerFrame = 5000;

        for (int i = 0; i < data.PointCount; i++)
        {
            List<int> neighbors = voxelGrid.FindNeighbors(i, searchRadius);
            if (neighbors.Count >= threshold)
            {
                filteredVertices.Add(data.Vertices[i]);
                filteredColors.Add(data.Colors[i]);
            }

            if (i > 0 && (i + 1) % pointsPerFrame == 0)
            {
                yield return null;
            }
        }
        onComplete?.Invoke(new PCV_Data(filteredVertices, filteredColors));
    }

    public static PCV_Data FilterGPU(PCV_Data data, VoxelGrid voxelGrid, ComputeShader computeShader, float searchRadius, int threshold)
    {
        if (data == null || data.PointCount == 0 || voxelGrid == null || computeShader == null)
        {
            return new PCV_Data(new List<Vector3>(), new List<Color>());
        }

        if (!voxelGrid.IsGpuDataReady())
        {
            UnityEngine.Debug.LogError("[PCV_NoiseFilter] VoxelGridのGPUバッファが初期化されていません。");
            return data;
        }

        int pointCount = data.PointCount;
        int pointStructSize = sizeof(float) * 8;

        ComputeBuffer filteredPointsBuffer = new ComputeBuffer(pointCount, pointStructSize, ComputeBufferType.Append);
        ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

        try
        {
            filteredPointsBuffer.SetCounterValue(0);

            int kernel = computeShader.FindKernel("CSMain");

            computeShader.SetInt("_PointCount", pointCount);
            computeShader.SetFloat("_SearchRadius", searchRadius);
            computeShader.SetInt("_NeighborThreshold", threshold);
            computeShader.SetFloat("_VoxelSize", voxelGrid.VoxelSize);
            computeShader.SetInt("_HashTableSize", voxelGrid.HashTableSize);

            computeShader.SetBuffer(kernel, "_Points", voxelGrid.OriginalPointsBuffer);
            computeShader.SetBuffer(kernel, "_FilteredPoints", filteredPointsBuffer);

            computeShader.SetBuffer(kernel, "_VoxelData", voxelGrid.VoxelDataBuffer);
            computeShader.SetBuffer(kernel, "_VoxelPointIndices", voxelGrid.VoxelPointIndicesBuffer);
            computeShader.SetBuffer(kernel, "_VoxelHashTable", voxelGrid.VoxelHashTableBuffer);
            computeShader.SetBuffer(kernel, "_VoxelHashChains", voxelGrid.VoxelHashChainsBuffer);

            int threadGroups = Mathf.CeilToInt(pointCount / 64.0f);
            computeShader.Dispatch(kernel, threadGroups, 1, 1);

            ComputeBuffer.CopyCount(filteredPointsBuffer, countBuffer, 0);
            int[] countArray = { 0 };
            countBuffer.GetData(countArray);
            int filteredPointCount = countArray[0];

            var filteredVertices = new List<Vector3>(filteredPointCount);
            var filteredColors = new List<Color>(filteredPointCount);

            if (filteredPointCount > 0)
            {
                var filteredPointData = new PCV_Point[filteredPointCount];
                filteredPointsBuffer.GetData(filteredPointData, 0, 0, filteredPointCount);
                for (int i = 0; i < filteredPointCount; i++)
                {
                    filteredVertices.Add((Vector3)filteredPointData[i].position);
                    filteredColors.Add(filteredPointData[i].color);
                }
            }
            return new PCV_Data(filteredVertices, filteredColors);
        }
        finally
        {
            filteredPointsBuffer.Release();
            countBuffer.Release();
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct PCV_Point
    {
        public Vector4 position;
        public Color color;
    }
}