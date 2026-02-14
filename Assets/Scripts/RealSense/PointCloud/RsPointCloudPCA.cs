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

        int usedSampleCount = 0;
        double sumX = 0, sumY = 0, sumZ = 0;

        int maxSamples = DEFAULT_MAX_SAMPLES;

        if (count <= maxSamples)
        {
            usedSampleCount = count;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = vertices[i];
                sumX += v.x;
                sumY += v.y;
                sumZ += v.z;
            }
        }
        else
        {
            usedSampleCount = maxSamples;
            float step = (float)count / maxSamples;
            for (int i = 0; i < maxSamples; i++)
            {
                int idx = Mathf.Min((int)(i * step), count - 1);
                Vector3 v = vertices[idx];
                sumX += v.x;
                sumY += v.y;
                sumZ += v.z;
            }
        }

        float invCount = 1.0f / usedSampleCount;
        Vector3 centroid = new Vector3((float)(sumX * invCount), (float)(sumY * invCount), (float)(sumZ * invCount));
        float cx = centroid.x, cy = centroid.y, cz = centroid.z;

        float cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;

        if (count <= maxSamples)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 v = vertices[i];
                float rx = v.x - cx;
                float ry = v.y - cy;
                float rz = v.z - cz;

                cxx += rx * rx; cxy += rx * ry; cxz += rx * rz;
                cyy += ry * ry; cyz += ry * rz; czz += rz * rz;
            }
        }
        else
        {
            float step = (float)count / maxSamples;
            for (int i = 0; i < maxSamples; i++)
            {
                int idx = Mathf.Min((int)(i * step), count - 1);
                Vector3 v = vertices[idx];
                float rx = v.x - cx;
                float ry = v.y - cy;
                float rz = v.z - cz;

                cxx += rx * rx; cxy += rx * ry; cxz += rx * rz;
                cyy += ry * ry; cyz += ry * rz; czz += rz * rz;
            }
        }

        Matrix4x4 covariance = new Matrix4x4();
        covariance.m00 = cxx; covariance.m01 = cxy; covariance.m02 = cxz;
        covariance.m10 = cxy; covariance.m11 = cyy; covariance.m12 = cyz;
        covariance.m20 = cxz; covariance.m21 = cyz; covariance.m22 = czz;
        covariance.m33 = 1;

        Vector3 dir = PowerIteration(covariance, DEFAULT_ITERATIONS); // PowerIteration“à‚Å³‹K‰»Ï‚Ý

        return (centroid, dir);
    }

    public static (Vector3 point, Vector3 dir) EstimateLineFromMergedSamples(List<RsSamplingResult> results)
    {
        if (results == null || results.Count == 0)
            return (Vector3.zero, Vector3.forward);

        int totalCount = 0;
        double sumX = 0, sumY = 0, sumZ = 0;

        int resultCount = results.Count;
        for (int i = 0; i < resultCount; i++)
        {
            RsSamplingResult r = results[i];
            if (!r.IsValid) continue;

            totalCount += r.SampledCount;
            sumX += r.CentroidSum.x;
            sumY += r.CentroidSum.y;
            sumZ += r.CentroidSum.z;
        }

        if (totalCount < 2)
            return (Vector3.zero, Vector3.forward);

        float invTotal = 1.0f / totalCount;
        Vector3 globalCentroid = new Vector3((float)(sumX * invTotal), (float)(sumY * invTotal), (float)(sumZ * invTotal));
        float gcx = globalCentroid.x, gcy = globalCentroid.y, gcz = globalCentroid.z;

        Matrix4x4 globalCov = new Matrix4x4();
        globalCov.m33 = 1;

        for (int k = 0; k < resultCount; k++)
        {
            RsSamplingResult r = results[k];
            if (!r.IsValid) continue;

            globalCov.m00 += r.CovarianceMatrix.m00; globalCov.m01 += r.CovarianceMatrix.m01; globalCov.m02 += r.CovarianceMatrix.m02;
            globalCov.m10 += r.CovarianceMatrix.m10; globalCov.m11 += r.CovarianceMatrix.m11; globalCov.m12 += r.CovarianceMatrix.m12;
            globalCov.m20 += r.CovarianceMatrix.m20; globalCov.m21 += r.CovarianceMatrix.m21; globalCov.m22 += r.CovarianceMatrix.m22;

            float dx = r.Centroid.x - gcx;
            float dy = r.Centroid.y - gcy;
            float dz = r.Centroid.z - gcz;
            int n = r.SampledCount;

            globalCov.m00 += n * dx * dx; globalCov.m01 += n * dx * dy; globalCov.m02 += n * dx * dz;
            globalCov.m10 += n * dy * dx; globalCov.m11 += n * dy * dy; globalCov.m12 += n * dy * dz;
            globalCov.m20 += n * dz * dx; globalCov.m21 += n * dz * dy; globalCov.m22 += n * dz * dz;
        }

        Vector3 dir = PowerIteration(globalCov, DEFAULT_ITERATIONS);

        return (globalCentroid, dir);
    }

    public static RsSamplingResult ComputeStatistics(Vector3[] vertices, int count)
    {
        if (count < 2)
            return new RsSamplingResult { SampledCount = count };

        int usedSampleCount = 0;
        double sumX = 0, sumY = 0, sumZ = 0;
        int maxSamples = DEFAULT_MAX_SAMPLES;

        if (count <= maxSamples)
        {
            usedSampleCount = count;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = vertices[i];
                sumX += v.x; sumY += v.y; sumZ += v.z;
            }
        }
        else
        {
            usedSampleCount = maxSamples;
            float step = (float)count / maxSamples;
            for (int i = 0; i < maxSamples; i++)
            {
                int idx = Mathf.Min((int)(i * step), count - 1);
                Vector3 v = vertices[idx];
                sumX += v.x; sumY += v.y; sumZ += v.z;
            }
        }

        float invCount = 1.0f / usedSampleCount;
        Vector3 centroidSum = new Vector3((float)sumX, (float)sumY, (float)sumZ);
        Vector3 centroid = new Vector3((float)(sumX * invCount), (float)(sumY * invCount), (float)(sumZ * invCount));
        float cx = centroid.x, cy = centroid.y, cz = centroid.z;

        float cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;

        if (count <= maxSamples)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 v = vertices[i];
                float rx = v.x - cx;
                float ry = v.y - cy;
                float rz = v.z - cz;

                cxx += rx * rx; cxy += rx * ry; cxz += rx * rz;
                cyy += ry * ry; cyz += ry * rz; czz += rz * rz;
            }
        }
        else
        {
            float step = (float)count / maxSamples;
            for (int i = 0; i < maxSamples; i++)
            {
                int idx = Mathf.Min((int)(i * step), count - 1);
                Vector3 v = vertices[idx];
                float rx = v.x - cx;
                float ry = v.y - cy;
                float rz = v.z - cz;

                cxx += rx * rx; cxy += rx * ry; cxz += rx * rz;
                cyy += ry * ry; cyz += ry * rz; czz += rz * rz;
            }
        }

        Matrix4x4 covariance = new Matrix4x4();
        covariance.m00 = cxx; covariance.m01 = cxy; covariance.m02 = cxz;
        covariance.m10 = cxy; covariance.m11 = cyy; covariance.m12 = cyz;
        covariance.m20 = cxz; covariance.m21 = cyz; covariance.m22 = czz;
        covariance.m33 = 1;

        return new RsSamplingResult
        {
            SampledCount = usedSampleCount,
            Centroid = centroid,
            CovarianceMatrix = covariance,
            CentroidSum = centroidSum
        };
    }

    public static float CalculateSamplingRate(int estimatedPointCount, int maxSampleTransfer)
    {
        if (estimatedPointCount <= 0) return 0.01f;
        return Mathf.Clamp((float)maxSampleTransfer / estimatedPointCount, 0.001f, 1.0f);
    }

    #endregion

    #region Internal Methods

    private static Vector3 PowerIteration(Matrix4x4 m, int iterations)
    {
        float bx = 1f, by = 1f, bz = 1f;

        for (int i = 0; i < iterations; i++)
        {
            float nx = m.m00 * bx + m.m01 * by + m.m02 * bz;
            float ny = m.m10 * bx + m.m11 * by + m.m12 * bz;
            float nz = m.m20 * bx + m.m21 * by + m.m22 * bz;

            float lengthSq = nx * nx + ny * ny + nz * nz;
            if (lengthSq < 1e-6f) break;

            float invLength = 1.0f / Mathf.Sqrt(lengthSq);

            bx = nx * invLength;
            by = ny * invLength;
            bz = nz * invLength;
        }

        return new Vector3(bx, by, bz);
    }

    #endregion
}