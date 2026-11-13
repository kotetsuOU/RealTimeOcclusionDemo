using System.Collections.Generic;
using UnityEngine;

namespace OPE
{
    /// <summary>
    /// 疎・ノイズあり実測点群に対するオクルージョンパイプライン（投影、密度 LOD、深度ピラミッド構築、
    /// 勾配補正、8 方向セクター最小局所評価値 minSectorOcclusion[0..7] の算出）を
    /// シェーダー (PCD_Occlusion_Kernels_*.hlsl) と同一アルゴリズムで構築・計算する Pure C# クラス。
    /// </summary>
    public static class OPE_SparsePointCloudPyramidEvaluator
    {
        public const int MaxPyramidLevel = 6;
        public const float SentinelDepth = 1e9f;
        public const float DepthEpsilon = 1e-5f;
        private const uint DepthMaxUint = 0xFFFFFFFFu;

        private static readonly Vector2Int[] SectorOffsets = new Vector2Int[8]
        {
            new Vector2Int( 0, -1), // 0: North
            new Vector2Int( 1, -1), // 1: North-East
            new Vector2Int( 1,  0), // 2: East
            new Vector2Int( 1,  1), // 3: South-East
            new Vector2Int( 0,  1), // 4: South
            new Vector2Int(-1,  1), // 5: South-West
            new Vector2Int(-1,  0), // 6: West
            new Vector2Int(-1, -1)  // 7: North-West
        };

        /// <summary>
        /// 疎・ノイズあり実測点群と仮想物体マップから、パイプラインの深度ピラミッド・探索半径・
        /// 各セクターの最小局所評価値 (minSectorOcclusion[0..7]) を一括構築します。
        /// </summary>
        public static OPE_EyeEvaluationCache BuildEyeEvaluationCache(
            Camera camera,
            Vector3[] sparseWorldPoints,
            int pointCount,
            Vector4[] virtualObjectMap,
            PCDRendererFeature.PCDRenderSettings settings,
            int width,
            int height)
        {
            if (camera == null || virtualObjectMap == null || width <= 0 || height <= 0)
            {
                return null;
            }

            int totalPixels = width * height;
            Vector4[] viewPosMap = new Vector4[totalPixels];
            byte[] originTypeMap = new byte[totalPixels]; // 0: Physical, 1: Virtual, 2: Background
            uint[] depthUintMap = new uint[totalPixels];

            InitializeFromVirtualObjectMap(virtualObjectMap, totalPixels, viewPosMap, originTypeMap, depthUintMap);
            ProjectSparsePoints(camera, sparseWorldPoints, pointCount, width, height, viewPosMap, originTypeMap, depthUintMap);

            Vector4[][] pyramid = new Vector4[MaxPyramidLevel + 1][];
            int[] levelWidths = new int[MaxPyramidLevel + 1];
            int[] levelHeights = new int[MaxPyramidLevel + 1];
            BuildPhysicalDepthPyramid(
                viewPosMap, originTypeMap, width, height, settings.enableTagBasedOptimization,
                pyramid, levelWidths, levelHeights);

            byte[] searchLevels = OPE_SearchLevelMapBuilder.Build(
                depthUintMap, originTypeMap, pyramid, levelWidths, levelHeights, settings, width, height);

            EvaluateMinSectorOcclusion(
                viewPosMap, originTypeMap, pyramid, levelWidths, levelHeights, searchLevels, settings, width, height,
                out int[] evalIndices, out float[] evalMinSectors);

            return new OPE_EyeEvaluationCache
            {
                width = width,
                height = height,
                searchLevels = searchLevels,
                evaluatedPixelIndices = evalIndices,
                evaluatedMinSectorOcclusion = evalMinSectors
            };
        }

        private static void InitializeFromVirtualObjectMap(
            Vector4[] virtualObjectMap,
            int totalPixels,
            Vector4[] viewPosMap,
            byte[] originTypeMap,
            uint[] depthUintMap)
        {
            Vector4 sentinel = new Vector4(0f, 0f, 0f, SentinelDepth);
            for (int i = 0; i < totalPixels; i++)
            {
                Vector4 vo = virtualObjectMap[i];
                if (vo.w < SentinelDepth)
                {
                    viewPosMap[i] = vo;
                    originTypeMap[i] = 1;
                    depthUintMap[i] = (uint)(Mathf.Clamp01(vo.w) * (double)DepthMaxUint);
                }
                else
                {
                    viewPosMap[i] = sentinel;
                    originTypeMap[i] = 2;
                    depthUintMap[i] = DepthMaxUint;
                }
            }
        }

        private static void ProjectSparsePoints(
            Camera camera,
            Vector3[] sparseWorldPoints,
            int pointCount,
            int width,
            int height,
            Vector4[] viewPosMap,
            byte[] originTypeMap,
            uint[] depthUintMap)
        {
            Matrix4x4 viewMatrix = camera.worldToCameraMatrix;
            Matrix4x4 projMatrix = camera.projectionMatrix;
            int validPointCount = sparseWorldPoints != null ? Mathf.Min(pointCount, sparseWorldPoints.Length) : 0;

            for (int i = 0; i < validPointCount; i++)
            {
                Vector3 vp = viewMatrix.MultiplyPoint3x4(sparseWorldPoints[i]);
                Vector4 clipPos = projMatrix * new Vector4(vp.x, vp.y, vp.z, 1.0f);
                if (clipPos.w <= 0.0f) continue;

                float invW = 1.0f / clipPos.w;
                float ndcX = clipPos.x * invW;
                float ndcY = clipPos.y * invW;
                float ndcZ = clipPos.z * invW;

                if (ndcX < -1f || ndcX > 1f || ndcY < -1f || ndcY > 1f || ndcZ < -1f || ndcZ > 1f)
                    continue;

                int sx = (int)((ndcX * 0.5f + 0.5f) * width);
                int sy = (int)((ndcY * 0.5f + 0.5f) * height);
                if (sx < 0 || sx >= width || sy < 0 || sy >= height)
                    continue;

                float depth = ndcZ * 0.5f + 0.5f;
                uint depthUint = (uint)(Mathf.Clamp01(depth) * (double)DepthMaxUint);
                int pixelIdx = sy * width + sx;
                if (depthUint < depthUintMap[pixelIdx])
                {
                    depthUintMap[pixelIdx] = depthUint;
                    viewPosMap[pixelIdx] = new Vector4(vp.x, vp.y, vp.z, depth);
                    originTypeMap[pixelIdx] = 0; // Physical Point Cloud
                }
            }
        }

        private static void BuildPhysicalDepthPyramid(
            Vector4[] viewPosMap,
            byte[] originTypeMap,
            int width,
            int height,
            bool useTagOpt,
            Vector4[][] pyramid,
            int[] levelWidths,
            int[] levelHeights)
        {
            Vector4 sentinel = new Vector4(0f, 0f, 0f, SentinelDepth);
            pyramid[0] = viewPosMap;
            levelWidths[0] = width;
            levelHeights[0] = height;

            for (int lv = 1; lv <= MaxPyramidLevel; lv++)
            {
                int lw = Mathf.Max(1, width >> lv);
                int lh = Mathf.Max(1, height >> lv);
                levelWidths[lv] = lw;
                levelHeights[lv] = lh;

                Vector4[] currMap = new Vector4[lw * lh];
                pyramid[lv] = currMap;

                int prevW = levelWidths[lv - 1];
                int prevH = levelHeights[lv - 1];
                Vector4[] prevMap = pyramid[lv - 1];

                for (int y = 0; y < lh; y++)
                {
                    int sy0 = y * 2;
                    int sy1 = sy0 + 1;
                    int rowOff = y * lw;

                    for (int x = 0; x < lw; x++)
                    {
                        int sx0 = x * 2;
                        int sx1 = sx0 + 1;

                        if (lv == 1)
                        {
                            Vector4 minP = sentinel;
                            CheckPhysicalL0(prevMap, originTypeMap, prevW, prevH, sx0, sy0, useTagOpt, ref minP);
                            CheckPhysicalL0(prevMap, originTypeMap, prevW, prevH, sx1, sy0, useTagOpt, ref minP);
                            CheckPhysicalL0(prevMap, originTypeMap, prevW, prevH, sx0, sy1, useTagOpt, ref minP);
                            CheckPhysicalL0(prevMap, originTypeMap, prevW, prevH, sx1, sy1, useTagOpt, ref minP);
                            currMap[rowOff + x] = minP;
                        }
                        else
                        {
                            Vector4 minP = SamplePyramid(prevMap, prevW, prevH, sx0, sy0);
                            Vector4 p1 = SamplePyramid(prevMap, prevW, prevH, sx1, sy0);
                            if (p1.w < minP.w) minP = p1;
                            Vector4 p2 = SamplePyramid(prevMap, prevW, prevH, sx0, sy1);
                            if (p2.w < minP.w) minP = p2;
                            Vector4 p3 = SamplePyramid(prevMap, prevW, prevH, sx1, sy1);
                            if (p3.w < minP.w) minP = p3;
                            currMap[rowOff + x] = minP;
                        }
                    }
                }
            }
        }

        private static void CheckPhysicalL0(
            Vector4[] posMap, byte[] typeMap, int w, int h, int x, int y, bool useTagOpt, ref Vector4 minP)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int idx = y * w + x;
            if (useTagOpt && typeMap[idx] != 0) return;
            Vector4 p = posMap[idx];
            if (p.w < minP.w) minP = p;
        }

        private static Vector4 SamplePyramid(Vector4[] map, int w, int h, int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h)
            {
                return new Vector4(0f, 0f, 0f, SentinelDepth);
            }
            return map[y * w + x];
        }

        private static void EvaluateMinSectorOcclusion(
            Vector4[] viewPosMap,
            byte[] originTypeMap,
            Vector4[][] pyramid,
            int[] levelWidths,
            int[] levelHeights,
            byte[] searchLevels,
            PCDRendererFeature.PCDRenderSettings settings,
            int width,
            int height,
            out int[] evalIndices,
            out float[] evalMinSectors)
        {
            int totalPixels = width * height;
            var evalIndicesList = new List<int>(totalPixels / 4);
            for (int i = 0; i < totalPixels; i++)
            {
                if (originTypeMap[i] == 1 && viewPosMap[i].w < SentinelDepth)
                {
                    evalIndicesList.Add(i);
                }
            }

            int evalCount = evalIndicesList.Count;
            evalIndices = evalIndicesList.ToArray();
            evalMinSectors = new float[evalCount * 8];

            bool useTagOpt = settings.enableTagBasedOptimization;
            int kernelType = (int)settings.kernelType;
            float alphaParam = settings.exponentAlpha;

            for (int idx = 0; idx < evalCount; idx++)
            {
                int pixelIdx = evalIndices[idx];
                int py = pixelIdx / width;
                int px = pixelIdx - py * width;

                Vector4 currentPos = viewPosMap[pixelIdx];
                Vector3 xVec = new Vector3(currentPos.x, currentPos.y, currentPos.z);
                float currentPosSq = Vector3.Dot(xVec, xVec);
                float lenX = Mathf.Sqrt(currentPosSq);
                float invCurrentPosSq = 1.0f / Mathf.Max(currentPosSq, 0.0001f);

                int level = searchLevels[pixelIdx];
                int baseOut = idx * 8;
                for (int s = 0; s < 8; s++) evalMinSectors[baseOut + s] = 2.0f;

                for (int lv = 0; lv <= level; lv++)
                {
                    int lw = levelWidths[lv];
                    int lh = levelHeights[lv];
                    Vector4[] lvMap = pyramid[lv];
                    int baseX = px >> lv;
                    int baseY = py >> lv;

                    for (int s = 0; s < 8; s++)
                    {
                        Vector2Int offset = SectorOffsets[s];
                        int tx = lv == 0 ? px + offset.x : baseX + offset.x;
                        int ty = lv == 0 ? py + offset.y : baseY + offset.y;
                        if (tx < 0 || ty < 0 || tx >= lw || ty >= lh) continue;

                        int targetIdx = ty * lw + tx;
                        if (lv == 0 && useTagOpt && originTypeMap[targetIdx] != 0) continue;

                        Vector4 neighborPos = lvMap[targetIdx];
                        if (neighborPos.w < SentinelDepth && (currentPos.w - neighborPos.w) > DepthEpsilon)
                        {
                            Vector3 yVec = new Vector3(neighborPos.x, neighborPos.y, neighborPos.z);
                            float occVal = ComputeSingleOcclusionValue(kernelType, alphaParam, xVec, lenX, invCurrentPosSq, yVec);
                            if (occVal < evalMinSectors[baseOut + s]) evalMinSectors[baseOut + s] = occVal;
                        }
                    }
                }
            }
        }

        private static float ComputeSingleOcclusionValue(
            int kernelType, float alphaParam, Vector3 x, float lenX, float invCurrentPosSq, Vector3 y)
        {
            if (kernelType == 0) // Pintus Operator (UI: Bouchiba)
            {
                Vector3 yMinusX = y - x;
                float lenYMinusX = yMinusX.magnitude;
                if (lenYMinusX > 0.0001f && lenX > 0.0001f)
                {
                    float dotP = Mathf.Clamp01(Vector3.Dot(yMinusX / lenYMinusX, -x / lenX));
                    float val = 1.0f - dotP;
                    return val > 0.0f ? Mathf.Clamp01(Mathf.Max(1e-7f, val)) : 0.0f;
                }
                return 0.0f;
            }
            if (kernelType == 4) // DepthOnly
            {
                return 0.0f;
            }

            float sqY = Vector3.Dot(y, y);
            float dotXY = Vector3.Dot(x, y);
            float dOrthoSq = Mathf.Max(0.0f, sqY - dotXY * dotXY * invCurrentPosSq);
            float expVal = 1.0f - Mathf.Exp(-alphaParam * dOrthoSq);
            return expVal > 0.0f ? Mathf.Clamp01(Mathf.Max(1e-7f, expVal)) : 0.0f;
        }
    }
}
