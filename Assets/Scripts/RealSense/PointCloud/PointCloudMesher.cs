using System.Linq;
using UnityEngine;

public class PointCloudMesher
{
    private readonly MeshFilter _meshFilter;
    private readonly MeshRenderer _meshRenderer;
    private Mesh _mesh;

    public PointCloudMesher(MeshFilter meshFilter, MeshRenderer meshRenderer)
    {
        _meshFilter = meshFilter;
        _meshRenderer = meshRenderer;
    }

    public void ResetMesh(int width, int height, Texture2D uvmap)
    {
        _meshRenderer.sharedMaterial.SetTexture("_UVMap", uvmap);

        int pointCount = width * height;
        var indices = Enumerable.Range(0, pointCount).ToArray();
        var uvs = new Vector2[pointCount];
        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width; i++)
                uvs[i + j * width] = new Vector2(i / (float)width, j / (float)height);
        }

        _mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        _mesh.MarkDynamic();
        _mesh.vertices = new Vector3[pointCount];
        _mesh.uv = uvs;
        _mesh.SetIndices(indices, MeshTopology.Points, 0);
        _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10f);

        _meshFilter.sharedMesh = _mesh;
    }

    public void UpdateMesh(Vector3[] vertices, int validPointsCount, Color pointColor)
    {
        if (_mesh == null) return;

        var colors = new Color[vertices.Length];
        for (int i = 0; i < validPointsCount; i++)
        {
            colors[i] = pointColor;
        }

        _mesh.vertices = vertices;
        _mesh.colors = colors;
        _mesh.UploadMeshData(false);
    }
}