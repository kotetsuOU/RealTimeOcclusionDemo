using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

[RequireComponent(typeof(PCV_Settings), typeof(PCV_Renderer), typeof(PCV_InputHandler))]
public class PointCloudViewer : MonoBehaviour
{
    #region Component References
    [Header("Component References")]
    [SerializeField] private PCV_Settings settings;
    [SerializeField] private PCV_Renderer pointCloudRenderer;
    #endregion

    #region Private State
    private PCV_Data currentPointCloudData;
    private PCV_SpatialSearch spatialSearch;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        settings = GetComponent<PCV_Settings>();
        pointCloudRenderer = GetComponent<PCV_Renderer>();
        if (settings != null) settings.hideFlags = HideFlags.HideInInspector;
        if (pointCloudRenderer != null) pointCloudRenderer.hideFlags = HideFlags.HideInInspector;
    }

    private void Start()
    {
        if (!UnityEngine.Application.isPlaying) return;
        pointCloudRenderer.InitializeOutline(settings.outline, settings.outlineColor);
        pointCloudRenderer.UpdatePointSize(settings.pointSize);
        RebuildPointCloud();
    }

    private void Update()
    {
        if (!UnityEngine.Application.isPlaying) return;

        if (settings.HasFileSettingsChanged() || settings.HasProcessingSettingsChanged())
        {
            RebuildPointCloud();
            settings.SaveInspectorState();
        }
        else if (settings.HasRenderingSettingsChanged())
        {
            pointCloudRenderer.UpdatePointSize(settings.pointSize);
            pointCloudRenderer.InitializeOutline(settings.outline, settings.outlineColor);
            settings.SaveInspectorState();
        }
    }
    #endregion

    #region Core Logic
    public void RebuildPointCloud()
    {
        if (settings == null) settings = GetComponent<PCV_Settings>();
        if (pointCloudRenderer == null) pointCloudRenderer = GetComponent<PCV_Renderer>();

        if (settings == null || pointCloudRenderer == null)
        {
            UnityEngine.Debug.LogError("必要なコンポーネント (PCV_Settings or PCV_Renderer) が見つかりません。");
            return;
        }

        PCV_Data loadedData = PCV_Loader.LoadFromFiles(settings.fileSettings);
        UpdatePointCloudData(loadedData);

        if (loadedData != null && loadedData.PointCount > 0)
        {
            UnityEngine.Debug.Log($"点群が {loadedData.PointCount} 点で再構築されました。");
        }
        else
        {
            UnityEngine.Debug.LogWarning("読み込む点群データが存在しません。");
        }
    }

    private void UpdatePointCloudData(PCV_Data data)
    {
        currentPointCloudData = data;
        if (data != null && data.PointCount > 0)
        {
            spatialSearch = new PCV_SpatialSearch(data, settings.voxelSize);
            pointCloudRenderer.UpdateMesh(data);
        }
        else
        {
            spatialSearch = null;
            pointCloudRenderer.ClearMesh();
        }
    }
    #endregion

    #region Filtering Methods
    public void StartNoiseFiltering()
    {
        if (currentPointCloudData == null || spatialSearch == null)
        {
            UnityEngine.Debug.LogWarning("点群データがロードされていません。処理は実行不可能です。");
            return;
        }

        if (settings.pointCloudFilterShader != null)
        {
            ExecuteNoiseFilteringGPU();
        }
        else
        {
            UnityEngine.Debug.LogWarning("近傍探索ノイズフィルターCompute Shaderが設定されていません。CPUで処理を実行します。");
            if (UnityEngine.Application.isPlaying)
            {
                StartCoroutine(ExecuteNoiseFilteringCPUCoroutine());
            }
            else
            {
                ExecuteNoiseFilteringCPU();
            }
        }
    }

    public void StartMorpologyOperation()
    {
        if (currentPointCloudData == null)
        {
            UnityEngine.Debug.LogWarning("点群データがロードされていません。処理は実行不可能です。");
            return;
        }
        if (settings.morpologyOperationShader == null)
        {
            UnityEngine.Debug.LogWarning("モルフォロジー演算Compute Shaderが設定されていません。");
            return;
        }
        ExecuteMorpologyOperationGPU();
    }

    private void ExecuteNoiseFilteringCPU()
    {
        var stopwatch = Stopwatch.StartNew();
        int originalCount = currentPointCloudData.PointCount;
        UnityEngine.Debug.Log($"CPUによるノイズ除去処理を開始します。(閾値: {settings.neighborThreshold})");

        PCV_Data filteredData = PCV_NoiseFilter.FilterCPU(currentPointCloudData, spatialSearch.VoxelGrid, settings.searchRadius, settings.neighborThreshold);

        stopwatch.Stop();
        LogFilteringResult("ノイズ除去", originalCount, filteredData.PointCount, stopwatch.ElapsedMilliseconds);
        UpdatePointCloudData(filteredData);
    }

    private IEnumerator ExecuteNoiseFilteringCPUCoroutine()
    {
        var stopwatch = Stopwatch.StartNew();
        int originalCount = currentPointCloudData.PointCount;
        UnityEngine.Debug.Log($"CPUによるノイズ除去処理(コルーチン)を開始します。(閾値: {settings.neighborThreshold})");

        PCV_Data result = null;
        yield return PCV_NoiseFilter.FilterCPUCoroutine(currentPointCloudData, spatialSearch.VoxelGrid, settings.searchRadius, settings.neighborThreshold,
            (filteredData) => { result = filteredData; }
        );

        stopwatch.Stop();
        LogFilteringResult("ノイズ除去", originalCount, result.PointCount, stopwatch.ElapsedMilliseconds);
        UpdatePointCloudData(result);
    }

    private void ExecuteNoiseFilteringGPU()
    {
        var stopwatch = Stopwatch.StartNew();
        int originalCount = currentPointCloudData.PointCount;
        UnityEngine.Debug.Log($"GPUによる近傍探索ノイズ除去処理を開始します。(閾値: {settings.neighborThreshold})");

        PCV_Data filteredData = PCV_NoiseFilter.FilterGPU(currentPointCloudData, settings.pointCloudFilterShader, settings.searchRadius, settings.neighborThreshold);

        stopwatch.Stop();
        LogFilteringResult("近傍探索ノイズ除去", originalCount, filteredData.PointCount, stopwatch.ElapsedMilliseconds);
        UpdatePointCloudData(filteredData);
    }

    private void ExecuteMorpologyOperationGPU()
    {
        var stopwatch = Stopwatch.StartNew();
        int originalCount = currentPointCloudData.PointCount;
        UnityEngine.Debug.Log($"GPUによるモルフォロジー演算を開始します。(侵食: {settings.erosionIterations}回, 膨張: {settings.dilationIterations}回)");

        PCV_Data filteredData = PCV_MorphologyFilter.ApplyGPU(currentPointCloudData, settings.morpologyOperationShader, settings.voxelSize, settings.erosionIterations, settings.dilationIterations);

        stopwatch.Stop();
        LogFilteringResult("モルフォロジー演算", originalCount, filteredData.PointCount, stopwatch.ElapsedMilliseconds);
        UpdatePointCloudData(filteredData);
    }

    private void LogFilteringResult(string filterName, int originalCount, int filteredCount, long elapsedMilliseconds)
    {
        UnityEngine.Debug.Log($"{filterName}処理が完了しました。処理時間: {elapsedMilliseconds} ms. 元の点数: {originalCount}, 処理後の点数: {filteredCount}");
        if (filteredCount == 0)
        {
            UnityEngine.Debug.LogWarning("全ての点が除去されました。メッシュは空になります。");
        }
    }
    #endregion

    #region Interaction Methods
    public void HandleInteraction()
    {
        if (spatialSearch == null || Camera.main == null)
        {
            UnityEngine.Debug.LogWarning("空間検索モジュールまたはMain Cameraが利用できません。");
            return;
        }
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        pointCloudRenderer.ResetHighlight(currentPointCloudData);
        if (spatialSearch.FindClosestPoint(ray, 0.1f, out int closestPointIndex))
        {
            List<int> neighborIndices = spatialSearch.FindNeighbors(closestPointIndex, settings.searchRadius);
            UnityEngine.Debug.Log($"Voxel Gridを使用して {neighborIndices.Count} 個の近傍点が見つかりました。");
            pointCloudRenderer.HighlightPoints(closestPointIndex, neighborIndices, currentPointCloudData, Color.magenta, settings.neighborColor);
        }
    }
    #endregion

#if UNITY_EDITOR
    public void ExportVoxelCountsToCSV()
    {
        if (spatialSearch == null)
        {
            UnityEngine.Debug.Log("点群データがロードされていません。再構築を実行します。");
            RebuildPointCloud();
        }

        if (spatialSearch == null || spatialSearch.VoxelGrid == null)
        {
            UnityEngine.Debug.LogError("VoxelGridの初期化に失敗しました。点群をロードしてください。");
            return;
        }

        PCV_VoxelCountExporter.Export(spatialSearch.VoxelGrid);
    }
#endif
}