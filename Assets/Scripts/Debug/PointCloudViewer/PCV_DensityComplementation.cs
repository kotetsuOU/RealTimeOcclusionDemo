using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using System.Linq;

public static class PCV_DensityComplementation
{
    private struct VoxelData
    {
        public Vector3Int index;
        public int pointCount;
        public int dataOffset;
    }

    public static void Execute(PCV_DataManager dataManager, PCV_Settings settings)
    {
        if (dataManager.CurrentData == null || dataManager.SpatialSearch == null || dataManager.SpatialSearch.VoxelGrid == null)
        {
            UnityEngine.Debug.LogWarning("点群データまたはVoxelGridが初期化されていません。処理は実行不可能です。");
            return;
        }

        PCV_Data combinedData;
        var stopwatch = Stopwatch.StartNew();

        if (settings.useGpuDensityComplementation && settings.densityComplementationShader != null)
        {
            UnityEngine.Debug.Log($"GPUによる密度補完処理を開始します。");

            PCV_Data newData = ApplyGPU(
                dataManager.SpatialSearch.VoxelGrid,
                settings.densityComplementationShader,
                settings
            );

            if (newData.PointCount == 0)
            {
                UnityEngine.Debug.LogWarning("閾値を超える有効なボクセルが見つかりませんでした。点は追加されません。");
                return;
            }

            Vector3[] combinedVertices = dataManager.CurrentData.Vertices.Concat(newData.Vertices).ToArray();
            Color[] combinedColors = dataManager.CurrentData.Colors.Concat(newData.Colors).ToArray();
            combinedData = new PCV_Data(combinedVertices, combinedColors);

            stopwatch.Stop();
            UnityEngine.Debug.Log($"密度補完処理 (GPU) が完了しました。{newData.PointCount} 点が追加されました。処理時間: {stopwatch.ElapsedMilliseconds} ms.");
        }
        else
        {
            if (!settings.useGpuDensityComplementation)
            {
                UnityEngine.Debug.Log("CPU実行が選択されています。CPUで密度補完を実行します。");
            }
            else if (settings.densityComplementationShader == null)
            {
                UnityEngine.Debug.LogWarning("GPU実行が選択されていますが、密度補完Compute Shaderが設定されていません。CPUで処理を実行します。");
            }
            combinedData = ApplyCPU(dataManager.CurrentData, dataManager.SpatialSearch.VoxelGrid, settings, stopwatch);
            if (combinedData == null) return;
        }

        dataManager.SetData(combinedData, settings.voxelSize);
    }

    public static PCV_Data ApplyGPU(VoxelGrid voxelGrid, ComputeShader computeShader, PCV_Settings settings)
    {
        if (computeShader == null || voxelGrid == null)
        {
            return new PCV_Data(new List<Vector3>(), new List<Color>());
        }

        if (voxelGrid.VoxelDataBuffer == null)
        {
            UnityEngine.Debug.LogError("VoxelGridのGPUバッファが初期化されていません。");
            return new PCV_Data(new List<Vector3>(), new List<Color>());
        }

        int voxelCount = voxelGrid.VoxelDataBuffer.count;
        if (voxelCount == 0 || settings.complementationPointsPerAxis == 0)
        {
            return new PCV_Data(new List<Vector3>(), new List<Color>());
        }

        uint pointsPerAxis = settings.complementationPointsPerAxis;
        uint totalPointsPerVoxel = (pointsPerAxis == 1) ? 1u : (pointsPerAxis * pointsPerAxis);
        int maxNewPoints = voxelCount * (int)totalPointsPerVoxel;

        ComputeBuffer complementedPointsBuffer = null;
        ComputeBuffer countBuffer = null;

        try
        {
            complementedPointsBuffer = new ComputeBuffer(maxNewPoints, sizeof(float) * 8, ComputeBufferType.Append);
            complementedPointsBuffer.SetCounterValue(0);

            int kernel = computeShader.FindKernel("CSDensityComplementation");
            computeShader.SetInt("_DensityThreshold", settings.complementationDensityThreshold);
            computeShader.SetFloat("_VoxelSize", settings.voxelSize);
            computeShader.SetInt("_PointsPerAxis", (int)pointsPerAxis);
            computeShader.SetInt("_UseRandomPlacement", settings.complementationRandomPlacement ? 1 : 0);
            computeShader.SetVector("_ComplementationColor", settings.complementationPointColor);
            computeShader.SetFloat("_RandomSeed", Time.time);

            computeShader.SetBuffer(kernel, "_VoxelData", voxelGrid.VoxelDataBuffer);
            computeShader.SetBuffer(kernel, "_ComplementedPointsOut", complementedPointsBuffer);

            int threadGroups = Mathf.CeilToInt(voxelCount / 64.0f);
            computeShader.Dispatch(kernel, threadGroups, 1, 1);

            countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
            ComputeBuffer.CopyCount(complementedPointsBuffer, countBuffer, 0);

            int[] countArray = { 0 };
            countBuffer.GetData(countArray);
            int newPointCount = countArray[0];

            var newVertices = new List<Vector3>();
            var newColors = new List<Color>();

            if (newPointCount > 0)
            {
                var newPointData = new PCV_Point[newPointCount];
                complementedPointsBuffer.GetData(newPointData, 0, 0, newPointCount);

                for (int i = 0; i < newPointCount; i++)
                {
                    newVertices.Add(newPointData[i].position);
                    newColors.Add(newPointData[i].color);
                }
            }

            return new PCV_Data(newVertices, newColors);
        }
        finally
        {
            if (complementedPointsBuffer != null && complementedPointsBuffer.IsValid())
            {
                complementedPointsBuffer.Release();
            }

            if (countBuffer != null && countBuffer.IsValid())
            {
                countBuffer.Release();
            }
        }
    }

    private static PCV_Data ApplyCPU(PCV_Data currentData, VoxelGrid voxelGrid, PCV_Settings settings, Stopwatch stopwatch)
    {
        uint pointsPerAxis = settings.complementationPointsPerAxis;
        if (pointsPerAxis == 0)
        {
            UnityEngine.Debug.LogWarning("complementationPointsPerAxis が 0 以下に設定されているため、処理をスキップします。");
            return null;
        }
        bool useRandomPlacement = settings.complementationRandomPlacement;
        uint totalPointsPerVoxel = (pointsPerAxis == 1) ? 1u : (pointsPerAxis * pointsPerAxis);
        string placementMode = useRandomPlacement ? "ランダム配置" : "均等配置";
        UnityEngine.Debug.Log($"密度補完処理 (CPU) を開始します。(閾値: {settings.complementationDensityThreshold}, 追加点: {totalPointsPerVoxel}点/Voxel, モード: {placementMode})");

        float voxelSize = settings.voxelSize;
        Color pointColor = settings.complementationPointColor;
        var additionalVertices = new List<Vector3>();
        var additionalColors = new List<Color>();
        foreach (var kvp in voxelGrid.Grid)
        {
            if (kvp.Value.Count >= settings.complementationDensityThreshold)
            {
                Vector3Int voxelIndex = kvp.Key;
                float centerX = (voxelIndex.x * voxelSize) + (voxelSize / 2.0f);
                float voxelMinY = voxelIndex.y * voxelSize;
                float voxelMinZ = voxelIndex.z * voxelSize;
                if (useRandomPlacement)
                {
                    float voxelMaxY = voxelMinY + voxelSize;
                    float voxelMaxZ = voxelMinZ + voxelSize;
                    for (uint i = 0; i < totalPointsPerVoxel; i++)
                    {
                        float pointY = UnityEngine.Random.Range(voxelMinY, voxelMaxY);
                        float pointZ = UnityEngine.Random.Range(voxelMinZ, voxelMaxZ);
                        additionalVertices.Add(new Vector3(centerX, pointY, pointZ));
                        additionalColors.Add(pointColor);
                    }
                }
                else
                {
                    if (pointsPerAxis == 1)
                    {
                        float centerY = voxelMinY + (voxelSize / 2.0f);
                        float centerZ = voxelMinZ + (voxelSize / 2.0f);
                        additionalVertices.Add(new Vector3(centerX, centerY, centerZ));
                        additionalColors.Add(pointColor);
                    }
                    else
                    {
                        float step = voxelSize / pointsPerAxis;
                        float initialOffset = step / 2.0f;
                        for (uint y = 0; y < pointsPerAxis; y++)
                        {
                            float pointY = voxelMinY + initialOffset + (step * y);
                            for (uint z = 0; z < pointsPerAxis; z++)
                            {
                                float pointZ = voxelMinZ + initialOffset + (step * z);
                                additionalVertices.Add(new Vector3(centerX, pointY, pointZ));
                                additionalColors.Add(pointColor);
                            }
                        }
                    }
                }
            }
        }
        if (additionalVertices.Count == 0)
        {
            UnityEngine.Debug.LogWarning("閾値を超える有効なボクセルが見つかりませんでした。点は追加されません。");
            return null;
        }
        Vector3[] combinedVertices = currentData.Vertices.Concat(additionalVertices).ToArray();
        Color[] combinedColors = currentData.Colors.Concat(additionalColors).ToArray();
        PCV_Data combinedData = new PCV_Data(combinedVertices, combinedColors);
        stopwatch.Stop();
        UnityEngine.Debug.Log($"密度補完処理 (CPU) が完了しました。{additionalVertices.Count} 点が追加されました。処理時間: {stopwatch.ElapsedMilliseconds} ms.");

        return combinedData;
    }
}
