using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class VoxelGrid
{
    private readonly Dictionary<Vector3Int, List<int>> grid;
    private readonly float voxelSize;
    private readonly Vector3[] originalPoints;

    public IReadOnlyDictionary<Vector3Int, List<int>> Grid => grid;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct VoxelData
    {
        public Vector3Int index;
        public int pointCount;
        public int dataOffset;
        public int padding;
    }

    public ComputeBuffer OriginalPointsBuffer { get; private set; }
    public ComputeBuffer VoxelDataBuffer { get; private set; }
    public ComputeBuffer VoxelPointIndicesBuffer { get; private set; }

    private PCV_Point[] pointDataCache;

    public VoxelGrid(Vector3[] points, float size)
    {
        originalPoints = points;
        voxelSize = size;
        grid = new Dictionary<Vector3Int, List<int>>();
        Build();
    }

    private void Build()
    {
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

        BuildGpuBuffers();
    }

    private Vector3Int GetVoxelIndex(Vector3 point)
    {
        return new Vector3Int(
            Mathf.FloorToInt(point.x / voxelSize),
            Mathf.FloorToInt(point.y / voxelSize),
            Mathf.FloorToInt(point.z / voxelSize)
        );
    }

    public List<int> FindNeighbors(int pointIndex, float searchRadius)
    {
        List<int> neighbors = new List<int>();
        Vector3 searchPoint = originalPoints[pointIndex];

        Vector3Int centerVoxelIndex = GetVoxelIndex(searchPoint);
        float searchRadiusSq = searchRadius * searchRadius;

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
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

    private void BuildGpuBuffers()
    {
        ReleaseBuffers();

        if (originalPoints.Length == 0) return;

        OriginalPointsBuffer = new ComputeBuffer(originalPoints.Length, sizeof(float) * 8); // PCV_Point (32 bytes)

        if (grid.Count == 0) return;

        var voxelDataList = new VoxelData[grid.Count];
        var pointIndicesList = new List<int>(originalPoints.Length);

        int i_voxel = 0;
        int currentOffset = 0;

        foreach (var kvp in grid)
        {
            voxelDataList[i_voxel] = new VoxelData
            {
                index = kvp.Key,
                pointCount = kvp.Value.Count,
                dataOffset = currentOffset,
                padding = 0
            };

            pointIndicesList.AddRange(kvp.Value);
            currentOffset += kvp.Value.Count;
            i_voxel++;
        }

        VoxelDataBuffer = new ComputeBuffer(voxelDataList.Length, sizeof(int) * 6);
        VoxelDataBuffer.SetData(voxelDataList);

        VoxelPointIndicesBuffer = new ComputeBuffer(pointIndicesList.Count, sizeof(int));
        VoxelPointIndicesBuffer.SetData(pointIndicesList);
    }

    public void SetPointDataCache(PCV_Data data)
    {
        if (pointDataCache == null || pointDataCache.Length != data.PointCount)
        {
            pointDataCache = new PCV_Point[data.PointCount];
        }

        for (int i = 0; i < data.PointCount; i++)
        {
            pointDataCache[i].position = data.Vertices[i];
            pointDataCache[i].padding1 = 0f;
            pointDataCache[i].color = data.Colors[i];
        }

        if (OriginalPointsBuffer != null && OriginalPointsBuffer.IsValid())
        {
            OriginalPointsBuffer.SetData(pointDataCache);
        }
        else
        {
            UnityEngine.Debug.LogError("  - OriginalPointsBuffer ‚ª‰Šú‰»‚³‚ê‚Ä‚¢‚Ü‚¹‚ñI");
        }
    }

    ~VoxelGrid()
    {
        ReleaseBuffers();
    }

    public void ReleaseBuffers()
    {
        if (OriginalPointsBuffer != null)
        {
            if (OriginalPointsBuffer.IsValid())
            {
                OriginalPointsBuffer.Release();
            }
            OriginalPointsBuffer = null;
        }

        if (VoxelDataBuffer != null)
        {
            if (VoxelDataBuffer.IsValid())
            {
                VoxelDataBuffer.Release();
            }
            VoxelDataBuffer = null;
        }

        if (VoxelPointIndicesBuffer != null)
        {
            if (VoxelPointIndicesBuffer.IsValid())
            {
                VoxelPointIndicesBuffer.Release();
            }
            VoxelPointIndicesBuffer = null;
        }

        pointDataCache = null;
    }
}