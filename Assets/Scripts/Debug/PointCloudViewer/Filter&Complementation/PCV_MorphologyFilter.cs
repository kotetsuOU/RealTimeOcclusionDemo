using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public static class PCV_MorphologyFilter
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct PCV_Point
    {
        public Vector4 position;
        public Color color;
    }

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

        if (settings.voxelGridBuilderShader == null)
        {
            UnityEngine.Debug.LogWarning("VoxelGridBuilder Compute Shaderが設定されていません。");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        int originalCount = dataManager.CurrentData.PointCount;
        UnityEngine.Debug.Log($"GPUによるモルフォロジー演算を開始します。(侵食: {settings.erosionIterations}回, 膨張: {settings.dilationIterations}回)");

        PCV_Data filteredData = ApplyGPU(dataManager.CurrentData,
            settings.morpologyOperationShader,
            settings.voxelGridBuilderShader,
            settings.voxelSize,
            settings.erosionIterations, settings.dilationIterations, settings.complementationPointsPerAxis,
            settings.complementationRandomPlacement);

        stopwatch.Stop();
        LogFilteringResult("モルフォロジー演算", originalCount, filteredData.PointCount, stopwatch.ElapsedMilliseconds);
        dataManager.SetData(filteredData, settings.voxelSize);
    }

    public static PCV_Data ApplyGPU(PCV_Data data, ComputeShader computeShader,
        ComputeShader gridBuilderShader,
        float voxelSize,
        int erosionIterations, int dilationIterations, uint pointsPerAxis, bool randomPlacement)
    {
        if (data == null || data.PointCount == 0 || computeShader == null || gridBuilderShader == null)
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

        PCV_GpuVoxelGrid gpuVoxelGrid = new PCV_GpuVoxelGrid(gridBuilderShader, voxelSize);
        gpuVoxelGrid.AllocateBuffers(maxBufferSize);

        int erosionKernel = computeShader.FindKernel("CSErosion");
        int dilationKernel = computeShader.FindKernel("CSDilation");
        int mergeKernel = computeShader.FindKernel("CSMerge");
        int blitKernel = computeShader.FindKernel("CSBlit");

        int currentBufferIndex = 0;
        int currentPointCount = data.PointCount;

        PCV_Point[] initialPointData = new PCV_Point[currentPointCount];

        try
        {
            for (int i = 0; i < data.PointCount; i++)
            {
                initialPointData[i] = new PCV_Point { position = data.Vertices[i], color = data.Colors[i] };
            }
            bufferA.SetData(initialPointData, 0, 0, data.PointCount);
            initialPointData = null;

            for (int iter = 0; iter < totalIterations; iter++)
            {
                if (currentPointCount == 0)
                {
                    UnityEngine.Debug.LogWarning("全ての点が削除されました。処理を中断します。");
                    break;
                }

                bool isErosion = iter < erosionIterations;

                ComputeBuffer pointsIn = (currentBufferIndex == 0) ? bufferA : bufferB;
                ComputeBuffer pointsOut = (currentBufferIndex == 0) ? bufferB : bufferA;

                gpuVoxelGrid.Build(pointsIn, currentPointCount);

                if (isErosion)
                {
                    countBuffer.SetData(new uint[] { 0 });

                    computeShader.SetBuffer(erosionKernel, "_VoxelData", gpuVoxelGrid.VoxelDataBuffer);
                    computeShader.SetBuffer(erosionKernel, "_VoxelPointIndices", gpuVoxelGrid.VoxelPointIndicesBuffer);
                    computeShader.SetBuffer(erosionKernel, "_VoxelHashTable", gpuVoxelGrid.VoxelHashTableBuffer);
                    computeShader.SetBuffer(erosionKernel, "_VoxelHashChains", gpuVoxelGrid.VoxelHashChainsBuffer);
                    computeShader.SetInt("_VoxelCount", gpuVoxelGrid.VoxelCount);
                    computeShader.SetInt("_HashTableSize", gpuVoxelGrid.HashTableSize);

                    computeShader.SetInt("_PointCountIn", currentPointCount);
                    computeShader.SetFloat("_VoxelSize", voxelSize);
                    computeShader.SetBuffer(erosionKernel, "_PointsIn", pointsIn);
                    computeShader.SetBuffer(erosionKernel, "_PointsOut", pointsOut);
                    computeShader.SetBuffer(erosionKernel, "_PointCountOut", countBuffer);

                    int threadGroups = Mathf.CeilToInt(gpuVoxelGrid.VoxelCount / 64.0f);
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

                    computeShader.SetBuffer(dilationKernel, "_VoxelData", gpuVoxelGrid.VoxelDataBuffer);
                    computeShader.SetBuffer(dilationKernel, "_VoxelPointIndices", gpuVoxelGrid.VoxelPointIndicesBuffer);
                    computeShader.SetBuffer(dilationKernel, "_VoxelHashTable", gpuVoxelGrid.VoxelHashTableBuffer);
                    computeShader.SetBuffer(dilationKernel, "_VoxelHashChains", gpuVoxelGrid.VoxelHashChainsBuffer);
                    computeShader.SetInt("_VoxelCount", gpuVoxelGrid.VoxelCount);
                    computeShader.SetInt("_HashTableSize", gpuVoxelGrid.HashTableSize);

                    computeShader.SetInt("_PointCountIn", currentPointCount);
                    computeShader.SetFloat("_VoxelSize", voxelSize);
                    computeShader.SetInt("_PointsPerAxis", (int)pointsPerAxis);
                    computeShader.SetInt("_UseRandomPlacement", randomPlacement ? 1 : 0);
                    computeShader.SetFloat("_RandomSeed", Time.time + iter);
                    computeShader.SetBuffer(dilationKernel, "_PointsIn", pointsIn);
                    computeShader.SetBuffer(dilationKernel, "_PointsOut", newPointsBuffer);
                    computeShader.SetBuffer(dilationKernel, "_PointCountOut", countBuffer);

                    int threadGroups = Mathf.CeilToInt(gpuVoxelGrid.VoxelCount / 64.0f);
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

                        gpuVoxelGrid.AllocateBuffers(maxBufferSize);

                        ComputeBuffer nextPointsIn = (currentBufferIndex == 0) ? bufferB : bufferA;

                        if (newTotalPointCount > 0)
                        {
                            computeShader.SetBuffer(blitKernel, "_PointsIn", mergedDataBuffer);
                            computeShader.SetBuffer(blitKernel, "_PointsOut", nextPointsIn);
                            computeShader.SetInt("_PointCountIn", newTotalPointCount);

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
                    filteredVertices.Add((Vector3)filteredPointData[i].position);
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

            gpuVoxelGrid.Dispose();
        }
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