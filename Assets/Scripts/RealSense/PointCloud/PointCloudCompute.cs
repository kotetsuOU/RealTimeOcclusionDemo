using System;
using System.Collections.Generic;
using UnityEngine;

public class PointCloudCompute : IDisposable
{
    private ComputeShader filterShader;
    private ComputeShader transformShader;

    private ComputeBuffer filteredVerticesBuffer;
    private ComputeBuffer countBuffer;
    private ComputeBuffer samplingBuffer;
    private ComputeBuffer distanceDiscardBuffer;

    private Vector3 rsScanRange;
    private float frameWidth;
    private float maxPlaneDistance;

    private Vector3 globalThreshold1, globalThreshold2;
    private int rsLength;
    private Matrix4x4 localToWorld;

    public ComputeBuffer GetFilteredVerticesBuffer()
    {
        return filteredVerticesBuffer;
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

        ReleaseBuffers();
        filteredVerticesBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3, ComputeBufferType.Append);
        countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        samplingBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3, ComputeBufferType.Append);
        distanceDiscardBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3, ComputeBufferType.Append);
    }

    public (int finalCount, Vector3 point, Vector3 dir, int discardedCount, int sampledCount, float discardPercentage) FilterAndEstimateLine(ComputeBuffer rawVerticesBuffer, Vector3 previousLinePoint, Vector3 previousLineDir)
    {
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
        filterShader.SetInt("vertexCount", rsLength);
        filterShader.SetFloat("maxDistance", maxPlaneDistance);
        filterShader.SetVector("linePoint", previousLinePoint);
        filterShader.SetVector("lineDir", previousLineDir);

        int threadGroups = Mathf.CeilToInt(rsLength / 256.0f);
        filterShader.Dispatch(kernel, threadGroups, 1, 1);


        ComputeBuffer.CopyCount(samplingBuffer, countBuffer, 0);
        int[] sampledCountArr = new int[1];
        countBuffer.GetData(sampledCountArr);
        int sampledCount = sampledCountArr[0];

        Vector3 point = previousLinePoint;
        Vector3 dir = previousLineDir;

        if (sampledCount > 0)
        {
            Vector3[] sampledVertices = new Vector3[sampledCount];
            samplingBuffer.GetData(sampledVertices, 0, 0, sampledCount);
            (point, dir) = EstimateLineCPU(sampledVertices, sampledCount);
        }

        ComputeBuffer.CopyCount(distanceDiscardBuffer, countBuffer, 0);
        int[] discardedCountArr = new int[1];
        countBuffer.GetData(discardedCountArr);
        int discardedCount = discardedCountArr[0];

        float discardPercentage = 0f;
        if (sampledCount > 0)
        {
            discardPercentage = (float)discardedCount / sampledCount * 100f;
        }

        ComputeBuffer.CopyCount(filteredVerticesBuffer, countBuffer, 0);
        int[] finalCountArr = new int[1];
        countBuffer.GetData(finalCountArr);
        int finalCount = finalCountArr[0];

        return (finalCount, point, dir, discardedCount, sampledCount, discardPercentage);
    }

    public int Transform(ComputeBuffer rawVerticesBuffer)
    {
        filteredVerticesBuffer.SetCounterValue(0);

        int kernel = transformShader.FindKernel("CSMain");

        transformShader.SetBuffer(kernel, "rawVertices", rawVerticesBuffer);
        transformShader.SetBuffer(kernel, "filteredVertices", filteredVerticesBuffer);
        transformShader.SetMatrix("localToWorld", localToWorld);
        transformShader.SetInt("vertexCount", rsLength);

        transformShader.SetVector("globalThreshold1", globalThreshold1);
        transformShader.SetVector("globalThreshold2", globalThreshold2);

        int threadGroups = Mathf.CeilToInt(rsLength / 256.0f);
        transformShader.Dispatch(kernel, threadGroups, 1, 1);

        ComputeBuffer.CopyCount(filteredVerticesBuffer, countBuffer, 0);
        int[] countArr = new int[1];
        countBuffer.GetData(countArr);
        int newCount = countArr[0];
        return newCount;
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


    private (Vector3 point, Vector3 dir) EstimateLineCPU(Vector3[] vertices, int count)
    {
        if (count < 2) return (Vector3.zero, Vector3.forward);

        int sampleCount = Mathf.Clamp((int)(count * 0.01f), 100, 1000);
        List<Vector3> sample = new List<Vector3>(sampleCount);

        System.Random rnd = new System.Random();
        for (int i = 0; i < sampleCount; i++)
            sample.Add(vertices[rnd.Next(count)]);

        Vector3 centroid = Vector3.zero;
        foreach (var v in sample) centroid += v;
        centroid /= sample.Count;

        float xx = 0, xy = 0, xz = 0;
        float yy = 0, yz = 0, zz = 0;
        foreach (var v in sample)
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

        Vector3 dir = PowerIteration(cov, 50);
        dir.Normalize();

        return (centroid, dir);
    }

    private Vector3 PowerIteration(Matrix4x4 m, int iterations)
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
    }
}