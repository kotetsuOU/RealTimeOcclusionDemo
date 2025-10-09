using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using System;

public static class PCV_NoiseFilter
{
    private struct Point
    {
        public Vector3 position;
        public Color color;
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

        var pointData = new Point[data.PointCount];
        for (int i = 0; i < data.PointCount; i++)
        {
            pointData[i] = new Point { position = data.Vertices[i], color = data.Colors[i] };
        }

        int pointStructSize = sizeof(float) * 3 + sizeof(float) * 4;
        var pointsBuffer = new ComputeBuffer(data.PointCount, pointStructSize);
        pointsBuffer.SetData(pointData);

        var filteredPointsBuffer = new ComputeBuffer(data.PointCount, pointStructSize, ComputeBufferType.Append);
        filteredPointsBuffer.SetCounterValue(0);

        int kernel = computeShader.FindKernel("CSMain");
        computeShader.SetInt("_PointCount", data.PointCount);
        computeShader.SetFloat("_SearchRadius", searchRadius);
        computeShader.SetInt("_NeighborThreshold", threshold);
        computeShader.SetBuffer(kernel, "_Points", pointsBuffer);
        computeShader.SetBuffer(kernel, "_FilteredPoints", filteredPointsBuffer);

        int threadGroups = Mathf.CeilToInt(data.PointCount / 64.0f);
        computeShader.Dispatch(kernel, threadGroups, 1, 1);

        var countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        ComputeBuffer.CopyCount(filteredPointsBuffer, countBuffer, 0);
        int[] countArray = { 0 };
        countBuffer.GetData(countArray);
        int filteredPointCount = countArray[0];

        var filteredVertices = new List<Vector3>();
        var filteredColors = new List<Color>();

        if (filteredPointCount > 0)
        {
            var filteredPointData = new Point[filteredPointCount];
            filteredPointsBuffer.GetData(filteredPointData, 0, 0, filteredPointCount);
            for (int i = 0; i < filteredPointCount; i++)
            {
                filteredVertices.Add(filteredPointData[i].position);
                filteredColors.Add(filteredPointData[i].color);
            }
        }

        pointsBuffer.Release();
        filteredPointsBuffer.Release();
        countBuffer.Release();

        return new PCV_Data(filteredVertices, filteredColors);
    }
}
