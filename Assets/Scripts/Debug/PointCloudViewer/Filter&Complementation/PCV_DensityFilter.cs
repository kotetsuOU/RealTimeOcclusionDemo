using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using System.Runtime.InteropServices;

public static class PCV_DensityFilter
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Point
    {
        public Vector4 position;
        public Color color;
    }

    private const int POINT_SIZE = 32;

    public static void Execute(PCV_DataManager dataManager, PCV_Settings settings)
    {
        if (dataManager.CurrentData == null || dataManager.SpatialSearch == null)
        {
            UnityEngine.Debug.LogWarning("点群データがロードされていません。処理は実行不可能です。");
            return;
        }

        PCV_Data filteredData;
        var stopwatch = Stopwatch.StartNew();
        int originalCount = dataManager.CurrentData.PointCount;

        if (settings.useGpuDensityFilter && settings.densityFilterShader != null && settings.voxelGridBuilderShader != null)
        {
            UnityEngine.Debug.Log($"GPUによるボクセル密度フィルタリングを開始します。(閾値: {settings.voxelDensityThreshold})");
            filteredData = ApplyGPU(
                dataManager.CurrentData,
                settings.densityFilterShader,
                settings.voxelGridBuilderShader,
                settings.voxelSize,
                settings.voxelDensityThreshold
            );
            stopwatch.Stop();
            LogFilteringResult("ボクセル密度フィルタリング (GPU)", originalCount, filteredData.PointCount, stopwatch.ElapsedMilliseconds);
        }
        else
        {
            if (!settings.useGpuDensityFilter)
            {
                UnityEngine.Debug.Log("CPU実行が選択されています。CPUでボクセル密度フィルタリングを実行します。");
            }
            else if (settings.densityFilterShader == null)
            {
                UnityEngine.Debug.LogWarning("GPU実行が選択されていますが、密度フィルタリングCompute Shaderが設定されていません。CPUで処理を実行します。");
            }
            else if (settings.voxelGridBuilderShader == null)
            {
                UnityEngine.Debug.LogWarning("GPU実行が選択されていますが、VoxelGridBuilder Compute Shaderが設定されていません。CPUで処理を実行します。");
            }

            filteredData = ApplyCPU(dataManager.CurrentData, dataManager.SpatialSearch.VoxelGrid, settings.voxelDensityThreshold);
            stopwatch.Stop();
            LogFilteringResult("ボクセル密度フィルタリング (CPU)", originalCount, filteredData.PointCount, stopwatch.ElapsedMilliseconds);
        }

        dataManager.SetData(filteredData, settings.voxelSize);
    }

    public static PCV_Data ApplyGPU(PCV_Data data, ComputeShader computeShader, ComputeShader gridBuilderShader, float voxelSize, int densityThreshold)
    {
        if (data == null || data.PointCount == 0 || computeShader == null || gridBuilderShader == null)
        {
            UnityEngine.Debug.LogError("[PCV_DensityFilter] Invalid input parameters");
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

            int voxelCount = gpuVoxelGrid.VoxelCount;
            if (voxelCount == 0)
            {
                UnityEngine.Debug.LogWarning("[PCV_DensityFilter] No voxels to process");
                return new PCV_Data(new List<Vector3>(), new List<Color>());
            }

            filteredPointsBuffer = new ComputeBuffer(pointCount, POINT_SIZE, ComputeBufferType.Append);
            filteredPointsBuffer.SetCounterValue(0);

            int kernel = computeShader.FindKernel("CSDensityFilter");

            computeShader.SetInt("_DensityThreshold", densityThreshold);
            computeShader.SetBuffer(kernel, "_VoxelData", gpuVoxelGrid.VoxelDataBuffer);
            computeShader.SetBuffer(kernel, "_VoxelPointIndices", gpuVoxelGrid.VoxelPointIndicesBuffer);
            computeShader.SetBuffer(kernel, "_PointsIn", pointsBuffer);
            computeShader.SetBuffer(kernel, "_PointsOut", filteredPointsBuffer);

            int threadGroups = Mathf.CeilToInt(voxelCount / 64.0f);
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

    private static PCV_Data ApplyCPU(PCV_Data inputData, VoxelGrid voxelGrid, int densityThreshold)
    {
        var filteredVertices = new List<Vector3>(inputData.PointCount);
        var filteredColors = new List<Color>(inputData.PointCount);

        foreach (var voxelContent in voxelGrid.Grid)
        {
            if (voxelContent.Value.Count >= densityThreshold)
            {
                foreach (int pointIndex in voxelContent.Value)
                {
                    if (pointIndex >= 0 && pointIndex < inputData.PointCount)
                    {
                        filteredVertices.Add(inputData.Vertices[pointIndex]);
                        filteredColors.Add(inputData.Colors[pointIndex]);
                    }
                }
            }
        }

        return new PCV_Data(filteredVertices, filteredColors);
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