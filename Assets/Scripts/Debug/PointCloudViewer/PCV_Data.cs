using UnityEngine;
using System.Collections.Generic;

public class PCV_Data
{
    public Vector3[] Vertices { get; }
    public Color[] Colors { get; }
    public int PointCount => Vertices?.Length ?? 0;

    public PCV_Data(IReadOnlyList<Vector3> vertices, IReadOnlyList<Color> colors)
    {
        if (vertices != null && colors != null && vertices.Count == colors.Count)
        {
            this.Vertices = new Vector3[vertices.Count];
            this.Colors = new Color[colors.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                this.Vertices[i] = vertices[i];
                this.Colors[i] = colors[i];
            }
        }
        else
        {
            this.Vertices = new Vector3[0];
            this.Colors = new Color[0];
        }
    }
}