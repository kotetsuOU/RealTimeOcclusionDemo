using System;
using System.Collections.Generic;
using UnityEngine;

public class PCV_SpatialSearch : IDisposable
{
    private readonly PCV_Data data;
    public VoxelGrid VoxelGrid { get; private set; }
    private bool disposedValue;

    public PCV_SpatialSearch(PCV_Data pointCloudData, float voxelSize)
    {
        this.data = pointCloudData;
        if (this.data != null && this.data.PointCount > 0)
        {
            this.VoxelGrid = new VoxelGrid(this.data.Vertices, voxelSize);
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
        if (VoxelGrid == null) return new List<int>();
        return VoxelGrid.FindNeighbors(pointIndex, searchRadius);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
            }

            if (VoxelGrid != null)
            {
                VoxelGrid.ReleaseBuffers();
                VoxelGrid = null;
            }
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}