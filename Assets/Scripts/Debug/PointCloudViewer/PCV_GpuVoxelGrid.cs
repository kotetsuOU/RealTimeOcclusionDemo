using UnityEngine;
using System;

public class PCV_GpuVoxelGrid : IDisposable
{
    public ComputeBuffer VoxelDataBuffer { get; private set; }
    public ComputeBuffer VoxelHashTableBuffer { get; private set; }
    public ComputeBuffer VoxelHashChainsBuffer { get; private set; }
    public ComputeBuffer VoxelPointIndicesBuffer { get; private set; }

    public int VoxelCount { get; private set; } = 0;
    public int HashTableSize { get; private set; } = 0;
    public float VoxelSize { get; private set; } = 0.1f;

    private ComputeShader builderShader;
    private ComputeBuffer voxelCounterBuffer;

    private int kernelClearTable, kernelClearData, kernelBuildGrid, kernelScanOffsets, kernelBuildIndices;

    private int maxVoxelCount = 0;
    private int maxPointCount = 0;

    public PCV_GpuVoxelGrid(ComputeShader shader, float voxelSize)
    {
        if (shader == null)
        {
            UnityEngine.Debug.LogError("VoxelGridBuilder Compute Shader is not set!");
            return;
        }

        this.builderShader = shader;
        this.VoxelSize = voxelSize;

        kernelClearTable = builderShader.FindKernel("CSClearHashTable");
        kernelClearData = builderShader.FindKernel("CSClearVoxelData");
        kernelBuildGrid = builderShader.FindKernel("CSBuildGrid");
        kernelScanOffsets = builderShader.FindKernel("CSScanOffsets");
        kernelBuildIndices = builderShader.FindKernel("CSBuildIndices");
    }

    public void AllocateBuffers(int newMaxPoints)
    {
        if (newMaxPoints <= 0) newMaxPoints = 1;

        if (maxPointCount >= newMaxPoints && VoxelDataBuffer != null)
        {
            return;
        }

        ReleaseBuffers();

        maxPointCount = newMaxPoints;
        maxVoxelCount = newMaxPoints;

        int voxelDataStructSize = 24;

        VoxelDataBuffer = new ComputeBuffer(maxVoxelCount, voxelDataStructSize, ComputeBufferType.Structured);
        VoxelPointIndicesBuffer = new ComputeBuffer(maxPointCount, sizeof(int));

        HashTableSize = GetNextPrime(maxPointCount * 2);
        VoxelHashTableBuffer = new ComputeBuffer(HashTableSize, sizeof(int));

        VoxelHashChainsBuffer = new ComputeBuffer(maxVoxelCount, sizeof(int));

        voxelCounterBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Default);
    }

    public void Build(ComputeBuffer pointsIn, int pointCount)
    {
        if (pointCount == 0)
        {
            VoxelCount = 0;
            return;
        }

        AllocateBuffers(pointCount);

        builderShader.SetBuffer(kernelClearTable, "_VoxelHashTable", VoxelHashTableBuffer);
        builderShader.SetBuffer(kernelClearTable, "_VoxelCounter", voxelCounterBuffer);
        builderShader.SetInt("_HashTableSize", HashTableSize);

        builderShader.SetBuffer(kernelClearData, "_VoxelData", VoxelDataBuffer);
        builderShader.SetInt("_MaxVoxelCount", maxVoxelCount);

        builderShader.SetBuffer(kernelBuildGrid, "_PointsIn", pointsIn);
        builderShader.SetInt("_PointCount", pointCount);
        builderShader.SetFloat("_VoxelSize", VoxelSize);
        builderShader.SetInt("_HashTableSize", HashTableSize);
        builderShader.SetInt("_MaxVoxelCount", maxVoxelCount);
        builderShader.SetBuffer(kernelBuildGrid, "_VoxelData", VoxelDataBuffer);
        builderShader.SetBuffer(kernelBuildGrid, "_VoxelHashTable", VoxelHashTableBuffer);
        builderShader.SetBuffer(kernelBuildGrid, "_VoxelHashChains", VoxelHashChainsBuffer);
        builderShader.SetBuffer(kernelBuildGrid, "_VoxelCounter", voxelCounterBuffer);

        builderShader.SetBuffer(kernelScanOffsets, "_VoxelData", VoxelDataBuffer);
        builderShader.SetBuffer(kernelScanOffsets, "_VoxelCounter", voxelCounterBuffer);

        builderShader.SetBuffer(kernelBuildIndices, "_PointsIn", pointsIn);
        builderShader.SetInt("_PointCount", pointCount);
        builderShader.SetFloat("_VoxelSize", VoxelSize);
        builderShader.SetInt("_HashTableSize", HashTableSize);
        builderShader.SetBuffer(kernelBuildIndices, "_VoxelData", VoxelDataBuffer);
        builderShader.SetBuffer(kernelBuildIndices, "_VoxelHashTable", VoxelHashTableBuffer);
        builderShader.SetBuffer(kernelBuildIndices, "_VoxelHashChains", VoxelHashChainsBuffer);
        builderShader.SetBuffer(kernelBuildIndices, "_VoxelPointIndices", VoxelPointIndicesBuffer);

        int threadGroups = Mathf.CeilToInt(HashTableSize / 256.0f);
        builderShader.Dispatch(kernelClearTable, threadGroups, 1, 1);

        threadGroups = Mathf.CeilToInt(maxVoxelCount / 256.0f);
        builderShader.Dispatch(kernelClearData, threadGroups, 1, 1);

        threadGroups = Mathf.CeilToInt(pointCount / 256.0f);
        builderShader.Dispatch(kernelBuildGrid, threadGroups, 1, 1);

        builderShader.Dispatch(kernelScanOffsets, 1, 1, 1);

        builderShader.Dispatch(kernelBuildIndices, threadGroups, 1, 1);

        uint[] countArray = { 0 };
        voxelCounterBuffer.GetData(countArray);
        VoxelCount = (int)System.Math.Min(countArray[0], (uint)maxVoxelCount);
    }

    public void Dispose()
    {
        ReleaseBuffers();
    }

    public void ReleaseBuffers()
    {
        VoxelDataBuffer?.Release();
        VoxelHashTableBuffer?.Release();
        VoxelHashChainsBuffer?.Release();
        VoxelPointIndicesBuffer?.Release();
        voxelCounterBuffer?.Release();

        VoxelDataBuffer = null;
        VoxelHashTableBuffer = null;
        VoxelHashChainsBuffer = null;
        VoxelPointIndicesBuffer = null;
        voxelCounterBuffer = null;

        maxVoxelCount = 0;
        maxPointCount = 0;
        VoxelCount = 0;
    }

    private static int GetNextPrime(int min)
    {
        if (min < 2) min = 2;
        for (int i = min | 1; i < int.MaxValue; i += 2)
        {
            if (IsPrime(i)) return i;
        }
        return min;
    }

    private static bool IsPrime(int n)
    {
        if (n < 2) return false;
        if (n == 2 || n == 3) return true;
        if (n % 2 == 0 || n % 3 == 0) return false;
        for (int i = 5; i * i <= n; i += 6)
        {
            if (n % i == 0 || n % (i + 2) == 0) return false;
        }
        return true;
    }
}