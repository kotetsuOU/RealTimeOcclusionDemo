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

    private void InitializeComponentsAndSubscribe()
    {
        settings = GetComponent<PCV_Settings>();
        pointCloudRenderer = GetComponent<PCV_Renderer>();
        dataManager = GetComponent<PCV_DataManager>();
        operationHandler = GetComponent<PCV_OperationHandler>();

        if (settings != null) settings.hideFlags = HideFlags.HideInInspector;
        if (pointCloudRenderer != null) pointCloudRenderer.hideFlags = HideFlags.HideInInspector;

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
            pcdRendererFeature.SetPointCloudData(data);
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
        else
        {
            UnityEngine.Debug.Log("PCDRendererFeatureのインスタンスを発見しました。");
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

    public void ApplyRotationCorrection()
    {
        if (settings == null || settings.fileSettings == null) return;
        int appliedCount = 0;

        foreach (var file in settings.fileSettings)
        {
            if (file.useFile && file.targetObject != null)
            {
                Transform targetT = file.targetObject.transform;
#if UNITY_EDITOR
                Undo.RecordObject(targetT, "Apply PCV Rotation");
#endif
                targetT.rotation *= this.transform.rotation;
                appliedCount++;
                UnityEngine.Debug.Log($"[Calibration] Rotation applied to '{file.targetObject.name}'. Added: {this.transform.rotation.eulerAngles}");
            }
        }

        if (appliedCount > 0)
        {
#if UNITY_EDITOR
            Undo.RecordObject(this.transform, "Reset Viewer Rotation");
#endif
            this.transform.rotation = Quaternion.identity;
            UnityEngine.Debug.Log($"[Calibration] {appliedCount} 件の回転を反映しました。Viewerの回転をリセットしました。");
        }
        else
        {
            UnityEngine.Debug.LogWarning("[Calibration] 反映対象が見つかりませんでした。");
        }
    }

    public void ApplyPositionCorrection()
    {
        if (settings == null || settings.fileSettings == null) return;
        int appliedCount = 0;

        foreach (var file in settings.fileSettings)
        {
            if (file.useFile && file.targetObject != null)
            {
                Transform targetT = file.targetObject.transform;
#if UNITY_EDITOR
                Undo.RecordObject(targetT, "Apply PCV Position");
#endif
                targetT.position += this.transform.position;
                appliedCount++;
                UnityEngine.Debug.Log($"[Calibration] Position applied to '{file.targetObject.name}'. Added: {this.transform.position}");
            }
        }

        if (appliedCount > 0)
        {
#if UNITY_EDITOR
            Undo.RecordObject(this.transform, "Reset Viewer Position");
#endif
            this.transform.position = Vector3.zero;
            UnityEngine.Debug.Log($"[Calibration] {appliedCount} 件の位置を反映しました。Viewerの位置をリセットしました。");
        }
        else
        {
            UnityEngine.Debug.LogWarning("[Calibration] 反映対象が見つかりませんでした。");
        }
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