using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// <see cref="RsIntegratedPointCloudProcessor"/> が ComputeShader へ転送する構造体群。
/// </summary>
internal static class RsIntegratedPointCloudTypes
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RsIntrinsics
    {
        public int width, height;
        public float ppx, ppy, fx, fy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RsExtrinsics
    {
        public float r0, r1, r2, r3, r4, r5, r6, r7, r8;
        public float t0, t1, t2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CullingParams
    {
        public int width, height, mode;
        public float minDist, maxDist, minHue, maxHue, minSat, maxSat, minVal, maxVal;
        public int minY, maxY, minCb, maxCb, minCr, maxCr;
        public Matrix4x4 transformMatrix;
        public int coordinateConversion;
        public int colorFormat; // 0: RGB8, 1: YUYV
    }
}

