using UnityEngine;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct PCV_Point
{
    public float posX;
    public float posY;
    public float posZ;
    public float padding1; 
    public float colorR;
    public float colorG;
    public float colorB;
    public float colorA;

    public Vector3 position
    {
        get => new Vector3(posX, posY, posZ);
        set { posX = value.x; posY = value.y; posZ = value.z; }
    }

    public Color color
    {
        get => new Color(colorR, colorG, colorB, colorA);
        set { colorR = value.r; colorG = value.g; colorB = value.b; colorA = value.a; }
    }
}