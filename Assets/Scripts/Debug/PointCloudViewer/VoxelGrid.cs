using System.Collections.Generic;
using UnityEngine;

public class VoxelGrid
{
    private readonly Dictionary<Vector3Int, List<int>> grid;
    private readonly float voxelSize;
    private readonly Vector3[] originalPoints;

    public IReadOnlyDictionary<Vector3Int, List<int>> Grid => grid;

    public float VoxelSize => voxelSize;

    public VoxelGrid(Vector3[] points, float size)
    {
        originalPoints = points ?? new Vector3[0];
        voxelSize = size;
        grid = new Dictionary<Vector3Int, List<int>>();
        BuildCpuGrid();
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
}