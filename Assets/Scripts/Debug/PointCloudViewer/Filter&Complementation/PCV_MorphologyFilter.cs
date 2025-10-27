using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using System.Linq;

public static class PCV_MorphologyFilter
{
    public static void Execute(PCV_DataManager dataManager, PCV_Settings settings)
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

        PCV_Data filteredData = ApplyGPU(dataManager.CurrentData, settings.morpologyOperationShader, settings.voxelSize,
            settings.erosionIterations, settings.dilationIterations, settings.complementationPointsPerAxis,
            settings.complementationRandomPlacement);

        stopwatch.Stop();
        LogFilteringResult("モルフォロジー演算", originalCount, filteredData.PointCount, stopwatch.ElapsedMilliseconds);
        dataManager.SetData(filteredData, settings.voxelSize);
    }

    public static PCV_Data ApplyGPU(PCV_Data data, ComputeShader computeShader, float voxelSize,
        int erosionIterations, int dilationIterations, uint pointsPerAxis, bool randomPlacement)
    {
        if (data == null || data.PointCount == 0 || computeShader == null)
        {
            return new PCV_Data(new List<Vector3>(), new List<Color>());
        }

        int pointStructSize = sizeof(float) * 8;
        int totalIterations = erosionIterations + dilationIterations;

        int maxBufferSize = data.PointCount * 10;
        ComputeBuffer bufferA = new ComputeBuffer(maxBufferSize, pointStructSize);
        ComputeBuffer bufferB = new ComputeBuffer(maxBufferSize, pointStructSize);
        ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(uint));

        ComputeBuffer newPointsBuffer = new ComputeBuffer(maxBufferSize, pointStructSize);

        ComputeBuffer voxelDataBuffer = null;
        ComputeBuffer voxelPointIndicesBuffer = null;
        ComputeBuffer voxelHashTableBuffer = null;
        ComputeBuffer voxelHashChainsBuffer = null;

        int erosionKernel = computeShader.FindKernel("CSErosion");
        int dilationKernel = computeShader.FindKernel("CSDilation");
        int mergeKernel = computeShader.FindKernel("CSMerge");
        int blitKernel = computeShader.FindKernel("CSBlit");


        int currentBufferIndex = 0;
        int currentPointCount = data.PointCount;

        PCV_Point[] currentPointData = new PCV_Point[maxBufferSize];
        Vector3[] currentVertices = new Vector3[maxBufferSize];

        try
        {
            for (int i = 0; i < data.PointCount; i++)
            {
                currentPointData[i] = new PCV_Point { position = data.Vertices[i], color = data.Colors[i] };
            }
            bufferA.SetData(currentPointData, 0, 0, data.PointCount);

            for (int iter = 0; iter < totalIterations; iter++)
            {
                bool isErosion = iter < erosionIterations;

                ComputeBuffer pointsIn = (currentBufferIndex == 0) ? bufferA : bufferB;
                ComputeBuffer pointsOut = (currentBufferIndex == 0) ? bufferB : bufferA;

                pointsIn.GetData(currentPointData, 0, 0, currentPointCount);

                for (int i = 0; i < currentPointCount; i++)
                {
                    currentVertices[i] = currentPointData[i].position;
                }

                BuildVoxelGridWithHash(currentVertices, currentPointCount, voxelSize,
                    out var voxelDataList, out var pointIndicesList,
                    out var hashTable, out var hashChains, out int hashTableSize);

                voxelDataBuffer?.Release();
                voxelPointIndicesBuffer?.Release();
                voxelHashTableBuffer?.Release();
                voxelHashChainsBuffer?.Release();

                int voxelCount = Mathf.Max(1, voxelDataList.Count);
                int indicesCount = Mathf.Max(1, pointIndicesList.Count);

                voxelDataBuffer = new ComputeBuffer(voxelCount, sizeof(int) * 6, ComputeBufferType.Structured);
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

                if (isErosion)
                {
                    countBuffer.SetData(new uint[] { 0 });
                    computeShader.SetBuffer(erosionKernel, "_VoxelData", voxelDataBuffer);
                    computeShader.SetBuffer(erosionKernel, "_VoxelPointIndices", voxelPointIndicesBuffer);
                    computeShader.SetBuffer(erosionKernel, "_VoxelHashTable", voxelHashTableBuffer);
                    computeShader.SetBuffer(erosionKernel, "_VoxelHashChains", voxelHashChainsBuffer);
                    computeShader.SetInt("_VoxelCount", voxelDataList.Count);
                    computeShader.SetInt("_HashTableSize", hashTableSize);
                    computeShader.SetInt("_PointCountIn", currentPointCount);
                    computeShader.SetFloat("_VoxelSize", voxelSize);
                    computeShader.SetBuffer(erosionKernel, "_PointsIn", pointsIn);
                    computeShader.SetBuffer(erosionKernel, "_PointsOut", pointsOut); // Erosionは直接PointsOutへ
                    computeShader.SetBuffer(erosionKernel, "_PointCountOut", countBuffer);

                    int threadGroups = Mathf.CeilToInt(voxelCount / 64.0f);
                    if (threadGroups > 0)
                    {
                        computeShader.Dispatch(erosionKernel, threadGroups, 1, 1);
                    }

                    uint[] countArray = { 0 };
                    countBuffer.GetData(countArray);
                    currentPointCount = (int)countArray[0];

                    currentBufferIndex = 1 - currentBufferIndex;
                }
                else // Dilation
                {
                    countBuffer.SetData(new uint[] { 0 });

                    computeShader.SetBuffer(dilationKernel, "_VoxelData", voxelDataBuffer);
                    computeShader.SetBuffer(dilationKernel, "_VoxelPointIndices", voxelPointIndicesBuffer);
                    computeShader.SetBuffer(dilationKernel, "_VoxelHashTable", voxelHashTableBuffer);
                    computeShader.SetBuffer(dilationKernel, "_VoxelHashChains", voxelHashChainsBuffer);
                    computeShader.SetInt("_VoxelCount", voxelDataList.Count);
                    computeShader.SetInt("_HashTableSize", hashTableSize);
                    computeShader.SetInt("_PointCountIn", currentPointCount);
                    computeShader.SetFloat("_VoxelSize", voxelSize);
                    computeShader.SetInt("_PointsPerAxis", (int)pointsPerAxis);
                    computeShader.SetInt("_UseRandomPlacement", randomPlacement ? 1 : 0);
                    computeShader.SetFloat("_RandomSeed", Time.time + iter);
                    computeShader.SetBuffer(dilationKernel, "_PointsIn", pointsIn);
                    computeShader.SetBuffer(dilationKernel, "_PointsOut", newPointsBuffer);
                    computeShader.SetBuffer(dilationKernel, "_PointCountOut", countBuffer);

                    int threadGroups = Mathf.CeilToInt(voxelCount / 64.0f);
                    if (threadGroups > 0)
                    {
                        computeShader.Dispatch(dilationKernel, threadGroups, 1, 1);
                    }

                    uint[] countArray = { 0 };
                    countBuffer.GetData(countArray);
                    int newPointCount = (int)countArray[0];

                    computeShader.SetBuffer(mergeKernel, "_PointsIn", pointsIn);
                    computeShader.SetBuffer(mergeKernel, "_NewPointsIn", newPointsBuffer);
                    computeShader.SetBuffer(mergeKernel, "_PointsOut", pointsOut);
                    computeShader.SetInt("_PointCountIn", currentPointCount);
                    computeShader.SetInt("_NewPointCountIn", newPointCount);

                    int mergeThreadGroups = Mathf.CeilToInt((currentPointCount + newPointCount) / 64.0f);
                    if (mergeThreadGroups > 0)
                    {
                        computeShader.Dispatch(mergeKernel, mergeThreadGroups, 1, 1);
                    }

                    int newTotalPointCount = currentPointCount + newPointCount;

                    if (newTotalPointCount > maxBufferSize)
                    {
                        UnityEngine.Debug.LogWarning($"バッファサイズを超えました。{maxBufferSize} -> {newTotalPointCount * 2}");

                        ComputeBuffer oldBufferA = bufferA;
                        ComputeBuffer oldBufferB = bufferB;
                        ComputeBuffer oldNewPointsBuffer = newPointsBuffer;

                        ComputeBuffer mergedDataBuffer = (currentBufferIndex == 0) ? oldBufferB : oldBufferA;

                        maxBufferSize = newTotalPointCount * 2;
                        bufferA = new ComputeBuffer(maxBufferSize, pointStructSize);
                        bufferB = new ComputeBuffer(maxBufferSize, pointStructSize);
                        newPointsBuffer = new ComputeBuffer(maxBufferSize, pointStructSize);

                        ComputeBuffer nextPointsIn = (currentBufferIndex == 0) ? bufferB : bufferA;

                        if (newTotalPointCount > 0)
                        {
                            computeShader.SetBuffer(blitKernel, "_PointsIn", mergedDataBuffer);
                            computeShader.SetBuffer(blitKernel, "_PointsOut", nextPointsIn);
                            computeShader.SetInt("_PointCountIn", newTotalPointCount); // コピーする点数を指定

                            int blitThreadGroups = Mathf.CeilToInt(newTotalPointCount / 64.0f);
                            computeShader.Dispatch(blitKernel, blitThreadGroups, 1, 1);
                        }

                        oldBufferA.Release();
                        oldBufferB.Release();
                        oldNewPointsBuffer.Release();
                    }

                    currentPointCount = newTotalPointCount;
                    currentBufferIndex = 1 - currentBufferIndex;
                }

                if (currentPointCount == 0)
                {
                    UnityEngine.Debug.LogWarning("全ての点が削除されました。処理を中断します。");
                    break;
                }
            }

            ComputeBuffer finalBuffer = (currentBufferIndex == 0) ? bufferA : bufferB;
            var filteredVertices = new List<Vector3>();
            var filteredColors = new List<Color>();

            if (currentPointCount > 0)
            {
                var filteredPointData = new PCV_Point[currentPointCount];
                finalBuffer.GetData(filteredPointData, 0, 0, currentPointCount);
                for (int i = 0; i < currentPointCount; i++)
                {
                    filteredVertices.Add(filteredPointData[i].position);
                    filteredColors.Add(filteredPointData[i].color);
                }
            }

            return new PCV_Data(filteredVertices, filteredColors);
        }
        finally
        {
            bufferA.Release();
            bufferB.Release();
            countBuffer.Release();
            newPointsBuffer.Release();
            voxelDataBuffer?.Release();
            voxelPointIndicesBuffer?.Release();
            voxelHashTableBuffer?.Release();
            voxelHashChainsBuffer?.Release();
        }
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

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct VoxelData
    {
        public Vector3Int index;
        public int pointCount;
        public int dataOffset;
        public int padding;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct PCV_Point
    {
        public Vector3 position;
        public float padding1;
        public Color color;
    }


    private static void LogFilteringResult(string filterName, int originalCount, int filteredCount, long elapsedMilliseconds)
    {
        UnityEngine.Debug.Log($"{filterName}処理が完了しました。処理時間: {elapsedMilliseconds} ms. 元の点数: {originalCount}, 処理後の点数: {filteredCount}");
        if (filteredCount == 0)
        {
            UnityEngine.Debug.LogWarning("全ての点が除去されました。メッシュは空になります。");
        }
    }
}