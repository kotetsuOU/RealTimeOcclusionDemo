using System.Collections.Generic;
using UnityEngine;

public struct RsSamplingResult
{
    public int SampledCount;
    public Vector3 Centroid;
    public Matrix4x4 CovarianceMatrix;
    public Vector3 CentroidSum;

    public bool IsValid => SampledCount > 0;
}

public static class RsPointCloudPCA
{
    private const int DEFAULT_ITERATIONS = 50;
    private const int DEFAULT_MAX_SAMPLES = 1000;

    #region Public API

    public static (Vector3 point, Vector3 dir) EstimateLine(Vector3[] vertices, int count)
    {
        if (count < 2) return (Vector3.zero, Vector3.forward);

        var samples = CollectSamples(vertices, count, DEFAULT_MAX_SAMPLES);
        if (samples.Count < 2) return (Vector3.zero, Vector3.forward);

        Vector3 centroid = ComputeCentroid(samples);
        Matrix4x4 covariance = ComputeCovarianceMatrix(samples, centroid);
        Vector3 dir = PowerIteration(covariance, DEFAULT_ITERATIONS).normalized;

        return (centroid, dir);
    }

    public static (Vector3 point, Vector3 dir) EstimateLineFromMergedSamples(List<RsSamplingResult> results)
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
        Matrix4x4 globalCov = MergeCovarianceMatrices(results, globalCentroid);
        Vector3 dir = PowerIteration(globalCov, DEFAULT_ITERATIONS).normalized;

        return (globalCentroid, dir);
    }

    public static RsSamplingResult ComputeStatistics(Vector3[] vertices, int count)
    {
        if (count < 2)
            return new RsSamplingResult { SampledCount = count };

        var samples = CollectSamples(vertices, count, DEFAULT_MAX_SAMPLES);
        if (samples.Count < 2)
            return new RsSamplingResult { SampledCount = samples.Count };

        Vector3 centroid = ComputeCentroid(samples);
        Matrix4x4 covariance = ComputeCovarianceMatrix(samples, centroid);

        return new RsSamplingResult
        {
            SampledCount = samples.Count,
            Centroid = centroid,
            CovarianceMatrix = covariance,
            CentroidSum = centroid * samples.Count
        };
    }

    public static float CalculateSamplingRate(int estimatedPointCount, int maxSampleTransfer)
    {
        if (estimatedPointCount <= 0) return 0.01f;
        return Mathf.Clamp((float)maxSampleTransfer / estimatedPointCount, 0.001f, 1.0f);
    }

    #endregion

    #region Internal Methods

    private static List<Vector3> CollectSamples(Vector3[] vertices, int count, int maxSampleCount)
    {
        var samples = new List<Vector3>(Mathf.Min(count, maxSampleCount));

        if (count <= maxSampleCount)
        {
            for (int i = 0; i < count; i++)
                samples.Add(vertices[i]);
        }
        else
        {
            float step = (float)count / maxSampleCount;
            for (int i = 0; i < maxSampleCount; i++)
                samples.Add(vertices[Mathf.Min((int)(i * step), count - 1)]);
        }

        return samples;
    }

    private static Vector3 ComputeCentroid(List<Vector3> samples)
    {
        Vector3 sum = Vector3.zero;
        foreach (var v in samples) sum += v;
        return sum / samples.Count;
    }

    private static Matrix4x4 ComputeCovarianceMatrix(List<Vector3> samples, Vector3 centroid)
    {
        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;

        foreach (var v in samples)
        {
            Vector3 r = v - centroid;
            xx += r.x * r.x; xy += r.x * r.y; xz += r.x * r.z;
            yy += r.y * r.y; yz += r.y * r.z; zz += r.z * r.z;
        }

        var cov = new Matrix4x4();
        cov[0, 0] = xx; cov[0, 1] = xy; cov[0, 2] = xz;
        cov[1, 0] = xy; cov[1, 1] = yy; cov[1, 2] = yz;
        cov[2, 0] = xz; cov[2, 1] = yz; cov[2, 2] = zz;
        cov[3, 3] = 1;
        return cov;
    }

    private static Matrix4x4 MergeCovarianceMatrices(List<RsSamplingResult> results, Vector3 globalCentroid)
    {
        var globalCov = new Matrix4x4 { [3, 3] = 1 };

        foreach (var r in results)
        {
            if (!r.IsValid) continue;

            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    globalCov[i, j] += r.CovarianceMatrix[i, j];

            Vector3 d = r.Centroid - globalCentroid;
            int n = r.SampledCount;
            globalCov[0, 0] += n * d.x * d.x; globalCov[0, 1] += n * d.x * d.y; globalCov[0, 2] += n * d.x * d.z;
            globalCov[1, 0] += n * d.y * d.x; globalCov[1, 1] += n * d.y * d.y; globalCov[1, 2] += n * d.y * d.z;
            globalCov[2, 0] += n * d.z * d.x; globalCov[2, 1] += n * d.z * d.y; globalCov[2, 2] += n * d.z * d.z;
        }

        return globalCov;
    }

    private static Vector3 PowerIteration(Matrix4x4 m, int iterations)
    {
        Vector3 b = new Vector3(1, 1, 1).normalized;

        for (int i = 0; i < iterations; i++)
        {
            b = new Vector3(
                m[0, 0] * b.x + m[0, 1] * b.y + m[0, 2] * b.z,
                m[1, 0] * b.x + m[1, 1] * b.y + m[1, 2] * b.z,
                m[2, 0] * b.x + m[2, 1] * b.y + m[2, 2] * b.z
            ).normalized;
        }

        return b;
    }

    #endregion
}
