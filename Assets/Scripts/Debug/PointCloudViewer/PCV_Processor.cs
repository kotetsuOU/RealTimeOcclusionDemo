using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

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

        for (int i = 0; i < data.PointCount; i++)
        {
            float distanceSq = Vector3.Cross(ray.direction, data.Vertices[i] - ray.origin).sqrMagnitude;
            if (distanceSq < minDistanceSq)
            {
                minDistanceSq = distanceSq;
                closestIndex = i;
            }
        }

        return closestIndex != -1 && minDistanceSq < (maxDistance * maxDistance);
    }

    public List<int> FindNeighbors(int pointIndex, float searchRadius)
    {
        if (voxelGrid == null) return new List<int>();
        return voxelGrid.FindNeighbors(pointIndex, searchRadius);
    }

    public IEnumerator FilterNoiseCoroutine(float searchRadius, int threshold, Action<PCV_Data> onComplete)
    {
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

            if (i > 0 && i % pointsPerFrame == 0)
            {
                yield return null;
            }
        }

        onComplete?.Invoke(new PCV_Data(filteredVertices, filteredColors));
    }
}
