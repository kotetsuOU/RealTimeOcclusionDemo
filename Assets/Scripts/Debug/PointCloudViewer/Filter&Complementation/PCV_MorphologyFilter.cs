using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
// using System.Linq; // 不要

public static class PCV_MorphologyFilter
{
    // ★ 修正: PCV_Point の定義
    // (VoxelGridBuilder.compute の struct Point (float4+float4) と
    // MorpologyOperation.compute の struct Point (float3+pad+float4)
    // が一致しないという問題があるが、ここでは MorphologyFilter.cs の
    // 元の実装 (float3+pad+float4) に合わせる)

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct PCV_Point
    {
        public Vector3 position;
        public float padding1; // 32 bytes (sizeof(float) * 8)
        public Color color;
    }

    // ★ 修正: VoxelGridBuilder の Compute Shader をインスペクターから設定
    // public ComputeShader morpologyOperationShader;
    // public ComputeShader voxelGridBuilderShader; // (PCV_Settings に追加が必要)


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

        // ★ 修正: 動的環境では VoxelGridBuilder Shader も必須
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
            settings.voxelGridBuilderShader, // ★ ビルダーシェーダーを渡す
            settings.voxelSize,
            settings.erosionIterations, settings.dilationIterations, settings.complementationPointsPerAxis,
            settings.complementationRandomPlacement);

        stopwatch.Stop();
        LogFilteringResult("モルフォロジー演算", originalCount, filteredData.PointCount, stopwatch.ElapsedMilliseconds);
        dataManager.SetData(filteredData, settings.voxelSize);
    }

    public static PCV_Data ApplyGPU(PCV_Data data, ComputeShader computeShader,
        ComputeShader gridBuilderShader, // ★ ビルダーシェーダーを受け取る
        float voxelSize,
        int erosionIterations, int dilationIterations, uint pointsPerAxis, bool randomPlacement)
    {
        if (data == null || data.PointCount == 0 || computeShader == null || gridBuilderShader == null)
        {
            return new PCV_Data(new List<Vector3>(), new List<Color>());
        }

        int pointStructSize = sizeof(float) * 8; // PCV_Point (32 bytes)
        int totalIterations = erosionIterations + dilationIterations;

        // ★ 修正: バッファサイズは点群の変動を許容する
        int maxBufferSize = data.PointCount * 10;
        ComputeBuffer bufferA = new ComputeBuffer(maxBufferSize, pointStructSize);
        ComputeBuffer bufferB = new ComputeBuffer(maxBufferSize, pointStructSize);
        ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(uint));
        ComputeBuffer newPointsBuffer = new ComputeBuffer(maxBufferSize, pointStructSize);

        // ★ 修正: GPU VoxelGrid マネージャーを作成
        PCV_GpuVoxelGrid gpuVoxelGrid = new PCV_GpuVoxelGrid(gridBuilderShader, maxBufferSize, voxelSize);

        int erosionKernel = computeShader.FindKernel("CSErosion");
        int dilationKernel = computeShader.FindKernel("CSDilation");
        int mergeKernel = computeShader.FindKernel("CSMerge");
        int blitKernel = computeShader.FindKernel("CSBlit");

        int currentBufferIndex = 0;
        int currentPointCount = data.PointCount;

        PCV_Point[] currentPointData = new PCV_Point[maxBufferSize];

        try
        {
            for (int i = 0; i < data.PointCount; i++)
            {
                currentPointData[i] = new PCV_Point { position = data.Vertices[i], padding1 = 0f, color = data.Colors[i] };
            }
            bufferA.SetData(currentPointData, 0, 0, data.PointCount);

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

                // ▼▼▼ 最大のボトルネックだったCPU処理をGPU処理に置換 ▼▼▼
                // pointsIn.GetData(...);
                // BuildVoxelGridWithHash(...);
                // voxelDataBuffer?.Release(); ...
                // voxelDataBuffer.SetData(...);

                // ★ 修正: GPU側で VoxelGrid を構築
                gpuVoxelGrid.Build(pointsIn, currentPointCount);

                // ▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲

                if (isErosion)
                {
                    countBuffer.SetData(new uint[] { 0 });

                    // ★ 修正: gpuVoxelGrid からバッファをセット
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

                    // ★ 修正: gpuVoxelGrid からバッファをセット
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

                    // バッファリサイズ処理
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

                        // ★ 修正: GpuVoxelGrid もリサイズ
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

            // ★ 修正: GpuVoxelGrid を解放
            gpuVoxelGrid.Dispose();

            // ★ 削除: CPU側で確保していたバッファは不要
            // voxelDataBuffer?.Release();
            // voxelPointIndicesBuffer?.Release();
            // voxelHashTableBuffer?.Release();
            // voxelHashChainsBuffer?.Release();
        }
    }

    // ★ 削除: CPU側 VoxelGrid 構築ロジックは GpuVoxelGrid に移行
    // private static void BuildVoxelGridWithHash(...)
    // private static uint HashVoxelIndex(...)
    // private static Vector3Int GetVoxelIndex(...)
    // private static int GetNextPrime(...)
    // private static bool IsPrime(...)
    // private struct VoxelData { ... }


    private static void LogFilteringResult(string filterName, int originalCount, int filteredCount, long elapsedMilliseconds)
    {
        UnityEngine.Debug.Log($"{filterName}処理が完了しました。処理時間: {elapsedMilliseconds} ms. 元の点数: {originalCount}, 処理後の点数: {filteredCount}");
        if (filteredCount == 0)
        {
            UnityEngine.Debug.LogWarning("全ての点が除去されました。メッシュは空になります。");
        }
    }
}