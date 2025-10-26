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
        originalPoints = points ?? new Vector3[0];
        voxelSize = size;
        grid = new Dictionary<Vector3Int, List<int>>();
        Build();
    }

    private void Build()
    {
        if (originalPoints.Length == 0)
        {
            BuildEmptyGpuBuffers();
            return;
        }

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
        if (pointIndex < 0 || pointIndex >= originalPoints.Length) return neighbors; // Bounds check

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
        OriginalPointsBuffer = new ComputeBuffer(originalPoints.Length, sizeof(float) * 8); // PCV_Point (32 bytes)

        if (grid.Count == 0)
        {
            VoxelDataBuffer = new ComputeBuffer(1, sizeof(int) * 6, ComputeBufferType.Structured); // Minimum size 1
            VoxelDataBuffer.SetData(new VoxelData[] { new VoxelData() }); // Set dummy data

            VoxelPointIndicesBuffer = new ComputeBuffer(1, sizeof(int)); // Minimum size 1
            VoxelPointIndicesBuffer.SetData(new int[] { 0 }); // Set dummy data
            return;
        }

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

        VoxelDataBuffer = new ComputeBuffer(voxelDataList.Length, sizeof(int) * 6, ComputeBufferType.Structured);
        VoxelDataBuffer.SetData(voxelDataList);

        if (pointIndicesList.Count > 0)
        {
            VoxelPointIndicesBuffer = new ComputeBuffer(pointIndicesList.Count, sizeof(int));
            VoxelPointIndicesBuffer.SetData(pointIndicesList);
        }
        else
        {
            VoxelPointIndicesBuffer = new ComputeBuffer(1, sizeof(int));
            VoxelPointIndicesBuffer.SetData(new int[] { 0 });
        }
    }

    private void BuildEmptyGpuBuffers()
    {
        OriginalPointsBuffer = new ComputeBuffer(1, sizeof(float) * 8);
        OriginalPointsBuffer.SetData(new PCV_Point[] { new PCV_Point() });

        VoxelDataBuffer = new ComputeBuffer(1, sizeof(int) * 6, ComputeBufferType.Structured);
        VoxelDataBuffer.SetData(new VoxelData[] { new VoxelData() });

        VoxelPointIndicesBuffer = new ComputeBuffer(1, sizeof(int));
        VoxelPointIndicesBuffer.SetData(new int[] { 0 });
    }


    public void SetPointDataCache(PCV_Data data)
    {
        if (data == null || data.PointCount == 0)
        {
            if (OriginalPointsBuffer == null || !OriginalPointsBuffer.IsValid() || OriginalPointsBuffer.count != 1)
            {
                UnityEngine.Debug.LogWarning("SetPointDataCache called with empty data, but buffer state is unexpected.");
            }
            return;
        }

        if (pointDataCache == null || pointDataCache.Length != data.PointCount)
        {
            pointDataCache = new PCV_Point[data.PointCount];
        }

        for (int i = 0; i < data.PointCount; i++)
        {
            pointDataCache[i].position = data.Vertices[i];
            pointDataCache[i].padding1 = 0f; // Explicitly set padding
            pointDataCache[i].color = data.Colors[i];
        }

        if (OriginalPointsBuffer != null && OriginalPointsBuffer.IsValid())
        {
            if (OriginalPointsBuffer.count != data.PointCount)
            {
                UnityEngine.Debug.LogError($"Buffer count mismatch in SetPointDataCache! Buffer: {OriginalPointsBuffer.count}, Data: {data.PointCount}. Releasing and skipping SetData.");
                return;
            }
            OriginalPointsBuffer.SetData(pointDataCache);
        }
        else
        {
            UnityEngine.Debug.LogError("OriginalPointsBuffer is null or invalid in SetPointDataCache!");
        }
    }

    public void ReleaseBuffers()
    {
        if (OriginalPointsBuffer != null && OriginalPointsBuffer.IsValid())
        {
            OriginalPointsBuffer.Release();
        }
        OriginalPointsBuffer = null;

        if (VoxelDataBuffer != null && VoxelDataBuffer.IsValid())
        {
            VoxelDataBuffer.Release();
        }
        VoxelDataBuffer = null;

        if (VoxelPointIndicesBuffer != null && VoxelPointIndicesBuffer.IsValid())
        {
            VoxelPointIndicesBuffer.Release();
        }
        VoxelPointIndicesBuffer = null;

        pointDataCache = null;
    }
}

