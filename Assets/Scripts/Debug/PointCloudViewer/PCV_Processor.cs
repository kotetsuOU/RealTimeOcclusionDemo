using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class PCV_Processor
{
    private readonly PCV_Data data;
    private readonly VoxelGrid voxelGrid;

    public PCV_Processor(PCV_Data pointCloudData, float voxelSize)
    {
        this.data = pointCloudData;
        if (this.data != null && this.data.PointCount > 0)
        {
            this.voxelGrid = new VoxelGrid(this.data.Vertices, voxelSize);
        }
    }

    public bool FindClosestPoint(Ray ray, float maxDistance, out int closestIndex)
    {
        closestIndex = -1;
        if (data == null || data.PointCount == 0) return false;

        float minDistanceSq = float.MaxValue;
        float maxDistanceSq = maxDistance * maxDistance;

        for (int i = 0; i < data.PointCount; i++)
        {
            Vector3 point = data.Vertices[i];
            Vector3 originToPoint = point - ray.origin;
            float distanceSq = Vector3.Cross(ray.direction, originToPoint).sqrMagnitude;

            if (distanceSq < minDistanceSq)
            {
                minDistanceSq = distanceSq;
                closestIndex = i;
            }
        }

        return closestIndex != -1 && minDistanceSq < maxDistanceSq;
    }

    public List<int> FindNeighbors(int pointIndex, float searchRadius)
    {
        if (voxelGrid == null) return new List<int>();
        return voxelGrid.FindNeighbors(pointIndex, searchRadius);
    }

    public IEnumerator FilterNoiseCoroutine(float searchRadius, int threshold, Action<PCV_Data> onComplete)
    {
        if (data == null || data.PointCount == 0 || voxelGrid == null)
        {
            onComplete?.Invoke(new PCV_Data(new List<Vector3>(), new List<Color>()));
            yield break;
        }

        var filteredVertices = new List<Vector3>();
        var filteredColors = new List<Color>();
        int pointsPerFrame = 3000;

        int totalPoints = data.PointCount;

        for (int i = 0; i < totalPoints; i++)
        {
            List<int> neighbors = voxelGrid.FindNeighbors(i, searchRadius);

            if (neighbors.Count >= threshold)
            {
                filteredVertices.Add(data.Vertices[i]);
                filteredColors.Add(data.Colors[i]);
            }

            if (i > 0 && (i + 1) % pointsPerFrame == 0)
            {
                float progress = (float)(i + 1) / totalPoints;
                int percent = Mathf.FloorToInt(progress * 100);

                UnityEngine.Debug.Log($"ノイズ除去処理中: {percent}% 完了 ({i + 1}/{totalPoints} 点処理済み)");

                yield return null;
            }
        }

        UnityEngine.Debug.Log($"ノイズ除去処理: 100% 完了 ({totalPoints}/{totalPoints} 点処理済み)");

        onComplete?.Invoke(new PCV_Data(filteredVertices, filteredColors));
    }
}