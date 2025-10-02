using UnityEngine;
using System.Collections.Generic;

public static class PCV_MeshGenerator
{
    public static Mesh CreatePointCloudMesh(Vector3[] vertices, Color[] colors)
    {
        if (vertices == null || vertices.Length == 0 || colors == null || colors.Length != vertices.Length)
        {
            return null;
        }

        var mesh = new Mesh();
        if (vertices.Length > 65535)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        mesh.SetVertices(vertices);
        mesh.SetColors(colors);

        int[] indices = new int[vertices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }

        mesh.SetIndices(indices, MeshTopology.Points, 0);
        mesh.RecalculateBounds();

        return mesh;
    }
}