using System.Collections.Generic;
using UnityEngine;

public class VoxelGrid
{
    private readonly Dictionary<Vector3Int, List<int>> grid;
    private readonly float voxelSize;
    private readonly Vector3[] originalPoints;

    public IReadOnlyDictionary<Vector3Int, List<int>> Grid => grid;

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
}