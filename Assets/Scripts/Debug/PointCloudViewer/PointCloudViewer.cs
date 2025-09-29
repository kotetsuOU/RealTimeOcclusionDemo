using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PointCloudViewer : MonoBehaviour
{
    #region Public Fields (Inspector)
    [Header("Data Files")]
    public FileSettings[] fileSettings = new FileSettings[4]
    {
        new FileSettings { useFile = true,  filePath = "Assets/HandTrakingData/PointCloudData/currentGlobalVerticesRight.txt",  color = Color.red },
        new FileSettings { useFile = false, filePath = "Assets/HandTrakingData/PointCloudData/currentGlobalVerticesLeft.txt",   color = Color.green },
        new FileSettings { useFile = false, filePath = "Assets/HandTrakingData/PointCloudData/currentGlobalVerticesBottom.txt", color = Color.blue },
        new FileSettings { useFile = false, filePath = "Assets/HandTrakingData/PointCloudData/currentGlobalVerticesTop.txt",    color = Color.yellow }
    };

    [Header("Rendering Settings")]
    public float pointSize = 0.01f;

    [Header("Outline")]
    [SerializeField] private GameObject outline;
    [SerializeField] private Color outlineColor = Color.white;

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
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Material pointCloudMaterial;
    private Mesh pointCloudMesh;

    private PCV_Data currentPointCloudData;
    private PCV_Data originalPointCloudData;
    private PCV_Processor pointCloudProcessor;

    // Inspector state tracking
    private FileSettings[] lastFileSettings;
    #endregion

    #region Unity Lifecycle
    private void Start()
    {
        if (!UnityEngine.Application.isPlaying) return;

        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        pointCloudMaterial = new Material(Shader.Find("Unlit/PointCloudViewer"));
        meshRenderer.material = pointCloudMaterial;

        InitializeOutlineMaterials();

        RebuildPointCloud();
        SaveInspectorState();
    }

    private void Update()
    {
        if (!UnityEngine.Application.isPlaying) return;

        if (pointCloudMaterial != null)
        {
            pointCloudMaterial.SetFloat("_PointSize", pointSize);
        }

        if (HasInspectorStateChanged())
        {
            RebuildPointCloud();
            SaveInspectorState();
        }

        if (Input.GetMouseButtonDown(0))
        {
            HandleInteraction();
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            StartCoroutine(FilterNoiseCoroutine());
        }
    }
    #endregion

    #region Core Logic
    public void RebuildPointCloud()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null) return;
        }
        
        PCV_Data loadedData = PCV_Loader.LoadFromFiles(fileSettings);

        ClearMesh();

        if (loadedData != null && loadedData.PointCount > 0)
        {
            currentPointCloudData = loadedData;
            originalPointCloudData = loadedData;

            pointCloudProcessor = new PCV_Processor(currentPointCloudData, voxelSize);
            pointCloudMesh = PCV_MeshGenerator.CreatePointCloudMesh(currentPointCloudData.Vertices, currentPointCloudData.Colors);
            meshFilter.mesh = pointCloudMesh;
            UnityEngine.Debug.Log($"点群が {currentPointCloudData.PointCount} 点で再構築されました。");
        }
        else
        {
            pointCloudProcessor = null;
            currentPointCloudData = null;
            originalPointCloudData = null;
            UnityEngine.Debug.LogWarning("読み込む点群データが存在しません。");
        }
    }

    public void StartNoiseFiltering()
    {
        StartCoroutine(FilterNoiseCoroutine());
    }

    private IEnumerator FilterNoiseCoroutine()
    {
        if (pointCloudProcessor == null)
        {
            UnityEngine.Debug.LogWarning("プロセッサーが初期化されていません。ノイズ除去は実行不可能です。");
            yield break;
        }

        var stopwatch = Stopwatch.StartNew();
        UnityEngine.Debug.Log($"ノイズ除去処理を開始します。(閾値: {neighborThreshold})");
        int originalPointCount = currentPointCloudData.PointCount;

        yield return pointCloudProcessor.FilterNoiseCoroutine(
            searchRadius,
            neighborThreshold,
            filteredData =>
            {
                currentPointCloudData = filteredData;
                ClearMesh();

                if (currentPointCloudData != null && currentPointCloudData.PointCount > 0)
                {
                    pointCloudProcessor = new PCV_Processor(currentPointCloudData, voxelSize);
                    pointCloudMesh = PCV_MeshGenerator.CreatePointCloudMesh(currentPointCloudData.Vertices, currentPointCloudData.Colors);
                    meshFilter.mesh = pointCloudMesh;
                    UnityEngine.Debug.Log("フィルタリングされた点群でメッシュとVoxel Gridを再構築しました。");
                }
                else
                {
                    pointCloudProcessor = null;
                    UnityEngine.Debug.LogWarning("全ての点がノイズとして除去されました。メッシュは空になります。");
                }

                stopwatch.Stop();
                long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                int filteredPointCount = (currentPointCloudData != null) ? currentPointCloudData.PointCount : 0;
                UnityEngine.Debug.Log($"ノイズ除去処理が完了しました。処理時間: {elapsedMilliseconds} ms. 元の点数: {originalPointCount}, 除去後の点数: {filteredPointCount}");
            });
    }
    #endregion

    #region Helper & Interaction Methods
    private void HandleInteraction()
    {
        if (pointCloudProcessor == null || Camera.main == null)
        {
            UnityEngine.Debug.LogWarning("プロセッサーまたはMain Cameraが利用できません。");
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        ResetHighlightColors();

        if (pointCloudProcessor.FindClosestPoint(ray, 0.1f, out int closestPointIndex))
        {
            UnityEngine.Debug.Log($"最近傍点がインデックス: {closestPointIndex} で見つかりました。");
            List<int> neighborIndices = pointCloudProcessor.FindNeighbors(closestPointIndex, searchRadius);
            UnityEngine.Debug.Log($"Voxel Gridを使用して {neighborIndices.Count} 個の近傍点が見つかりました。");

            HighlightPoints(closestPointIndex, neighborIndices);
        }
    }

    private void ResetHighlightColors()
    {
        if (pointCloudMesh != null && currentPointCloudData != null && currentPointCloudData.PointCount > 0)
        {
            pointCloudMesh.colors = currentPointCloudData.Colors;
        }
    }

    private void HighlightPoints(int centerIndex, List<int> neighborIndices)
    {
        if (pointCloudMesh == null || currentPointCloudData == null) return;
        var updatedColors = (Color[])currentPointCloudData.Colors.Clone();

        if (centerIndex >= 0 && centerIndex < updatedColors.Length)
        {
            updatedColors[centerIndex] = Color.magenta;
        }

        foreach (int index in neighborIndices)
        {
            if (index >= 0 && index < updatedColors.Length)
            {
                updatedColors[index] = neighborColor;
            }
        }
        pointCloudMesh.colors = updatedColors;
    }

    private void ClearMesh()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null) return;
        }

        if (pointCloudMesh != null)
        {
            DestroyImmediate(pointCloudMesh);
            pointCloudMesh = null;
        }
        meshFilter.mesh = null;
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
    [System.Serializable]
    public struct FileSettings
    {
        public bool useFile;
        public string filePath;
        public Color color;

        public bool IsDifferent(FileSettings other)
        {
            return useFile != other.useFile || color != other.color;
        }
    }

    private void SaveInspectorState()
    {
        lastFileSettings = new FileSettings[fileSettings.Length];
        for (int i = 0; i < fileSettings.Length; i++)
        {
            lastFileSettings[i] = fileSettings[i];
        }
    }

    private bool HasInspectorStateChanged()
    {
        if (lastFileSettings == null || lastFileSettings.Length != fileSettings.Length) return true;

        for (int i = 0; i < fileSettings.Length; i++)
        {
            if (fileSettings[i].IsDifferent(lastFileSettings[i]))
            {
                return true;
            }
        }
        return false;
    }
    #endregion
}