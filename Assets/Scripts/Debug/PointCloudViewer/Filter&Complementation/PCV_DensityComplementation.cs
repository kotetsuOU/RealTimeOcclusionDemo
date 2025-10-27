using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using System.Linq;

public static class PCV_DensityComplementation
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PCV_Point
    {
        public Vector4 position;
        public Color color;
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
        int originalCount = dataManager.CurrentData.PointCount;

        if (settings.useGpuDensityComplementation && settings.densityComplementationShader != null)
        {
            UnityEngine.Debug.Log($"GPUによる密度補完処理を開始します。");

            combinedData = ApplyGPU(
                dataManager.SpatialSearch.VoxelGrid,
                originalCount,
                settings.densityComplementationShader,
                settings
            );

            if (combinedData == null)
            {
                UnityEngine.Debug.LogError("GPU補完処理中にエラーが発生しました。");
                stopwatch.Stop();
                return;
            }

            if (combinedData.PointCount == originalCount)
            {
                UnityEngine.Debug.LogWarning("閾値を超える有効なボクセルが見つかりませんでした。点は追加されません。");
                stopwatch.Stop();
                return;
            }

            stopwatch.Stop();
            int addedCount = combinedData.PointCount - originalCount;
            UnityEngine.Debug.Log($"密度補完処理 (GPU) が完了しました。{addedCount} 点が追加されました。処理時間: {stopwatch.ElapsedMilliseconds} ms.");
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
            if (combinedData == null)
            {
                stopwatch.Stop();
                return;
            }
        }

        dataManager.SetData(combinedData, settings.voxelSize);
    }

    public static PCV_Data ApplyGPU(VoxelGrid voxelGrid, int originalPointCount, ComputeShader computeShader, PCV_Settings settings)
    {
        if (computeShader == null || voxelGrid == null)
        {
            return new PCV_Data(new List<Vector3>(), new List<Color>());
        }

        if (voxelGrid.VoxelDataBuffer == null || voxelGrid.OriginalPointsBuffer == null)
        {
            UnityEngine.Debug.LogError("VoxelGridのGPUバッファ(VoxelData or OriginalPoints)が初期化されていません。");
            return null;
        }

        int voxelCount = voxelGrid.VoxelDataBuffer.count;
        if (voxelCount == 0 || settings.complementationPointsPerAxis == 0)
        {
            return ReadDataFromGpuBuffer(voxelGrid.OriginalPointsBuffer, originalPointCount, originalPointCount);
        }

        uint pointsPerAxis = settings.complementationPointsPerAxis;
        uint totalPointsPerVoxel = (pointsPerAxis == 1) ? 1u : (pointsPerAxis * pointsPerAxis);

        int maxNewPoints = voxelCount * (int)totalPointsPerVoxel;
        int maxCombinedPoints = originalPointCount + maxNewPoints;
        int pointStructSize = sizeof(float) * 8;

        ComputeBuffer newPointsBuffer = null;
        ComputeBuffer finalCombinedBuffer = null;
        ComputeBuffer countBuffer = null;

        try
        {
            newPointsBuffer = new ComputeBuffer(maxNewPoints, pointStructSize, ComputeBufferType.Append);
            newPointsBuffer.SetCounterValue(0);
            finalCombinedBuffer = new ComputeBuffer(maxCombinedPoints, pointStructSize);

            int kernelComp = computeShader.FindKernel("CSDensityComplementation");
            int kernelMerge = computeShader.FindKernel("CSMerge");
            int kernelBlit = computeShader.FindKernel("CSBlit");

            computeShader.SetInt("_DensityThreshold", settings.complementationDensityThreshold);
            computeShader.SetFloat("_VoxelSize", settings.voxelSize);
            computeShader.SetInt("_PointsPerAxis", (int)pointsPerAxis);
            computeShader.SetInt("_UseRandomPlacement", settings.complementationRandomPlacement ? 1 : 0);
            computeShader.SetVector("_ComplementationColor", settings.complementationPointColor);
            computeShader.SetFloat("_RandomSeed", Time.time);

            computeShader.SetBuffer(kernelComp, "_VoxelData", voxelGrid.VoxelDataBuffer);
            computeShader.SetBuffer(kernelComp, "_ComplementedPointsOut", newPointsBuffer);

            int threadGroups = Mathf.CeilToInt(voxelCount / 64.0f);
            computeShader.Dispatch(kernelComp, threadGroups, 1, 1);

            countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
            ComputeBuffer.CopyCount(newPointsBuffer, countBuffer, 0);

            int[] countArray = { 0 };
            countBuffer.GetData(countArray);
            int newPointCount = countArray[0];
            int finalPointCount = originalPointCount + newPointCount;

            if (finalPointCount > maxCombinedPoints)
            {
                UnityEngine.Debug.LogError($"バッファオーバーラン検出。 確保: {maxCombinedPoints}, 必要: {finalPointCount}");
                finalPointCount = maxCombinedPoints;
                newPointCount = maxCombinedPoints - originalPointCount;
            }

            if (newPointCount > 0)
            {
                computeShader.SetInt("_DensityThreshold", originalPointCount);
                computeShader.SetInt("_PointsPerAxis", newPointCount);

                computeShader.SetBuffer(kernelMerge, "_PointsIn", voxelGrid.OriginalPointsBuffer);
                computeShader.SetBuffer(kernelMerge, "_NewPointsIn", newPointsBuffer);

                computeShader.SetBuffer(kernelMerge, "_PointsOut", finalCombinedBuffer);

                int mergeThreadGroups = Mathf.CeilToInt(finalPointCount / 64.0f);
                computeShader.Dispatch(kernelMerge, mergeThreadGroups, 1, 1);
            }
            else
            {
                if (originalPointCount > 0)
                {
                    computeShader.SetInt("_DensityThreshold", originalPointCount);
                    computeShader.SetBuffer(kernelBlit, "_PointsIn", voxelGrid.OriginalPointsBuffer);

                    computeShader.SetBuffer(kernelBlit, "_PointsOut", finalCombinedBuffer);

                    int blitThreadGroups = Mathf.CeilToInt(originalPointCount / 64.0f);
                    computeShader.Dispatch(kernelBlit, blitThreadGroups, 1, 1);
                }
            }

            return ReadDataFromGpuBuffer(finalCombinedBuffer, finalPointCount, finalPointCount);
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError($"ApplyGPUエラー: {ex.Message}");
            return null;
        }
        finally
        {
            newPointsBuffer?.Release();
            finalCombinedBuffer?.Release();
            countBuffer?.Release();
        }
    }

    private static PCV_Data ReadDataFromGpuBuffer(ComputeBuffer buffer, int countToRead, int listCapacity)
    {
        var finalVertices = new List<Vector3>(listCapacity);
        var finalColors = new List<Color>(listCapacity);

        if (countToRead > 0)
        {
            var finalPointData = new PCV_Point[countToRead];
            buffer.GetData(finalPointData, 0, 0, countToRead);

            for (int i = 0; i < countToRead; i++)
            {
                finalVertices.Add((Vector3)finalPointData[i].position);
                finalColors.Add(finalPointData[i].color);
            }
        }
        return new PCV_Data(finalVertices, finalColors);
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

        int finalCount = currentData.PointCount + additionalVertices.Count;
        var combinedVertices = new List<Vector3>(finalCount);
        var combinedColors = new List<Color>(finalCount);

        combinedVertices.AddRange(currentData.Vertices);
        combinedColors.AddRange(currentData.Colors);

        combinedVertices.AddRange(additionalVertices);
        combinedColors.AddRange(additionalColors);

        PCV_Data combinedData = new PCV_Data(combinedVertices, combinedColors);
        stopwatch.Stop();
        UnityEngine.Debug.Log($"密度補完処理 (CPU) が完了しました。{additionalVertices.Count} 点が追加されました。処理時間: {stopwatch.ElapsedMilliseconds} ms.");

        return combinedData;
    }
}