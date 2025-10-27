using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using System;

public static class PCV_NoiseFilter
{
    public static void Execute(PCV_DataManager dataManager, PCV_Settings settings, MonoBehaviour coroutineRunner)
    {
        if (dataManager.CurrentData == null || dataManager.SpatialSearch == null)
        {
            UnityEngine.Debug.LogWarning("点群データがロードされていません。処理は実行不可能です。");
            return;
        }

        if (settings.useGpuNoiseFilter && settings.pointCloudFilterShader != null)
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

        PCV_Data filteredData = FilterGPU(dataManager.CurrentData, settings.pointCloudFilterShader, settings.searchRadius, settings.neighborThreshold);

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

    public static PCV_Data FilterGPU(PCV_Data data, ComputeShader computeShader, float searchRadius, int threshold)
    {
        if (data == null || data.PointCount == 0)
        {
            return new PCV_Data(new List<Vector3>(), new List<Color>());
        }

        int pointCount = data.PointCount;
        var pointData = new PCV_Point[pointCount];
        var vertices = new Vector3[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            pointData[i] = new PCV_Point { position = data.Vertices[i], color = data.Colors[i] };
            vertices[i] = data.Vertices[i];
        }

        float voxelSize = searchRadius;
        BuildVoxelGridWithHash(vertices, pointCount, voxelSize,
            out var voxelDataList, out var pointIndicesList,
            out var hashTable, out var hashChains, out int hashTableSize);

        int pointStructSize = sizeof(float) * 8;

        ComputeBuffer pointsBuffer = new ComputeBuffer(pointCount, pointStructSize);
        ComputeBuffer filteredPointsBuffer = new ComputeBuffer(pointCount, pointStructSize, ComputeBufferType.Append);
        ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

        ComputeBuffer voxelDataBuffer = null;
        ComputeBuffer voxelPointIndicesBuffer = null;
        ComputeBuffer voxelHashTableBuffer = null;
        ComputeBuffer voxelHashChainsBuffer = null;

        try
        {
            pointsBuffer.SetData(pointData);
            filteredPointsBuffer.SetCounterValue(0);

            int voxelCount = Mathf.Max(1, voxelDataList.Count);
            int indicesCount = Mathf.Max(1, pointIndicesList.Count);

            voxelDataBuffer = new ComputeBuffer(voxelCount, sizeof(int) * 6, ComputeBufferType.Structured); // VoxelData struct
            voxelPointIndicesBuffer = new ComputeBuffer(indicesCount, sizeof(int));
            voxelHashTableBuffer = new ComputeBuffer(hashTableSize, sizeof(int));
            voxelHashChainsBuffer = new ComputeBuffer(voxelCount, sizeof(int));

            if (voxelDataList.Count > 0)
            {
                voxelDataBuffer.SetData(voxelDataList);
                voxelPointIndicesBuffer.SetData(pointIndicesList);
                voxelHashTableBuffer.SetData(hashTable);
                voxelHashChainsBuffer.SetData(hashChains);
            }
            else
            {
                voxelDataBuffer.SetData(new VoxelData[] { new VoxelData() });
                voxelPointIndicesBuffer.SetData(new int[] { 0 });
                voxelHashTableBuffer.SetData(new int[hashTableSize]);
                voxelHashChainsBuffer.SetData(new int[] { -1 });
            }

            int kernel = computeShader.FindKernel("CSMain");

            computeShader.SetInt("_PointCount", pointCount);
            computeShader.SetFloat("_SearchRadius", searchRadius);
            computeShader.SetInt("_NeighborThreshold", threshold);
            computeShader.SetFloat("_VoxelSize", voxelSize);
            computeShader.SetInt("_HashTableSize", hashTableSize);

            computeShader.SetBuffer(kernel, "_Points", pointsBuffer);
            computeShader.SetBuffer(kernel, "_FilteredPoints", filteredPointsBuffer);
            computeShader.SetBuffer(kernel, "_VoxelData", voxelDataBuffer);
            computeShader.SetBuffer(kernel, "_VoxelPointIndices", voxelPointIndicesBuffer);
            computeShader.SetBuffer(kernel, "_VoxelHashTable", voxelHashTableBuffer);
            computeShader.SetBuffer(kernel, "_VoxelHashChains", voxelHashChainsBuffer);

            int threadGroups = Mathf.CeilToInt(pointCount / 64.0f);
            computeShader.Dispatch(kernel, threadGroups, 1, 1);

            ComputeBuffer.CopyCount(filteredPointsBuffer, countBuffer, 0);
            int[] countArray = { 0 };
            countBuffer.GetData(countArray);
            int filteredPointCount = countArray[0];

            var filteredVertices = new List<Vector3>();
            var filteredColors = new List<Color>();

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
            pointsBuffer.Release();
            filteredPointsBuffer.Release();
            countBuffer.Release();

            voxelDataBuffer?.Release();
            voxelPointIndicesBuffer?.Release();
            voxelHashTableBuffer?.Release();
            voxelHashChainsBuffer?.Release();
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct PCV_Point
    {
        public Vector4 position;
        public Color color;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct VoxelData
    {
        public Vector3Int index;
        public int pointCount;
        public int dataOffset;
        public int padding;
    }

    private static void BuildVoxelGridWithHash(
        Vector3[] vertices, int pointCount, float voxelSize,
        out List<VoxelData> voxelDataList,
        out List<int> pointIndicesList,
        out int[] hashTable,
        out int[] hashChains,
        out int hashTableSize)
    {
        var voxelGrid = new Dictionary<Vector3Int, List<int>>();
        for (int i = 0; i < pointCount; i++)
        {
            Vector3Int voxelIndex = GetVoxelIndex(vertices[i], voxelSize);
            if (!voxelGrid.ContainsKey(voxelIndex))
            {
                voxelGrid[voxelIndex] = new List<int>();
            }
            voxelGrid[voxelIndex].Add(i);
        }

        hashTableSize = GetNextPrime(voxelGrid.Count * 2);
        hashTable = new int[hashTableSize];
        for (int i = 0; i < hashTableSize; i++)
        {
            hashTable[i] = -1;
        }

        voxelDataList = new List<VoxelData>();
        pointIndicesList = new List<int>();
        hashChains = new int[voxelGrid.Count];

        int currentOffset = 0;
        int voxelIdx = 0;

        foreach (var kvp in voxelGrid)
        {
            voxelDataList.Add(new VoxelData
            {
                index = kvp.Key,
                pointCount = kvp.Value.Count,
                dataOffset = currentOffset,
                padding = 0
            });

            pointIndicesList.AddRange(kvp.Value);
            currentOffset += kvp.Value.Count;

            uint hash = HashVoxelIndex(kvp.Key, (uint)hashTableSize);
            hashChains[voxelIdx] = hashTable[hash];
            hashTable[hash] = voxelIdx;

            voxelIdx++;
        }
    }

    private static uint HashVoxelIndex(Vector3Int voxelIndex, uint hashTableSize)
    {
        const uint p1 = 73856093;
        const uint p2 = 19349663;
        const uint p3 = 83492791;
        uint hash = ((uint)voxelIndex.x * p1) ^ ((uint)voxelIndex.y * p2) ^ ((uint)voxelIndex.z * p3);
        return hash % hashTableSize;
    }

    private static Vector3Int GetVoxelIndex(Vector3 point, float voxelSize)
    {
        if (voxelSize <= 0) return Vector3Int.zero;
        return new Vector3Int(
            Mathf.FloorToInt(point.x / voxelSize),
            Mathf.FloorToInt(point.y / voxelSize),
            Mathf.FloorToInt(point.z / voxelSize)
        );
    }

    private static int GetNextPrime(int min)
    {
        for (int i = min | 1; i < int.MaxValue; i += 2)
        {
            if (IsPrime(i)) return i;
        }
        return min;
    }

    private static bool IsPrime(int n)
    {
        if (n < 2) return false;
        if (n == 2 || n == 3) return true;
        if (n % 2 == 0 || n % 3 == 0) return false;
        for (int i = 5; i * i <= n; i += 6)
        {
            if (n % i == 0 || n % (i + 2) == 0) return false;
        }
        return true;
    }
}