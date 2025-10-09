using System.Runtime.InteropServices;
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

    [Header("Outline Settings")]
    public GameObject outline;
    public Color outlineColor = Color.white;

    [Header("Neighbor Search & Filtering")]
    [Tooltip("空間分割グリッドの各セルのサイズ")]
    public float voxelSize = 0.05f;
    [Tooltip("点の周囲で近傍点を探索する半径")]
    public float searchRadius = 0.1f;
    [Tooltip("近傍点をハイライトする色")]
    public Color neighborColor = Color.cyan;
    [Tooltip("ノイズと判断する近傍点の閾値")]
    public int neighborThreshold = 100;

    [Header("Morpology Operation")]
    public int erosionIterations = 1;
    public int dilationIterations = 1;

    [Header("GPU Acceleration")]
    public ComputeShader pointCloudFilterShader;
    public ComputeShader morpologyOperationShader;

    private FileSettings[] lastFileSettings;
    private float lastPointSize;
    private Color lastOutlineColor;
    private float lastVoxelSize;
    private float lastSearchRadius;
    private Color lastNeighborColor;
    private int lastNeighborThreshold;

    private int lastErosionIterations;
    private int lastDilationIterations;
    private ComputeShader lastMorpologyOperationShader;


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
        lastOutlineColor = outlineColor;
        lastVoxelSize = voxelSize;
        lastSearchRadius = searchRadius;
        lastNeighborColor = neighborColor;
        lastNeighborThreshold = neighborThreshold;

        lastErosionIterations = erosionIterations;
        lastDilationIterations = dilationIterations;
        lastMorpologyOperationShader = morpologyOperationShader;
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
        return pointSize != lastPointSize || outlineColor != lastOutlineColor;
    }

    public bool HasMorpologySettingsChanged()
    {
        return erosionIterations != lastErosionIterations || dilationIterations != lastDilationIterations || morpologyOperationShader != lastMorpologyOperationShader;
    }

    public bool HasProcessingSettingsChanged()
    {
        return voxelSize != lastVoxelSize || searchRadius != lastSearchRadius || neighborColor != lastNeighborColor || neighborThreshold != lastNeighborThreshold || HasMorpologySettingsChanged();
    }
}