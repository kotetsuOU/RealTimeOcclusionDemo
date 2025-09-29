using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics; // Stopwatchを使用するために追加

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PointCloudViewer : MonoBehaviour
{
    #region Public Fields (Inspector)
    [Header("Data Files")]
    public string filePath1 = "Assets/HandTrakingData/PointCloudData/currentGlobalVerticesRight.txt";
    public string filePath2 = "Assets/HandTrakingData/PointCloudData/currentGlobalVerticesLeft.txt";
    public string filePath3 = "Assets/HandTrakingData/PointCloudData/currentGlobalVerticesBottom.txt";
    public string filePath4 = "Assets/HandTrakingData/PointCloudData/currentGlobalVerticesTop.txt";

    [Header("Rendering Settings")]
    public float pointSize = 0.01f;

    [Header("Per-File Toggles")]
    [SerializeField] public bool useFile1 = true;
    [SerializeField] public Color color1 = Color.red;
    [SerializeField] public bool useFile2 = false;
    [SerializeField] public Color color2 = Color.green;
    [SerializeField] public bool useFile3 = false;
    [SerializeField] public Color color3 = Color.blue;
    [SerializeField] public bool useFile4 = false;
    [SerializeField] public Color color4 = Color.yellow;

    [Header("Outline")]
    [SerializeField] private GameObject outline;
    [SerializeField] private Color outlineColor;

    [Header("Neighbor Search & Filtering")]
    [Tooltip("空間分割グリッドの各セルのサイズ")]
    [SerializeField] public float voxelSize = 0.05f;
    [Tooltip("点の周囲で近傍点を探索する半径")]
    [SerializeField] public float searchRadius = 0.1f;
    [Tooltip("近傍点をハイライトする色")]
    [SerializeField] public Color neighborColor = Color.cyan;
    [Tooltip("ノイズと判断する近傍点の閾値")]
    [SerializeField] public int neighborThreshold = 100;
    #endregion

    #region Private State
    private Mesh pointCloudMesh;
    private Material pointCloudMaterial;
    private VoxelGrid voxelGrid;

    private Vector3[] currentVertices;
    private Color[] originalColors;

    private bool lastUseFile1, lastUseFile2, lastUseFile3, lastUseFile4;
    private Color lastColor1, lastColor2, lastColor3, lastColor4;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    #endregion

    #region Unity Lifecycle
    private void Start()
    {
        if (!UnityEngine.Application.isPlaying) return;

        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        pointCloudMaterial = new Material(Shader.Find("Unlit/PointCloudViewer"));

        InitializeOutlineMaterials();
        RebuildMesh();
        SaveInspectorState();
    }

    private void Update()
    {
        if (!UnityEngine.Application.isPlaying) return;

        if (HasInspectorStateChanged())
        {
            RebuildMesh();
            SaveInspectorState();
        }

        if (Input.GetMouseButtonDown(0))
        {
            FindAndHighlightNeighbors();
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            FilterNoiseAndRebuildMesh();
        }
    }
    #endregion

    #region Core Logic
    public void RebuildMesh()
    {
        var allPoints = new List<Vector3>();
        var allColors = new List<Color>();

        if (useFile1) AddPointsWithColor(filePath1, color1, allPoints, allColors);
        if (useFile2) AddPointsWithColor(filePath2, color2, allPoints, allColors);
        if (useFile3) AddPointsWithColor(filePath3, color3, allPoints, allColors);
        if (useFile4) AddPointsWithColor(filePath4, color4, allPoints, allColors);

        if (pointCloudMesh != null)
        {
            DestroyImmediate(pointCloudMesh);
        }

        currentVertices = allPoints.ToArray();
        originalColors = allColors.ToArray();

        pointCloudMesh = PCV_MeshGenerator.CreatePointCloudMesh(currentVertices, originalColors);
        meshFilter.mesh = pointCloudMesh;

        if (pointCloudMesh != null)
        {
            meshRenderer.material = pointCloudMaterial;

            voxelGrid = new VoxelGrid(currentVertices, voxelSize);
            UnityEngine.Debug.Log($"Voxel Gridが {currentVertices.Length} 点で構築されました。");
        }
        else
        {
            voxelGrid = null;
        }
    }

    public void FilterNoiseAndRebuildMesh()
    {
        StartCoroutine(FilterNoiseCoroutine());
    }

    private IEnumerator FilterNoiseCoroutine()
    {
        if (voxelGrid == null || currentVertices == null || originalColors == null || currentVertices.Length == 0)
        {
            UnityEngine.Debug.LogWarning("VoxelGridまたは点群データが初期化されていません。ノイズ除去は実行不可能です。");
            yield break;
        }

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        UnityEngine.Debug.Log($"ノイズ除去処理を開始します。(閾値: {neighborThreshold})");

        var filteredVertices = new List<Vector3>();
        var filteredColors = new List<Color>();
        int pointsPerFrame = 5000;

        for (int i = 0; i < currentVertices.Length; i++)
        {
            List<int> neighbors = voxelGrid.FindNeighbors(i, searchRadius);
            if (neighbors.Count >= neighborThreshold)
            {
                filteredVertices.Add(currentVertices[i]);
                filteredColors.Add(originalColors[i]);
            }

            if (i > 0 && i % pointsPerFrame == 0)
            {
                yield return null;
            }
        }

        yield return null;

        stopwatch.Stop();
        long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        int originalPointCount = currentVertices.Length;
        int filteredPointCount = filteredVertices.Count;
        UnityEngine.Debug.Log($"ノイズ除去処理が完了しました。処理時間: {elapsedMilliseconds} ms. 元の点数: {originalPointCount}, 除去後の点数: {filteredPointCount}");

        if (pointCloudMesh != null)
        {
            DestroyImmediate(pointCloudMesh);
        }

        currentVertices = filteredVertices.ToArray();
        originalColors = filteredColors.ToArray();

        pointCloudMesh = PCV_MeshGenerator.CreatePointCloudMesh(currentVertices, originalColors);
        meshFilter.mesh = pointCloudMesh;

        if (pointCloudMesh != null)
        {
            voxelGrid = new VoxelGrid(currentVertices, voxelSize);
            UnityEngine.Debug.Log("フィルタリングされた点群でメッシュとVoxel Gridを再構築しました。");
        }
        else
        {
            voxelGrid = null;
            UnityEngine.Debug.LogWarning("全ての点がノイズとして除去されました。メッシュは空になります。");
        }
    }
    #endregion

    #region Helper & Interaction Methods
    private void AddPointsWithColor(string path, Color color, List<Vector3> positions, List<Color> colors)
    {
        List<Vector3> loadedVerts = PCV_Loader.LoadVerticesFromFile(path);
        positions.AddRange(loadedVerts);
        for (int i = 0; i < loadedVerts.Count; i++)
        {
            colors.Add(color);
        }
    }

    private void FindAndHighlightNeighbors()
    {
        if (pointCloudMesh == null || voxelGrid == null || Camera.main == null)
        {
            UnityEngine.Debug.LogWarning("Mesh, VoxelGrid, または Main Cameraが利用できません。");
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        float minDistanceSq = float.MaxValue;
        int closestPointIndex = -1;

        for (int i = 0; i < currentVertices.Length; i++)
        {
            float distanceSq = Vector3.Cross(ray.direction, currentVertices[i] - ray.origin).sqrMagnitude;
            if (distanceSq < minDistanceSq)
            {
                minDistanceSq = distanceSq;
                closestPointIndex = i;
            }
        }

        if (closestPointIndex != -1 && minDistanceSq < 0.01f)
        {
            UnityEngine.Debug.Log($"最近傍点がインデックス: {closestPointIndex} で見つかりました。");

            List<int> neighborIndices = voxelGrid.FindNeighbors(closestPointIndex, searchRadius);
            UnityEngine.Debug.Log($"Voxel Gridを使用して {neighborIndices.Count} 個の近傍点が見つかりました。");

            var updatedColors = (Color[])originalColors.Clone();
            updatedColors[closestPointIndex] = Color.magenta;

            foreach (int index in neighborIndices)
            {
                updatedColors[index] = neighborColor;
            }
            pointCloudMesh.colors = updatedColors;
        }
    }

    private void InitializeOutlineMaterials()
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
    #endregion

    #region Inspector State Tracking
    private void SaveInspectorState()
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

    private bool HasInspectorStateChanged()
    {
        return useFile1 != lastUseFile1 || useFile2 != lastUseFile2 || useFile3 != lastUseFile3 || useFile4 != lastUseFile4 ||
               color1 != lastColor1 || color2 != lastColor2 || color3 != lastColor3 || color4 != lastColor4;
    }
    #endregion
}