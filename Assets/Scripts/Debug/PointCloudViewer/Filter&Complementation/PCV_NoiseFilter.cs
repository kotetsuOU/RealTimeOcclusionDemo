using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using System;
using System.Runtime.InteropServices;

public static class PCV_NoiseFilter
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Point
    {
        public Vector4 position;
        public Color color;
    }

    private const int POINT_SIZE = 32;

    public static void Execute(PCV_DataManager dataManager, PCV_Settings settings, MonoBehaviour coroutineRunner)
    {
        if (dataManager.CurrentData == null || dataManager.SpatialSearch == null || dataManager.SpatialSearch.VoxelGrid == null)
        {
            UnityEngine.Debug.LogWarning("点群データまたはVoxelGridがロードされていません。処理は実行不可能です。");
            return;
        }

        bool isGpuReady = settings.useGpuNoiseFilter &&
                          settings.pointCloudFilterShader != null &&
                          settings.voxelGridBuilderShader != null;

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
            else if (settings.voxelGridBuilderShader == null)
            {
                UnityEngine.Debug.LogWarning("GPU実行が選択されていますが、VoxelGridBuilder Compute Shaderが設定されていません。CPUで処理を実行します。");
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
            settings.pointCloudFilterShader,
            settings.voxelGridBuilderShader,
            settings.voxelSize,
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

    public static PCV_Data FilterGPU(PCV_Data data, ComputeShader computeShader, ComputeShader gridBuilderShader, float voxelSize, float searchRadius, int threshold)
    {
        if (data == null || data.PointCount == 0 || computeShader == null || gridBuilderShader == null)
        {
            return new PCV_Data(new List<Vector3>(), new List<Color>());
        }

        int pointCount = data.PointCount;

        ComputeBuffer pointsBuffer = null;
        ComputeBuffer filteredPointsBuffer = null;
        ComputeBuffer countBuffer = null;
        PCV_GpuVoxelGrid gpuVoxelGrid = null;

        try
        {
            var pointArray = new Point[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                pointArray[i].position = new Vector4(data.Vertices[i].x, data.Vertices[i].y, data.Vertices[i].z, 0f);
                pointArray[i].color = data.Colors[i];
            }

            pointsBuffer = new ComputeBuffer(pointCount, POINT_SIZE);
            pointsBuffer.SetData(pointArray);

            gpuVoxelGrid = new PCV_GpuVoxelGrid(gridBuilderShader, voxelSize);
            gpuVoxelGrid.AllocateBuffers(pointCount);
            gpuVoxelGrid.Build(pointsBuffer, pointCount);

            filteredPointsBuffer = new ComputeBuffer(pointCount, POINT_SIZE, ComputeBufferType.Append);
            filteredPointsBuffer.SetCounterValue(0);

            int kernel = computeShader.FindKernel("CSMain");

            computeShader.SetInt("_PointCount", pointCount);
            computeShader.SetFloat("_SearchRadius", searchRadius);
            computeShader.SetInt("_NeighborThreshold", threshold);
            computeShader.SetFloat("_VoxelSize", voxelSize);
            computeShader.SetInt("_HashTableSize", gpuVoxelGrid.HashTableSize);

            computeShader.SetBuffer(kernel, "_Points", pointsBuffer);
            computeShader.SetBuffer(kernel, "_FilteredPoints", filteredPointsBuffer);

            computeShader.SetBuffer(kernel, "_VoxelData", gpuVoxelGrid.VoxelDataBuffer);
            computeShader.SetBuffer(kernel, "_VoxelPointIndices", gpuVoxelGrid.VoxelPointIndicesBuffer);
            computeShader.SetBuffer(kernel, "_VoxelHashTable", gpuVoxelGrid.VoxelHashTableBuffer);
            computeShader.SetBuffer(kernel, "_VoxelHashChains", gpuVoxelGrid.VoxelHashChainsBuffer);

            int threadGroups = Mathf.CeilToInt(pointCount / 64.0f);
            computeShader.Dispatch(kernel, threadGroups, 1, 1);

            countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
            ComputeBuffer.CopyCount(filteredPointsBuffer, countBuffer, 0);

            int[] countArray = { 0 };
            countBuffer.GetData(countArray);
            int filteredPointCount = countArray[0];

            var filteredVertices = new List<Vector3>(filteredPointCount);
            var filteredColors = new List<Color>(filteredPointCount);

            if (filteredPointCount > 0)
            {
                var filteredPointData = new Point[filteredPointCount];
                filteredPointsBuffer.GetData(filteredPointData, 0, 0, filteredPointCount);
                for (int i = 0; i < filteredPointCount; i++)
                {
                    filteredVertices.Add(new Vector3(
                        filteredPointData[i].position.x,
                        filteredPointData[i].position.y,
                        filteredPointData[i].position.z
                    ));
                    filteredColors.Add(filteredPointData[i].color);
                }
            }
            return new PCV_Data(filteredVertices, filteredColors);
        }
        finally
        {
            pointsBuffer?.Release();
            filteredPointsBuffer?.Release();
            countBuffer?.Release();
            gpuVoxelGrid?.Dispose();
        }
    }
}