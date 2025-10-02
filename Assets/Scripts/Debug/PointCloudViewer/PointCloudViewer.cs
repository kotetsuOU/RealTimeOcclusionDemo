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
    private PCV_Data originalPointCloudData;
    private PCV_Processor pointCloudProcessor;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        settings = GetComponent<PCV_Settings>();
        pointCloudRenderer = GetComponent<PCV_Renderer>();
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
        PCV_Data loadedData = PCV_Loader.LoadFromFiles(settings.fileSettings);

        if (loadedData != null && loadedData.PointCount > 0)
        {
            currentPointCloudData = loadedData;
            originalPointCloudData = loadedData;
            pointCloudProcessor = new PCV_Processor(currentPointCloudData, settings.voxelSize);
            pointCloudRenderer.UpdateMesh(currentPointCloudData);
            UnityEngine.Debug.Log($"点群が {currentPointCloudData.PointCount} 点で再構築されました。");
        }
        else
        {
            pointCloudProcessor = null;
            currentPointCloudData = null;
            originalPointCloudData = null;
            pointCloudRenderer.ClearMesh();
            UnityEngine.Debug.LogWarning("読み込む点群データが存在しません。");
        }
    }

    public void StartNoiseFiltering()
    {
        if (pointCloudProcessor == null)
        {
            UnityEngine.Debug.LogWarning("プロセッサーが初期化されていません。ノイズ除去は実行不可能です。");
            return;
        }

        if (settings.pointCloudFilterShader != null)
        {
            ExecuteNoiseFilteringGPU();
        }
        else
        {
            UnityEngine.Debug.LogWarning("Compute Shaderが設定されていません。CPUで処理を実行します。");
            StartCoroutine(FilterNoiseCoroutine());
        }
    }

    private void ExecuteNoiseFilteringGPU()
    {
        UnityEngine.Debug.Log($"GPUによるノイズ除去処理を開始します。(閾値: {settings.neighborThreshold})");
        int originalPointCount = currentPointCloudData.PointCount;

        PCV_Data filteredData = pointCloudProcessor.FilterNoiseGPU(
            settings.pointCloudFilterShader,
            settings.searchRadius,
            settings.neighborThreshold,
            out long elapsedMilliseconds
        );

        UpdatePointCloudAfterFiltering(filteredData);

        int filteredPointCount = (currentPointCloudData != null) ? currentPointCloudData.PointCount : 0;
        UnityEngine.Debug.Log($"ノイズ除去処理が完了しました。処理時間: {elapsedMilliseconds} ms. 元の点数: {originalPointCount}, 除去後の点数: {filteredPointCount}");
    }

    private IEnumerator FilterNoiseCoroutine()
    {
        var stopwatch = Stopwatch.StartNew();
        UnityEngine.Debug.Log($"CPUによるノイズ除去処理を開始します。(閾値: {settings.neighborThreshold})");
        int originalPointCount = currentPointCloudData.PointCount;

        yield return pointCloudProcessor.FilterNoiseCoroutine(
            settings.searchRadius,
            settings.neighborThreshold,
            filteredData =>
            {
                UpdatePointCloudAfterFiltering(filteredData);
                stopwatch.Stop();
                long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                int filteredPointCount = (currentPointCloudData != null) ? currentPointCloudData.PointCount : 0;
                UnityEngine.Debug.Log($"ノイズ除去処理が完了しました。処理時間: {elapsedMilliseconds} ms. 元の点数: {originalPointCount}, 除去後の点数: {filteredPointCount}");
            });
    }

    private void UpdatePointCloudAfterFiltering(PCV_Data data)
    {
        currentPointCloudData = data;
        if (currentPointCloudData != null && currentPointCloudData.PointCount > 0)
        {
            pointCloudProcessor = new PCV_Processor(currentPointCloudData, settings.voxelSize);
            pointCloudRenderer.UpdateMesh(currentPointCloudData);
            UnityEngine.Debug.Log("フィルタリングされた点群でメッシュとVoxel Gridを再構築しました。");
        }
        else
        {
            pointCloudProcessor = null;
            pointCloudRenderer.ClearMesh();
            UnityEngine.Debug.LogWarning("全ての点がノイズとして除去されました。メッシュは空になります。");
        }
    }
    #endregion

    #region Interaction Methods
    public void HandleInteraction()
    {
        if (pointCloudProcessor == null || Camera.main == null)
        {
            UnityEngine.Debug.LogWarning("プロセッサーまたはMain Cameraが利用できません。");
            return;
        }
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        pointCloudRenderer.ResetHighlight(currentPointCloudData);
        if (pointCloudProcessor.FindClosestPoint(ray, 0.1f, out int closestPointIndex))
        {
            List<int> neighborIndices = pointCloudProcessor.FindNeighbors(closestPointIndex, settings.searchRadius);
            UnityEngine.Debug.Log($"Voxel Gridを使用して {neighborIndices.Count} 個の近傍点が見つかりました。");
            pointCloudRenderer.HighlightPoints(closestPointIndex, neighborIndices, currentPointCloudData, Color.magenta, settings.neighborColor);
        }
    }
    #endregion
}