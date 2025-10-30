using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;

public class VoxelGrid
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Point
    {
        public Vector4 position;
        public Color color;
    }

    private readonly Dictionary<Vector3Int, List<int>> grid;
    private readonly float voxelSize;
    private readonly Vector3[] originalPoints;

    public IReadOnlyDictionary<Vector3Int, List<int>> Grid => grid;

    public ComputeBuffer OriginalPointsBuffer { get; private set; }

    public float VoxelSize => voxelSize;

    private Point[] pointDataCache;
    private const int POINT_SIZE = 32;

    public VoxelGrid(Vector3[] points, float size)
    {
        originalPoints = points ?? new Vector3[0];
        voxelSize = size;
        grid = new Dictionary<Vector3Int, List<int>>();
        BuildCpuGrid();

        int pointCount = Mathf.Max(1, originalPoints.Length);
        OriginalPointsBuffer = new ComputeBuffer(pointCount, POINT_SIZE);

        if (originalPoints.Length > 0)
        {
            SetPointDataCacheFromVertices();
        }
    }

    private void BuildCpuGrid()
    {
        if (originalPoints.Length == 0) return;

        for (int i = 0; i < originalPoints.Length; i++)
        {
            Vector3 point = originalPoints[i];
            Vector3Int voxelIndex = GetVoxelIndex(point);

            if (!grid.ContainsKey(voxelIndex))
            {
                grid[voxelIndex] = new List<int>();
            }
            grid[voxelIndex].Add(i);
        }
    }

    private Vector3Int GetVoxelIndex(Vector3 point)
    {
        if (voxelSize <= 0) return Vector3Int.zero;
        return new Vector3Int(
            Mathf.FloorToInt(point.x / voxelSize),
            Mathf.FloorToInt(point.y / voxelSize),
            Mathf.FloorToInt(point.z / voxelSize)
        );
    }

    public List<int> FindNeighbors(int pointIndex, float searchRadius)
    {
        List<int> neighbors = new List<int>();
        if (pointIndex < 0 || pointIndex >= originalPoints.Length) return neighbors;

        Vector3 searchPoint = originalPoints[pointIndex];
        Vector3Int centerVoxelIndex = GetVoxelIndex(searchPoint);
        float searchRadiusSq = searchRadius * searchRadius;

        int searchRange = Mathf.Max(1, Mathf.CeilToInt(searchRadius / voxelSize));

        for (int x = -searchRange; x <= searchRange; x++)
        {
            for (int y = -searchRange; y <= searchRange; y++)
            {
                for (int z = -searchRange; z <= searchRange; z++)
                {
                    Vector3Int neighborVoxelIndex = centerVoxelIndex + new Vector3Int(x, y, z);
                    if (grid.TryGetValue(neighborVoxelIndex, out List<int> pointsInVoxel))
                    {
                        foreach (int candidateIndex in pointsInVoxel)
                        {
                            if (candidateIndex == pointIndex) continue;

                            if ((originalPoints[candidateIndex] - searchPoint).sqrMagnitude <= searchRadiusSq)
                            {
                                neighbors.Add(candidateIndex);
                            }
                        }
                    }
                }
            }
        }
        return neighbors;
    }

    public bool IsGpuDataReady()
    {
        return OriginalPointsBuffer != null && OriginalPointsBuffer.IsValid();
    }

    private void SetPointDataCacheFromVertices()
    {
        if (originalPoints == null || originalPoints.Length == 0) return;

        if (pointDataCache == null || pointDataCache.Length != originalPoints.Length)
        {
            pointDataCache = new Point[originalPoints.Length];
        }

        for (int i = 0; i < originalPoints.Length; i++)
        {
            pointDataCache[i].position = new Vector4(
                originalPoints[i].x,
                originalPoints[i].y,
                originalPoints[i].z,
                0f
            );
            pointDataCache[i].color = Color.white;
        }
        OriginalPointsBuffer.SetData(pointDataCache);
    }

    public void SetPointDataCache(PCV_Data data)
    {
        if (data == null || data.PointCount == 0)
        {
            UnityEngine.Debug.LogWarning("SetPointDataCache: ƒf[ƒ^‚ª‹ó‚Å‚·");
            return;
        }

        if (pointDataCache == null || pointDataCache.Length != data.PointCount)
        {
            pointDataCache = new Point[data.PointCount];
        }

        for (int i = 0; i < data.PointCount; i++)
        {
            pointDataCache[i].position = new Vector4(
                data.Vertices[i].x,
                data.Vertices[i].y,
                data.Vertices[i].z,
                0f
            );
            pointDataCache[i].color = data.Colors[i];
        }

        if (OriginalPointsBuffer == null || !OriginalPointsBuffer.IsValid())
        {
            UnityEngine.Debug.LogError("OriginalPointsBuffer is null or invalid in SetPointDataCache!");
            int pointCount = Mathf.Max(1, data.PointCount);
            OriginalPointsBuffer = new ComputeBuffer(pointCount, POINT_SIZE);
        }

        if (OriginalPointsBuffer.count != data.PointCount)
        {
            UnityEngine.Debug.LogWarning($"Buffer count mismatch. Recreating OriginalPointsBuffer. Buffer: {OriginalPointsBuffer.count}, Data: {data.PointCount}.");
            OriginalPointsBuffer.Release();
            OriginalPointsBuffer = new ComputeBuffer(data.PointCount, POINT_SIZE);
        }

        OriginalPointsBuffer.SetData(pointDataCache);
    }

    public void ReleaseBuffers()
    {
        OriginalPointsBuffer?.Release();
        OriginalPointsBuffer = null;

        pointDataCache = null;
    }
}