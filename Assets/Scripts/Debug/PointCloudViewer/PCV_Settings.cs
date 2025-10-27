using UnityEngine;

[System.Serializable]
public struct FileSettings
{
    public bool useFile;
    public string filePath;
    public Color color;

    public bool IsDifferent(FileSettings other)
    {
        return useFile != other.useFile || filePath != other.filePath || color != other.color;
    }
}

public class PCV_Settings : MonoBehaviour
{
    public FileSettings[] fileSettings = new FileSettings[4]
    {
        new FileSettings { useFile = true,  filePath = "Assets/HandTrakingData/PointCloudData/currentGlobalVerticesRight.txt",  color = Color.red },
        new FileSettings { useFile = false, filePath = "Assets/HandTrakingData/PointCloudData/currentGlobalVerticesLeft.txt",   color = Color.green },
        new FileSettings { useFile = false, filePath = "Assets/HandTrakingData/PointCloudData/currentGlobalVerticesBottom.txt", color = Color.blue },
        new FileSettings { useFile = false, filePath = "Assets/HandTrakingData/PointCloudData/currentGlobalVerticesTop.txt",    color = Color.yellow }
    };

    public float pointSize = 0.01f;
    public GameObject outline;
    public Color outlineColor = Color.white;

    [Tooltip("空間分割グリッドの各セルのサイズ")]
    public float voxelSize = 0.05f;
    [Tooltip("点の周囲で近傍点を探索する半径")]
    public float searchRadius = 0.1f;
    [Tooltip("近傍点をハイライトする色")]
    public Color neighborColor = Color.cyan;
    [Tooltip("ノイズと判断する近傍点の閾値")]
    public int neighborThreshold = 100;
    [Tooltip("ノイズと判断するボクセル内の最小点数")]
    public int voxelDensityThreshold = 5;

    [Tooltip("侵食処理の反復回数")]
    public int erosionIterations = 1;
    [Tooltip("膨張処理の反復回数")]
    public int dilationIterations = 1;

    [Tooltip("補完を行うボクセル内の最小点数")]
    public int complementationDensityThreshold = 5;
    [Tooltip("補完時にボクセルごとに追加する点の1辺の数 (例: 2 = 4点, 3 = 9点)")]
    public uint complementationPointsPerAxis = 2;
    [Tooltip("補完時に追加する点の色")]
    public Color complementationPointColor = Color.purple;
    [Tooltip("有効なボクセル内に点をランダムに配置します。")]
    public bool complementationRandomPlacement = false;

    [Tooltip("各処理をコルーチンで実行し、フレームレートの低下を防ぐ")]
    public bool useCoroutine = false;

    [Header("GPU Acceleration")]
    [Tooltip("点群フィルタリングに使用するCompute Shader")]
    public ComputeShader pointCloudFilterShader;
    [Tooltip("形態学的操作に使用するCompute Shader")]
    public ComputeShader morpologyOperationShader;
    [Tooltip("ボクセル密度フィルタリングに使用するCompute Shader")]
    public ComputeShader densityFilterShader;
    [Tooltip("密度補完に使用するCompute Shader")]
    public ComputeShader densityComplementationShader;
    [Tooltip("ボクセルグリッド構築に使用するCompute Shader")]
    public ComputeShader voxelGridBuilderShader;

    [Tooltip("近傍探索ノイズ除去にGPUを使用する")]
    public bool useGpuNoiseFilter = true;
    [Tooltip("ボクセル密度フィルタリングにGPUを使用する")]
    public bool useGpuDensityFilter = true;
    [Tooltip("密度補完にGPUを使用する")]
    public bool useGpuDensityComplementation = true;

    private FileSettings[] lastFileSettings;
    private float lastPointSize;
    private GameObject lastOutline;
    private Color lastOutlineColor;
    private float lastVoxelSize;
    private float lastSearchRadius;
    private Color lastNeighborColor;
    private int lastNeighborThreshold;
    private int lastVoxelDensityThreshold;

    private int lastErosionIterations;
    private int lastDilationIterations;

    private int lastComplementationDensityThreshold;
    private uint lastComplementationPointsPerAxis;
    private Color lastComplementationPointColor;
    private bool lastComplementationRandomPlacement;

    private ComputeShader lastPointCloudFilterShader;
    private ComputeShader lastMorpologyOperationShader;
    private ComputeShader lastDensityFilterShader;
    private ComputeShader lastDensityComplementationShader;
    private ComputeShader lastVoxelGridBuilderShader;

    private bool lastUseGpuNoiseFilter;
    private bool lastUseGpuDensityFilter;
    private bool lastUseGpuDensityComplementation;


    private void Awake()
    {
        SaveInspectorState();
    }

    public void SaveInspectorState()
    {
        lastFileSettings = new FileSettings[fileSettings.Length];
        for (int i = 0; i < fileSettings.Length; i++)
        {
            lastFileSettings[i] = fileSettings[i];
        }
        lastPointSize = pointSize;
        lastOutline = outline;
        lastOutlineColor = outlineColor;
        lastVoxelSize = voxelSize;
        lastSearchRadius = searchRadius;
        lastNeighborColor = neighborColor;
        lastNeighborThreshold = neighborThreshold;
        lastVoxelDensityThreshold = voxelDensityThreshold;

        lastErosionIterations = erosionIterations;
        lastDilationIterations = dilationIterations;

        lastComplementationDensityThreshold = complementationDensityThreshold;
        lastComplementationPointsPerAxis = complementationPointsPerAxis;
        lastComplementationPointColor = complementationPointColor;
        lastComplementationRandomPlacement = complementationRandomPlacement;

        lastPointCloudFilterShader = pointCloudFilterShader;
        lastMorpologyOperationShader = morpologyOperationShader;
        lastDensityFilterShader = densityFilterShader;
        lastDensityComplementationShader = densityComplementationShader;
        lastVoxelGridBuilderShader = voxelGridBuilderShader;

        lastUseGpuNoiseFilter = useGpuNoiseFilter;
        lastUseGpuDensityFilter = useGpuDensityFilter;
        lastUseGpuDensityComplementation = useGpuDensityComplementation;
    }

    public bool HasFileSettingsChanged()
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

    public bool HasRenderingSettingsChanged()
    {
        return pointSize != lastPointSize || outlineColor != lastOutlineColor || outline != lastOutline;
    }

    public bool HasMorpologySettingsChanged()
    {
        return erosionIterations != lastErosionIterations || dilationIterations != lastDilationIterations || morpologyOperationShader != lastMorpologyOperationShader;
    }

    public bool HasComplementationSettingsChanged()
    {
        return complementationDensityThreshold != lastComplementationDensityThreshold ||
               complementationPointsPerAxis != lastComplementationPointsPerAxis ||
               complementationPointColor != lastComplementationPointColor ||
               complementationRandomPlacement != lastComplementationRandomPlacement;
    }

    public bool HasProcessingSettingsChanged()
    {
        bool densityShadersChanged = (morpologyOperationShader != lastMorpologyOperationShader) ||
                                     (densityFilterShader != lastDensityFilterShader) ||
                                     (densityComplementationShader != lastDensityComplementationShader) ||
                                     (pointCloudFilterShader != lastPointCloudFilterShader) ||
                                     (voxelGridBuilderShader != lastVoxelGridBuilderShader);

        bool processingParamsChanged = voxelSize != lastVoxelSize ||
                                       searchRadius != lastSearchRadius ||
                                       neighborColor != lastNeighborColor ||
                                       neighborThreshold != lastNeighborThreshold ||
                                       voxelDensityThreshold != lastVoxelDensityThreshold;

        return processingParamsChanged ||
               densityShadersChanged ||
               HasMorpologySettingsChanged() ||
               HasComplementationSettingsChanged();
    }
}