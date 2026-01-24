using System;
using System.Collections.Generic;
using UnityEngine;

public struct SamplingResult
{
    public int SampledCount;
    public Vector3 Centroid;
    public Matrix4x4 CovarianceMatrix;
    public Vector3 CentroidSum;

    public bool IsValid => SampledCount > 0;
}

public class PointCloudCompute : IDisposable
{
    private ComputeShader filterShader;
    private ComputeShader transformShader;

    private ComputeBuffer filteredVerticesBuffer;
    private ComputeBuffer countBuffer;
    private ComputeBuffer samplingBuffer;
    private ComputeBuffer distanceDiscardBuffer;

    private ComputeBuffer argsBuffer;
    private readonly int[] argsData = new int[] { 0, 1, 0, 0 };

    private Vector3 rsScanRange;
    private float frameWidth;
    private float maxPlaneDistance;

    private Vector3 globalThreshold1, globalThreshold2;
    private int rsLength;
    private Matrix4x4 localToWorld;

    private readonly List<Vector3> _sampleCache = new List<Vector3>(1000);
    private readonly int[] _countCache = new int[1];

    // ƒTƒ“ƒvƒŠƒ“ƒOÝ’è
    private const int MAX_SAMPLE_TRANSFER = 2000;
    private const int TARGET_SAMPLE_COUNT = 1000;
    private uint _frameCounter = 0;

    private SamplingResult _lastSamplingResult;
    public SamplingResult LastSamplingResult => _lastSamplingResult;

    public ComputeBuffer GetFilteredVerticesBuffer()
    {
        return filteredVerticesBuffer;
    }

    public ComputeBuffer GetArgsBuffer()
    {
        return argsBuffer;
    }

    public PointCloudCompute(ComputeShader filterShader, ComputeShader transformShader, Vector3 rsScanRange, float frameWidth, float maxPlaneDistance)
    {
        this.filterShader = filterShader;
        this.transformShader = transformShader;
        this.rsScanRange = rsScanRange;
        this.frameWidth = frameWidth;
        this.maxPlaneDistance = maxPlaneDistance;

        globalThreshold1 = new Vector3(frameWidth, frameWidth, frameWidth);
        globalThreshold2 = new Vector3(rsScanRange.x - frameWidth, rsScanRange.y - frameWidth, rsScanRange.z - frameWidth);
        UnityEngine.Debug.Log($"[Debug] Global Thresholds Initialized. Min: {globalThreshold1}, Max: {globalThreshold2}");
    }

    public void InitializeBuffers(int rsLength, Matrix4x4 localToWorld)
    {
        this.rsLength = rsLength;
        this.localToWorld = localToWorld;

        UnityEngine.Debug.Log($"[Compute] {localToWorld}");

        ReleaseBuffers();
        filteredVerticesBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3, ComputeBufferType.Append);
        countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        samplingBuffer = new ComputeBuffer(MAX_SAMPLE_TRANSFER, sizeof(float) * 3, ComputeBufferType.Append);
        distanceDiscardBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3, ComputeBufferType.Append);

        argsBuffer = new ComputeBuffer(1, sizeof(int) * 4, ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(argsData);
    }

    public void UpdateLocalToWorldMatrix(Matrix4x4 localToWorld)
    {
        this.localToWorld = localToWorld;
    }

    private float CalculateSamplingRate(int estimatedPointCount)
    {
        if (estimatedPointCount <= 0) return 0.01f;

        float rate = (float)MAX_SAMPLE_TRANSFER / estimatedPointCount;
        return Mathf.Clamp(rate, 0.001f, 1.0f);
    }

    public (int finalCount, Vector3 point, Vector3 dir, int discardedCount, int sampledCount, float discardPercentage) FilterAndEstimateLine(ComputeBuffer rawVerticesBuffer, Vector3 previousLinePoint, Vector3 previousLineDir, int vertexCount)
    {
        _frameCounter++;
        
        filteredVerticesBuffer.SetCounterValue(0);
        samplingBuffer.SetCounterValue(0);
        distanceDiscardBuffer.SetCounterValue(0);

        int kernel = filterShader.FindKernel("CSMain");

        filterShader.SetBuffer(kernel, "rawVertices", rawVerticesBuffer);
        filterShader.SetBuffer(kernel, "filteredVertices", filteredVerticesBuffer);
        filterShader.SetBuffer(kernel, "samplingBuffer", samplingBuffer);
        filterShader.SetBuffer(kernel, "distanceDiscardBuffer", distanceDiscardBuffer);

        filterShader.SetMatrix("localToWorld", localToWorld);
        filterShader.SetVector("globalThreshold1", globalThreshold1);
        filterShader.SetVector("globalThreshold2", globalThreshold2);
        filterShader.SetInt("vertexCount", vertexCount);
        filterShader.SetFloat("maxDistance", maxPlaneDistance);
        filterShader.SetVector("linePoint", previousLinePoint);
        filterShader.SetVector("lineDir", previousLineDir);
        
        float samplingRate = CalculateSamplingRate(vertexCount);
        filterShader.SetFloat("samplingRate", samplingRate);
        filterShader.SetInt("randomSeed", (int)_frameCounter);

        int threadGroups = Mathf.CeilToInt(vertexCount / 256.0f);
        filterShader.Dispatch(kernel, threadGroups, 1, 1);

        ComputeBuffer.CopyCount(filteredVerticesBuffer, argsBuffer, 0);

        ComputeBuffer.CopyCount(samplingBuffer, countBuffer, 0);
        countBuffer.GetData(_countCache);
        int sampledCount = Mathf.Min(_countCache[0], MAX_SAMPLE_TRANSFER);

        Vector3 point = previousLinePoint;
        Vector3 dir = previousLineDir;

        _lastSamplingResult = new SamplingResult();

        if (sampledCount > 0)
        {
            Vector3[] sampledVertices = new Vector3[sampledCount];
            samplingBuffer.GetData(sampledVertices, 0, 0, sampledCount);
            
            _lastSamplingResult = ComputeSamplingStatistics(sampledVertices, sampledCount);
            
            (point, dir) = EstimateLineCPU(sampledVertices, sampledCount);
        }

        ComputeBuffer.CopyCount(distanceDiscardBuffer, countBuffer, 0);
        countBuffer.GetData(_countCache);
        int discardedCount = _countCache[0];

        float discardPercentage = 0f;
        if (sampledCount > 0)
        {
            discardPercentage = (float)discardedCount / sampledCount * 100f;
        }

        ComputeBuffer.CopyCount(filteredVerticesBuffer, countBuffer, 0);
        countBuffer.GetData(_countCache);
        int finalCount = _countCache[0];

        return (finalCount, point, dir, discardedCount, sampledCount, discardPercentage);
    }

    public (int finalCount, Vector3 point, Vector3 dir, int discardedCount, int sampledCount, float discardPercentage) FilterAndEstimateLine(ComputeBuffer rawVerticesBuffer, Vector3 previousLinePoint, Vector3 previousLineDir)
    {
        return FilterAndEstimateLine(rawVerticesBuffer, previousLinePoint, previousLineDir, rsLength);
    }

    public ComputeBuffer TransformIndirect(ComputeBuffer rawVerticesBuffer, int vertexCount)
    {
        filteredVerticesBuffer.SetCounterValue(0);

        int kernel = transformShader.FindKernel("CSMain");

        transformShader.SetBuffer(kernel, "rawVertices", rawVerticesBuffer);
        transformShader.SetBuffer(kernel, "filteredVertices", filteredVerticesBuffer);
        transformShader.SetMatrix("localToWorld", localToWorld);
        transformShader.SetInt("vertexCount", vertexCount);

        transformShader.SetVector("globalThreshold1", globalThreshold1);
        transformShader.SetVector("globalThreshold2", globalThreshold2);

        int threadGroups = Mathf.CeilToInt(vertexCount / 256.0f);
        transformShader.Dispatch(kernel, threadGroups, 1, 1);

        ComputeBuffer.CopyCount(filteredVerticesBuffer, argsBuffer, 0);

        return argsBuffer;
    }

    public ComputeBuffer TransformIndirect(ComputeBuffer rawVerticesBuffer)
    {
        return TransformIndirect(rawVerticesBuffer, rsLength);
    }

    public int Transform(ComputeBuffer rawVerticesBuffer) { return Transform(rawVerticesBuffer, rsLength); }
    public int Transform(ComputeBuffer rawVerticesBuffer, int vertexCount)
    {
        TransformIndirect(rawVerticesBuffer, vertexCount);
        ComputeBuffer.CopyCount(filteredVerticesBuffer, countBuffer, 0);
        countBuffer.GetData(_countCache);
        return _countCache[0];
    }

    public void GetFilteredVerticesData(Vector3[] outVertices, int count)
    {
        if (count > 0 && count <= outVertices.Length)
        {
            filteredVerticesBuffer.GetData(outVertices, 0, 0, count);
        }
        else if (count > outVertices.Length)
        {
            UnityEngine.Debug.LogWarning($"Buffer count ({count}) exceeds array length ({outVertices.Length}). GetData skipped.");
        }
    }

    public int GetLastFilteredCount()
    {
        if (filteredVerticesBuffer == null || countBuffer == null) return 0;

        ComputeBuffer.CopyCount(filteredVerticesBuffer, countBuffer, 0);

        countBuffer.GetData(_countCache);

        return _countCache[0];
    }

    public (int finalCount, int discardedCount, int sampledCount, float discardPercentage) FilterOnly(
        ComputeBuffer rawVerticesBuffer, Vector3 previousLinePoint, Vector3 previousLineDir, int vertexCount)
    {
        _frameCounter++;
        
        filteredVerticesBuffer.SetCounterValue(0);
        samplingBuffer.SetCounterValue(0);
        distanceDiscardBuffer.SetCounterValue(0);

        int kernel = filterShader.FindKernel("CSMain");

        filterShader.SetBuffer(kernel, "rawVertices", rawVerticesBuffer);
        filterShader.SetBuffer(kernel, "filteredVertices", filteredVerticesBuffer);
        filterShader.SetBuffer(kernel, "samplingBuffer", samplingBuffer);
        filterShader.SetBuffer(kernel, "distanceDiscardBuffer", distanceDiscardBuffer);

        filterShader.SetMatrix("localToWorld", localToWorld);
        filterShader.SetVector("globalThreshold1", globalThreshold1);
        filterShader.SetVector("globalThreshold2", globalThreshold2);
        filterShader.SetInt("vertexCount", vertexCount);
        filterShader.SetFloat("maxDistance", maxPlaneDistance);
        filterShader.SetVector("linePoint", previousLinePoint);
        filterShader.SetVector("lineDir", previousLineDir);
        
        float samplingRate = CalculateSamplingRate(vertexCount);
        filterShader.SetFloat("samplingRate", samplingRate);
        filterShader.SetInt("randomSeed", (int)_frameCounter);

        int threadGroups = Mathf.CeilToInt(vertexCount / 256.0f);
        filterShader.Dispatch(kernel, threadGroups, 1, 1);

        ComputeBuffer.CopyCount(filteredVerticesBuffer, argsBuffer, 0);

        ComputeBuffer.CopyCount(samplingBuffer, countBuffer, 0);
        countBuffer.GetData(_countCache);
        int sampledCount = Mathf.Min(_countCache[0], MAX_SAMPLE_TRANSFER);

        _lastSamplingResult = new SamplingResult();

        if (sampledCount > 0)
        {
            Vector3[] sampledVertices = new Vector3[sampledCount];
            samplingBuffer.GetData(sampledVertices, 0, 0, sampledCount);
            
            _lastSamplingResult = ComputeSamplingStatistics(sampledVertices, sampledCount);
        }

        ComputeBuffer.CopyCount(distanceDiscardBuffer, countBuffer, 0);
        countBuffer.GetData(_countCache);
        int discardedCount = _countCache[0];

        float discardPercentage = 0f;
        if (sampledCount > 0)
        {
            discardPercentage = (float)discardedCount / sampledCount * 100f;
        }

        ComputeBuffer.CopyCount(filteredVerticesBuffer, countBuffer, 0);
        countBuffer.GetData(_countCache);
        int finalCount = _countCache[0];

        return (finalCount, discardedCount, sampledCount, discardPercentage);
    }

    private SamplingResult ComputeSamplingStatistics(Vector3[] vertices, int count)
    {
        if (count < 2)
        {
            return new SamplingResult { SampledCount = count };
        }

        int useCount = Mathf.Min(count, TARGET_SAMPLE_COUNT);
        
        _sampleCache.Clear();
        
        if (count <= TARGET_SAMPLE_COUNT)
        {
            for (int i = 0; i < count; i++)
            {
                _sampleCache.Add(vertices[i]);
            }
        }
        else
        {
            float step = (float)count / TARGET_SAMPLE_COUNT;
            for (int i = 0; i < TARGET_SAMPLE_COUNT; i++)
            {
                int idx = Mathf.Min((int)(i * step), count - 1);
                _sampleCache.Add(vertices[idx]);
            }
        }

        Vector3 centroid = Vector3.zero;
        foreach (var v in _sampleCache) centroid += v;
        centroid /= _sampleCache.Count;

        float xx = 0, xy = 0, xz = 0;
        float yy = 0, yz = 0, zz = 0;
        foreach (var v in _sampleCache)
        {
            Vector3 r = v - centroid;
            xx += r.x * r.x;
            xy += r.x * r.y;
            xz += r.x * r.z;
            yy += r.y * r.y;
            yz += r.y * r.z;
            zz += r.z * r.z;
        }

        Matrix4x4 cov = new Matrix4x4();
        cov[0, 0] = xx; cov[0, 1] = xy; cov[0, 2] = xz;
        cov[1, 0] = xy; cov[1, 1] = yy; cov[1, 2] = yz;
        cov[2, 0] = xz; cov[2, 1] = yz; cov[2, 2] = zz;
        cov[3, 3] = 1;

        return new SamplingResult
        {
            SampledCount = _sampleCache.Count,
            Centroid = centroid,
            CovarianceMatrix = cov,
            CentroidSum = centroid * _sampleCache.Count
        };
    }

    private (Vector3 point, Vector3 dir) EstimateLineCPU(Vector3[] vertices, int count)
    {
        if (count < 2) return (Vector3.zero, Vector3.forward);

        int useCount = Mathf.Min(count, TARGET_SAMPLE_COUNT);
        
        _sampleCache.Clear();
        
        if (count <= TARGET_SAMPLE_COUNT)
        {
            for (int i = 0; i < count; i++)
            {
                _sampleCache.Add(vertices[i]);
            }
        }
        else
        {
            float step = (float)count / TARGET_SAMPLE_COUNT;
            for (int i = 0; i < TARGET_SAMPLE_COUNT; i++)
            {
                int idx = Mathf.Min((int)(i * step), count - 1);
                _sampleCache.Add(vertices[idx]);
            }
        }

        Vector3 centroid = Vector3.zero;
        foreach (var v in _sampleCache) centroid += v;
        centroid /= _sampleCache.Count;

        float xx = 0, xy = 0, xz = 0;
        float yy = 0, yz = 0, zz = 0;
        foreach (var v in _sampleCache)
        {
            Vector3 r = v - centroid;
            xx += r.x * r.x;
            xy += r.x * r.y;
            xz += r.x * r.z;
            yy += r.y * r.y;
            yz += r.y * r.z;
            zz += r.z * r.z;
        }

        Matrix4x4 cov = new Matrix4x4();
        cov[0, 0] = xx; cov[0, 1] = xy; cov[0, 2] = xz;
        cov[1, 0] = xy; cov[1, 1] = yy; cov[1, 2] = yz;
        cov[2, 0] = xz; cov[2, 1] = yz; cov[2, 2] = zz;
        cov[3, 3] = 1;

        Vector3 dir = PowerIterationStatic(cov, 50);
        dir.Normalize();

        return (centroid, dir);
    }

    public static (Vector3 point, Vector3 dir) EstimateLineFromMergedSamples(List<SamplingResult> results)
    {
        if (results == null || results.Count == 0)
            return (Vector3.zero, Vector3.forward);

        int totalCount = 0;
        Vector3 totalCentroidSum = Vector3.zero;

        foreach (var r in results)
        {
            if (!r.IsValid) continue;
            totalCount += r.SampledCount;
            totalCentroidSum += r.CentroidSum;
        }

        if (totalCount < 2)
            return (Vector3.zero, Vector3.forward);

        Vector3 globalCentroid = totalCentroidSum / totalCount;

        Matrix4x4 globalCov = new Matrix4x4();
        globalCov[3, 3] = 1;

        foreach (var r in results)
        {
            if (!r.IsValid) continue;

            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    globalCov[i, j] += r.CovarianceMatrix[i, j];
                }
            }

            Vector3 diff = r.Centroid - globalCentroid;
            globalCov[0, 0] += r.SampledCount * diff.x * diff.x;
            globalCov[0, 1] += r.SampledCount * diff.x * diff.y;
            globalCov[0, 2] += r.SampledCount * diff.x * diff.z;
            globalCov[1, 0] += r.SampledCount * diff.y * diff.x;
            globalCov[1, 1] += r.SampledCount * diff.y * diff.y;
            globalCov[1, 2] += r.SampledCount * diff.y * diff.z;
            globalCov[2, 0] += r.SampledCount * diff.z * diff.x;
            globalCov[2, 1] += r.SampledCount * diff.z * diff.y;
            globalCov[2, 2] += r.SampledCount * diff.z * diff.z;
        }

        Vector3 dir = PowerIterationStatic(globalCov, 50);
        dir.Normalize();

        return (globalCentroid, dir);
    }

    private static Vector3 PowerIterationStatic(Matrix4x4 m, int iterations)
    {
        Vector3 b = new Vector3(1, 1, 1).normalized;
        for (int i = 0; i < iterations; i++)
        {
            Vector3 bNext = new Vector3(
                m[0, 0] * b.x + m[0, 1] * b.y + m[0, 2] * b.z,
                m[1, 0] * b.x + m[1, 1] * b.y + m[1, 2] * b.z,
                m[2, 0] * b.x + m[2, 1] * b.y + m[2, 2] * b.z
            );
            bNext.Normalize();
            b = bNext;
        }
        return b;
    }

    private Vector3 PowerIteration(Matrix4x4 m, int iterations)
    {
        return PowerIterationStatic(m, iterations);
    }

    public void Dispose()
    {
        ReleaseBuffers();
    }

    private void ReleaseBuffers()
    {
        filteredVerticesBuffer?.Release();
        countBuffer?.Release();
        samplingBuffer?.Release();
        distanceDiscardBuffer?.Release();
        argsBuffer?.Release();
    }
}