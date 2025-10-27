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
        public int padding; // 6 * sizeof(int) = 24 bytes
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PCV_Point
    {
        public Vector4 position; // 16 bytes
        public Color color;      // 16 bytes
    }

    public ComputeBuffer OriginalPointsBuffer { get; private set; }
    public ComputeBuffer VoxelDataBuffer { get; private set; }
    public ComputeBuffer VoxelPointIndicesBuffer { get; private set; }

    public ComputeBuffer VoxelHashTableBuffer { get; private set; }
    public ComputeBuffer VoxelHashChainsBuffer { get; private set; }

    public float VoxelSize => voxelSize;
    public int HashTableSize { get; private set; } = 1;


    private PCV_Point[] pointDataCache;

    public VoxelGrid(Vector3[] points, float size)
    {
        originalPoints = points ?? new Vector3[0];
        voxelSize = size;
        grid = new Dictionary<Vector3Int, List<int>>();
        BuildCpuGrid();
        BuildGpuBuffersWithHash();
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

    private void BuildGpuBuffersWithHash()
    {
        int pointCount = Mathf.Max(1, originalPoints.Length);
        OriginalPointsBuffer = new ComputeBuffer(pointCount, sizeof(float) * 8);

        if (grid.Count == 0 || originalPoints.Length == 0)
        {
            BuildEmptyGpuBuffers();
            return;
        }

        HashTableSize = GetNextPrime(grid.Count * 2);
        int[] hashTable = new int[HashTableSize];
        for (int i = 0; i < HashTableSize; i++)
        {
            hashTable[i] = -1;
        }

        var voxelDataList = new List<VoxelData>(grid.Count);
        var pointIndicesList = new List<int>(originalPoints.Length);
        var hashChains = new int[grid.Count];

        int currentOffset = 0;
        int voxelIdx = 0;

        foreach (var kvp in grid)
        {
            voxelDataList.Add(new VoxelData
            {
                index = kvp.Key,
                pointCount = kvp.Value.Count,
                dataOffset = currentOffset,
                padding = 0
            });

            pointIndicesList.AddRange(kvp.Value);
            currentOffset += kvp.Value.Count;

            uint hash = HashVoxelIndex(kvp.Key, (uint)HashTableSize);
            hashChains[voxelIdx] = hashTable[hash];
            hashTable[hash] = voxelIdx;

            voxelIdx++;
        }

        VoxelDataBuffer = new ComputeBuffer(voxelDataList.Count, sizeof(int) * 6, ComputeBufferType.Structured);
        VoxelDataBuffer.SetData(voxelDataList);

        VoxelPointIndicesBuffer = new ComputeBuffer(pointIndicesList.Count, sizeof(int));
        VoxelPointIndicesBuffer.SetData(pointIndicesList);

        VoxelHashTableBuffer = new ComputeBuffer(HashTableSize, sizeof(int));
        VoxelHashTableBuffer.SetData(hashTable);

        VoxelHashChainsBuffer = new ComputeBuffer(hashChains.Length, sizeof(int));
        VoxelHashChainsBuffer.SetData(hashChains);
    }

    private void BuildEmptyGpuBuffers()
    {
        VoxelDataBuffer = new ComputeBuffer(1, sizeof(int) * 6, ComputeBufferType.Structured);
        VoxelDataBuffer.SetData(new VoxelData[] { new VoxelData() });

        VoxelPointIndicesBuffer = new ComputeBuffer(1, sizeof(int));
        VoxelPointIndicesBuffer.SetData(new int[] { 0 });

        HashTableSize = GetNextPrime(1 * 2);
        VoxelHashTableBuffer = new ComputeBuffer(HashTableSize, sizeof(int));
        VoxelHashTableBuffer.SetData(new int[HashTableSize]);

        VoxelHashChainsBuffer = new ComputeBuffer(1, sizeof(int));
        VoxelHashChainsBuffer.SetData(new int[] { -1 });
    }

    public bool IsGpuDataReady()
    {
        return OriginalPointsBuffer != null && VoxelDataBuffer != null &&
               VoxelPointIndicesBuffer != null && VoxelHashTableBuffer != null &&
               VoxelHashChainsBuffer != null;
    }


    public void SetPointDataCache(PCV_Data data)
    {
        if (data == null || data.PointCount == 0)
        {
            if (OriginalPointsBuffer == null || !OriginalPointsBuffer.IsValid() || OriginalPointsBuffer.count != 1)
            {
            }
            return;
        }

        if (pointDataCache == null || pointDataCache.Length != data.PointCount)
        {
            pointDataCache = new PCV_Point[data.PointCount];
        }

        for (int i = 0; i < data.PointCount; i++)
        {
            pointDataCache[i].position = data.Vertices[i]; // Vector3 -> Vector4
            pointDataCache[i].color = data.Colors[i];
        }

        if (OriginalPointsBuffer == null || !OriginalPointsBuffer.IsValid())
        {
            UnityEngine.Debug.LogError("OriginalPointsBuffer is null or invalid in SetPointDataCache!");
            return;
        }

        if (OriginalPointsBuffer.count != data.PointCount)
        {
            UnityEngine.Debug.LogWarning($"Buffer count mismatch. Recreating OriginalPointsBuffer. Buffer: {OriginalPointsBuffer.count}, Data: {data.PointCount}.");
            OriginalPointsBuffer.Release();
            OriginalPointsBuffer = new ComputeBuffer(data.PointCount, sizeof(float) * 8);
        }

        OriginalPointsBuffer.SetData(pointDataCache);
    }

    public void ReleaseBuffers()
    {
        OriginalPointsBuffer?.Release();
        OriginalPointsBuffer = null;

        VoxelDataBuffer?.Release();
        VoxelDataBuffer = null;

        VoxelPointIndicesBuffer?.Release();
        VoxelPointIndicesBuffer = null;

        VoxelHashTableBuffer?.Release();
        VoxelHashTableBuffer = null;

        VoxelHashChainsBuffer?.Release();
        VoxelHashChainsBuffer = null;

        pointDataCache = null;
    }

    private static uint HashVoxelIndex(Vector3Int voxelIndex, uint hashTableSize)
    {
        const uint p1 = 73856093;
        const uint p2 = 19349663;
        const uint p3 = 83492791;
        uint hash = ((uint)voxelIndex.x * p1) ^ ((uint)voxelIndex.y * p2) ^ ((uint)voxelIndex.z * p3);
        return hash % hashTableSize;
    }

    private static int GetNextPrime(int min)
    {
        for (int i = min | 1; i < int.MaxValue; i += 2)
        {
            if (IsPrime(i)) return i;
        }
        return min;
    }

    private static bool IsPrime(int n)
    {
        if (n < 2) return false;
        if (n == 2 || n == 3) return true;
        if (n % 2 == 0 || n % 3 == 0) return false;
        for (int i = 5; i * i <= n; i += 6)
        {
            if (n % i == 0 || n % (i + 2) == 0) return false;
        }
        return true;
    }
}