using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System.Collections;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(PCV_Settings), typeof(PCV_Renderer), typeof(PCV_InputHandler))]
[RequireComponent(typeof(PCV_DataManager), typeof(PCV_OperationHandler))]
public class PCV_Controller : MonoBehaviour
{
    #region Component References
    [SerializeField] private PCV_Settings settings;
    [SerializeField] private PCV_Renderer pointCloudRenderer;
    [SerializeField] private PCV_DataManager dataManager;
    [SerializeField] private PCV_OperationHandler operationHandler;
    #endregion

    private bool isSubscribed = false;
    private PCDRendererFeature pcdRendererFeature;

    #region Unity Lifecycle
    private void Awake()
    {
        InitializeComponentsAndSubscribe();
        FindPCDRendererFeature();
    }

    private void OnEnable()
    {
        if (!isSubscribed)
        {
            InitializeComponentsAndSubscribe();
        }
    }

    private void OnDisable()
    {
        if (dataManager != null && isSubscribed)
        {
            dataManager.OnDataUpdated -= OnDataUpdated;
            isSubscribed = false;
        }
    }

    private void Start()
    {
        if (!UnityEngine.Application.isPlaying) return;
        pointCloudRenderer.InitializeOutline(settings.outline, settings.outlineColor);
        pointCloudRenderer.UpdatePointSize(settings.pointSize);
        StartCoroutine(RebuildPointCloudAfterFrame());
    }

    private IEnumerator RebuildPointCloudAfterFrame()
    {
        yield return null;
        RebuildPointCloud();
        ApplyRenderingSourceSettings();
    }

    private void Update()
    {
        if (!UnityEngine.Application.isPlaying) return;

        bool sourceChanged = settings.HasRenderingSourceChanged();

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

        if (sourceChanged)
        {
            ApplyRenderingSourceSettings();
            if (settings.renderingSource == PointCloudSource.PCV_File_CPU)
            {
                OnDataUpdated(dataManager.CurrentData);
            }
            settings.SaveInspectorState();
        }
    }
    #endregion

    private void ApplyRenderingSourceSettings()
    {
        if (pcdRendererFeature == null) FindPCDRendererFeature();
        if (pcdRendererFeature == null) return;

        if (settings.renderingSource == PointCloudSource.RealSense_GPU_Global)
        {
            pcdRendererFeature.SetUseGlobalBuffer(true);
            UnityEngine.Debug.Log("[PCV] Switched to RealSense (GPU Global Buffer) Mode.");
        }
        else
        {
            pcdRendererFeature.SetUseGlobalBuffer(false);
            UnityEngine.Debug.Log("[PCV] Switched to PCV File (CPU) Mode.");
        }
    }

    private void InitializeComponentsAndSubscribe()
    {
        settings = GetComponent<PCV_Settings>();
        pointCloudRenderer = GetComponent<PCV_Renderer>();
        dataManager = GetComponent<PCV_DataManager>();
        operationHandler = GetComponent<PCV_OperationHandler>();

        if (dataManager != null && !isSubscribed)
        {
            dataManager.OnDataUpdated += OnDataUpdated;
            isSubscribed = true;
        }
    }

    private void OnDataUpdated(PCV_Data data)
    {
        if (pointCloudRenderer == null)
        {
            pointCloudRenderer = GetComponent<PCV_Renderer>();
        }

        if (data != null && data.PointCount > 0)
        {
            pointCloudRenderer.UpdateMesh(data);
        }
        else
        {
            pointCloudRenderer.ClearMesh();
        }

        if (pcdRendererFeature == null)
        {
            FindPCDRendererFeature();
        }

        if (pcdRendererFeature != null)
        {
            if (settings.renderingSource == PointCloudSource.PCV_File_CPU)
            {
                pcdRendererFeature.SetPointCloudData(data);
            }
        }
        else
        {
            UnityEngine.Debug.LogWarning("PCDRendererFeature instance is not ready yet. Data will be sent on the next update.");
        }
    }

    private void FindPCDRendererFeature()
    {
        pcdRendererFeature = PCDRendererFeature.Instance;

        if (pcdRendererFeature == null)
        {
            UnityEngine.Debug.LogWarning("PCDRendererFeatureのインスタンスが見つかりません。アクティブなURPレンダラーにPCDRendererFeatureが追加されているか確認してください。");
        }
    }

    #region Public Methods for UI/Input
    public void RebuildPointCloud()
    {
        InitializeComponentsAndSubscribe();

        if (dataManager == null || settings == null)
        {
            UnityEngine.Debug.LogError("DataManagerまたはSettingsコンポーネントが見つかりません。");
            return;
        }

        dataManager.LoadAndSetData(settings.fileSettings, settings.voxelSize);
    }

    public void StartVoxelDensityFiltering()
    {
        if (operationHandler == null) InitializeComponentsAndSubscribe();

        if (!UnityEngine.Application.isPlaying && (dataManager.CurrentData == null || dataManager.SpatialSearch == null))
        {
            UnityEngine.Debug.Log("点群データがロードされていません (Editor)。再構築を実行します。");
            RebuildPointCloud();
        }

        operationHandler.ExecuteVoxelDensityFilter(dataManager);
    }

    public void StartNeighborFiltering()
    {
        if (operationHandler == null) InitializeComponentsAndSubscribe();

        if (!UnityEngine.Application.isPlaying && (dataManager.CurrentData == null || dataManager.SpatialSearch == null))
        {
            UnityEngine.Debug.Log("点群データがロードされていません (Editor)。再構築を実行します。");
            RebuildPointCloud();
        }

        operationHandler.ExecuteNeighborFilter(dataManager);
    }

    public void StartMorpologyOperation()
    {
        if (operationHandler == null) InitializeComponentsAndSubscribe();

        if (!UnityEngine.Application.isPlaying && (dataManager.CurrentData == null || dataManager.SpatialSearch == null))
        {
            UnityEngine.Debug.Log("点群データがロードされていません (Editor)。再構築を実行します。");
            RebuildPointCloud();
        }

        operationHandler.ExecuteMorphologyOperation(dataManager);
    }

    public void StartDensityComplementation()
    {
        if (operationHandler == null) InitializeComponentsAndSubscribe();

        if (!UnityEngine.Application.isPlaying && (dataManager.CurrentData == null || dataManager.SpatialSearch == null))
        {
            UnityEngine.Debug.Log("点群データがロードされていません (Editor)。密度補完の実行前に再構築を実行します。");
            RebuildPointCloud();
        }

        operationHandler.ExecuteDensityComplementation(dataManager);
    }

    public void ApplyTransformCorrection()
    {
        if (settings == null || settings.fileSettings == null) return;
        int appliedCount = 0;

        Matrix4x4 deltaMatrix = this.transform.localToWorldMatrix;

        foreach (var file in settings.fileSettings)
        {
            if (file.useFile && file.targetObject != null)
            {
                Transform targetT = file.targetObject.transform;

#if UNITY_EDITOR
                Undo.RecordObject(targetT, "Apply PCV Transform");
#endif
                Matrix4x4 targetMatrix = targetT.localToWorldMatrix;

                Matrix4x4 newMatrix = deltaMatrix * targetMatrix;

                targetT.SetPositionAndRotation(GetPositionFromMatrix(newMatrix), newMatrix.rotation);

                appliedCount++;
                UnityEngine.Debug.Log($"[Calibration] Applied Transform Matrix to '{file.targetObject.name}'.");
            }
        }

        if (appliedCount > 0)
        {
#if UNITY_EDITOR
            Undo.RecordObject(this.transform, "Reset Viewer Transform");
#endif
            this.transform.position = Vector3.zero;
            this.transform.rotation = Quaternion.identity;
            this.transform.localScale = Vector3.one;
            UnityEngine.Debug.Log($"[Calibration] {appliedCount} 件のTransformを反映しました。Viewerをリセットしました。");
        }
        else
        {
            UnityEngine.Debug.LogWarning("[Calibration] 反映対象が見つかりませんでした。");
        }
    }

    private Vector3 GetPositionFromMatrix(Matrix4x4 m)
    {
        return m.GetColumn(3);
    }

    public void HandleInteraction()
    {
        if (dataManager == null) InitializeComponentsAndSubscribe();

        if (dataManager.SpatialSearch == null || Camera.main == null)
        {
            UnityEngine.Debug.LogWarning("空間検索モジュールまたはMain Cameraが利用できません。");
            return;
        }
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        pointCloudRenderer.ResetHighlight(dataManager.CurrentData);
        if (dataManager.SpatialSearch.FindClosestPoint(ray, 0.1f, out int closestPointIndex))
        {
            List<int> neighborIndices = dataManager.SpatialSearch.FindNeighbors(closestPointIndex, settings.searchRadius);
            UnityEngine.Debug.Log($"Voxel Gridを使用して {neighborIndices.Count} 個の近傍点が見つかりました。");
            pointCloudRenderer.HighlightPoints(closestPointIndex, neighborIndices, dataManager.CurrentData, Color.magenta, settings.neighborColor);
        }
    }

#if UNITY_EDITOR
    public void ExportVoxelCountsToCSV()
    {
        InitializeComponentsAndSubscribe();

        if (dataManager.SpatialSearch == null)
        {
            UnityEngine.Debug.Log("点群データがロードされていません。再構築を実行します。");
            RebuildPointCloud();
        }

        if (dataManager.SpatialSearch == null || dataManager.SpatialSearch.VoxelGrid == null)
        {
            UnityEngine.Debug.LogError("VoxelGridの初期化に失敗しました。点群をロードしてください。");
            return;
        }

        PCV_VoxelCountExporter.Export(dataManager.SpatialSearch.VoxelGrid);
    }
#endif
    #endregion
}