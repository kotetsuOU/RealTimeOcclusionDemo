using System.Collections.Generic;
using System.IO;
using UnityEngine;
//using static System.Net.Mime.MediaTypeNames;

public class PointCloudViewer : MonoBehaviour
{
    public string filePath1 = "Assets/HandTrakingSampleData/currentGlobalVerticesRight.txt";
    public string filePath2 = "Assets/HandTrakingSampleData/currentGlobalVerticesLeft.txt";
    public string filePath3 = "Assets/HandTrakingSampleData/currentGlobalVerticesBottom.txt";
    public string filePath4 = "Assets/HandTrakingSampleData/currentGlobalVerticesTop.txt";
    public float pointSize = 0.01f;

    [Header("Use and Color per File")]
    [SerializeField] public bool useFile1 = true;
    [SerializeField] public Color color1 = Color.red;

    [SerializeField] public bool useFile2 = false;
    [SerializeField] public Color color2 = Color.green;

    [SerializeField] public bool useFile3 = false;
    [SerializeField] public Color color3 = Color.blue;

    [SerializeField] public bool useFile4 = false;
    [SerializeField] public Color color4 = Color.yellow;

    [SerializeField] GameObject outline;
    [SerializeField] Color outlineColor;

    private Mesh mesh;

    private bool lastUseFile1, lastUseFile2, lastUseFile3, lastUseFile4;
    private Color lastColor1, lastColor2, lastColor3, lastColor4;

    void Start()
    {
        if (UnityEngine.Application.isPlaying)
        {
            InitMaterials();
            RebuildMesh();
            SaveState();
        }
    }

    void Update()
    {
        if (!UnityEngine.Application.isPlaying) return;

        if (HasChanged())
        {
            RebuildMesh();
            SaveState();
        }
    }

    private void SaveState()
    {
        lastUseFile1 = useFile1;
        lastUseFile2 = useFile2;
        lastUseFile3 = useFile3;
        lastUseFile4 = useFile4;

        lastColor1 = color1;
        lastColor2 = color2;
        lastColor3 = color3;
        lastColor4 = color4;
    }

    private bool HasChanged()
    {
        return useFile1 != lastUseFile1 || useFile2 != lastUseFile2 || useFile3 != lastUseFile3 || useFile4 != lastUseFile4 ||
               color1 != lastColor1 || color2 != lastColor2 || color3 != lastColor3 || color4 != lastColor4;
    }

    public void RebuildMesh()
    {
        List<Vector3> allPoints = new List<Vector3>();
        List<Color> allColors = new List<Color>();

        if (useFile1) AddPointsWithColor(filePath1, color1, allPoints, allColors);
        if (useFile2) AddPointsWithColor(filePath2, color2, allPoints, allColors);
        if (useFile3) AddPointsWithColor(filePath3, color3, allPoints, allColors);
        if (useFile4) AddPointsWithColor(filePath4, color4, allPoints, allColors);

        if (mesh != null)
            DestroyImmediate(mesh);

        if (allPoints.Count > 0)
        {
            mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.vertices = allPoints.ToArray();
            mesh.colors = allColors.ToArray();

            int[] indices = new int[allPoints.Count];
            for (int i = 0; i < indices.Length; i++)
                indices[i] = i;

            mesh.SetIndices(indices, MeshTopology.Points, 0);
            mesh.RecalculateBounds();

            GetComponent<MeshFilter>().mesh = mesh;

            var renderer = GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Unlit/PointCloudViewer"));
            renderer.material = mat;
        }
        else
        {
            GetComponent<MeshFilter>().mesh = null;
        }
    }

    void InitMaterials()
    {
        if (outline != null)
        {
            var renderers = outline.GetComponentsInChildren<MeshRenderer>();
            foreach (var rend in renderers)
            {
                var mat = new Material(Shader.Find("Unlit/PointCloudViewerOutline"));
                mat.color = outlineColor;
                rend.material = mat;
            }
        }
    }

    void AddPointsWithColor(string path, Color color, List<Vector3> positions, List<Color> colors)
    {
        var verts = LoadVerticesFromFile(path);
        positions.AddRange(verts);
        for (int i = 0; i < verts.Length; i++)
            colors.Add(color);
    }

    Vector3[] LoadVerticesFromFile(string path)
    {
        if (!File.Exists(path))
        {
            UnityEngine.Debug.LogError($"File not found: {path}");
            return new Vector3[0];
        }

        List<Vector3> vertices = new List<Vector3>();
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split(',');
            if (parts.Length == 3 &&
                float.TryParse(parts[0], out float x) &&
                float.TryParse(parts[1], out float y) &&
                float.TryParse(parts[2], out float z))
            {
                vertices.Add(new Vector3(x, y, z));
            }
        }

        return vertices.ToArray();
    }
}