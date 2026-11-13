using System;
using UnityEngine;

namespace OPE
{
    /// <summary>
    /// 疎・ノイズあり実測点群に対するオクルージョンパイプライン（投影、密度LOD、深度ピラミッド構築、
    /// 勾配補正、8方向セクター最小局所評価値 minSectorOcclusion[0..7] の算出）を
    /// シェーダー (PCD_Occlusion_Kernels_*.hlsl) と 100% 同一のアルゴリズムで構築・計算する Pure C# クラス。
    /// T_loc が最終比較ステップでのみ作用する性質を利用し、101 ステップの T_loc スイープを
    /// GPU/カメラ待機 0 フレーム（数ミリ秒）で超高速かつ厳密に評価します。
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
            new Vector2Int( 1,  1), // 2->3: South-East
            new Vector2Int( 0,  1), // 4: South
            new Vector2Int(-1,  1), // 5: South-West
            new Vector2Int(-1,  0), // 6: West
            new Vector2Int(-1, -1)  // 7: North-West
        };

        /// <summary>
        /// 単眼カメラに対する疎な実測点群の評価結果キャッシュ。
        /// 各仮想物体ピクセルの 8方向最小局所評価値とパラメータ依存探索レベルを保持します。
        /// </summary>
        public class EyeEvaluationCache
        {
            public int width;
            public int height;

            /// <summary> パラメータ依存で決定された各ピクセルの探索レベル (0..6) </summary>
            public byte[] searchLevels;

            /// <summary> 評価対象ピクセル（仮想物体領域かつ手前点群に直接覆われていない画素）のインデックス配列 </summary>
            public int[] evaluatedPixelIndices;

            /// <summary>
            /// evaluatedPixelIndices の各要素に対応する 8方向の最小局所評価値。
            /// 長さは evaluatedPixelIndices.Length * 8。未ヒット方向は 2.0f。
            /// </summary>
            public float[] evaluatedMinSectorOcclusion;

            /// <summary>
            /// 指定した T_loc における全ピクセルの NeighborCountMap 互換パック配列 (uint[width * height]) を生成します。
            /// 画像出力やデバッグ検証用。
            /// </summary>
            public uint[] BuildPackedTestDataForThreshold(float tLoc)
            {
                uint[] data = new uint[width * height];
                int evalCount = evaluatedPixelIndices != null ? evaluatedPixelIndices.Length : 0;

                for (int i = 0; i < evalCount; i++)
                {
                    int pixelIdx = evaluatedPixelIndices[i];
                    int baseOffset = i * 8;

                    uint occupiedMask = 0u;
                    uint validMask = 0u;
                    uint binaryCount = 0u;
                    uint validCount = 0u;

                    for (int s = 0; s < 8; s++)
                    {
                        float sVal = evaluatedMinSectorOcclusion[baseOffset + s];
                        if (sVal < 1.999f)
                        {
                            validCount++;
                            validMask |= (1u << s);
                        }
                        if (sVal < tLoc)
                        {
                            binaryCount++;
                            occupiedMask |= (1u << s);
                        }
                    }

                    uint packed = (occupiedMask & 0xFFu)
                                | ((validCount & 0x0Fu) << 8)
                                | (1u << 12) // isEvaluated = 1
                                | ((validMask & 0xFFu) << 16)
                                | ((binaryCount & 0x0Fu) << 28);

                    data[pixelIdx] = packed;
                }

                return data;
            }

            /// <summary>
            /// 8セクター個別の閾値ベクトル sectorTLoc (サイズ8) における全ピクセルの
            /// NeighborCountMap 互換パック配列 (uint[width * height]) を生成します。
            /// 画像出力やデバッグ検証用。
            /// </summary>
            public uint[] BuildPackedTestDataForThreshold(float[] sectorTLoc)
            {
                uint[] data = new uint[width * height];
                int evalCount = evaluatedPixelIndices != null ? evaluatedPixelIndices.Length : 0;

                for (int i = 0; i < evalCount; i++)
                {
                    int pixelIdx = evaluatedPixelIndices[i];
                    int baseOffset = i * 8;

                    uint occupiedMask = 0u;
                    uint validMask = 0u;
                    uint binaryCount = 0u;
                    uint validCount = 0u;

                    for (int s = 0; s < 8; s++)
                    {
                        float sVal = evaluatedMinSectorOcclusion[baseOffset + s];
                        float sTh = (sectorTLoc != null && sectorTLoc.Length > s) ? sectorTLoc[s] : 0.5f;

                        if (sVal < 1.999f)
                        {
                            validCount++;
                            validMask |= (1u << s);
                        }
                        if (sVal < sTh)
                        {
                            binaryCount++;
                            occupiedMask |= (1u << s);
                        }
                    }

                    uint packed = (occupiedMask & 0xFFu)
                                | ((validCount & 0x0Fu) << 8)
                                | (1u << 12) // isEvaluated = 1
                                | ((validMask & 0xFFu) << 16)
                                | ((binaryCount & 0x0Fu) << 28);

                    data[pixelIdx] = packed;
                }

                return data;
            }
        }

        /// <summary>
        /// 疎・ノイズあり実測点群と仮想物体マップから、パイプラインの深度ピラミッド・探索半径・
        /// 各セクターの最小局所評価値 (minSectorOcclusion[0..7]) を一括構築します。
        /// </summary>
        public static EyeEvaluationCache BuildEyeEvaluationCache(
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
            Vector4 sentinel = new Vector4(0f, 0f, 0f, SentinelDepth);

            // 1. 仮想物体マップから初期 ViewPositionMap / OriginTypeMap / DepthMap を構築 (InitFromCamera 相当)
            Vector4[] viewPosMap = new Vector4[totalPixels];
            byte[] originTypeMap = new byte[totalPixels]; // 0: Physical, 1: Virtual, 2: Background
            uint[] depthUintMap = new uint[totalPixels];

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

            // 2. 実測点群をスクリーン空間へ投影 (ProjectPoints カーネルと完全一致)
            Matrix4x4 viewMatrix = camera.worldToCameraMatrix;
            Matrix4x4 projMatrix = camera.projectionMatrix;

            int validPointCount = (sparseWorldPoints != null) ? Mathf.Min(pointCount, sparseWorldPoints.Length) : 0;
            for (int i = 0; i < validPointCount; i++)
            {
                Vector3 wp = sparseWorldPoints[i];
                Vector3 vp = viewMatrix.MultiplyPoint3x4(wp);
                Vector4 clipPos = projMatrix * new Vector4(vp.x, vp.y, vp.z, 1.0f);

                if (clipPos.w <= 0.0f) continue;

                float invW = 1.0f / clipPos.w;
                float ndcX = clipPos.x * invW;
                float ndcY = clipPos.y * invW;
                float ndcZ = clipPos.z * invW;

                // OpenGL プロジェクションの NDC [-1, 1] 判定
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

            // 3. 物理点群の深度ピラミッド L1〜L6 の構築 (BuildDepthPyramidL1〜L6 と完全一致)
            Vector4[][] pyramid = new Vector4[MaxPyramidLevel + 1][];
            int[] levelWidths = new int[MaxPyramidLevel + 1];
            int[] levelHeights = new int[MaxPyramidLevel + 1];

            pyramid[0] = viewPosMap;
            levelWidths[0] = width;
            levelHeights[0] = height;

            bool useTagOpt = settings.enableTagBasedOptimization;

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

                if (lv == 1)
                {
                    // L1: ZMinPhysicalDownsample (OriginType == 0 のみを抽出)
                    for (int y = 0; y < lh; y++)
                    {
                        int sy0 = y * 2;
                        int sy1 = sy0 + 1;
                        int rowOff = y * lw;

                        for (int x = 0; x < lw; x++)
                        {
                            int sx0 = x * 2;
                            int sx1 = sx0 + 1;

                            Vector4 minP = sentinel;
                            CheckPhysicalL0(prevMap, originTypeMap, prevW, prevH, sx0, sy0, useTagOpt, ref minP);
                            CheckPhysicalL0(prevMap, originTypeMap, prevW, prevH, sx1, sy0, useTagOpt, ref minP);
                            CheckPhysicalL0(prevMap, originTypeMap, prevW, prevH, sx0, sy1, useTagOpt, ref minP);
                            CheckPhysicalL0(prevMap, originTypeMap, prevW, prevH, sx1, sy1, useTagOpt, ref minP);

                            currMap[rowOff + x] = minP;
                        }
                    }
                }
                else
                {
                    // L2〜L6: ZMinPositionDownsample
                    for (int y = 0; y < lh; y++)
                    {
                        int sy0 = y * 2;
                        int sy1 = sy0 + 1;
                        int rowOff = y * lw;

                        for (int x = 0; x < lw; x++)
                        {
                            int sx0 = x * 2;
                            int sx1 = sx0 + 1;

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

            // 4. パラメータ依存の探索半径 (CorrectedNeighborhoodSizeMap) の計算
            byte[] searchLevels = ComputeSearchLevelMap(
                depthUintMap,
                originTypeMap,
                pyramid,
                levelWidths,
                levelHeights,
                settings,
                width,
                height);

            // 5. 各評価対象ピクセルについて 8方向の最小局所評価値 minSectorOcclusion[0..7] を計算
            var evalIndicesList = new System.Collections.Generic.List<int>(totalPixels / 4);
            for (int i = 0; i < totalPixels; i++)
            {
                if (originTypeMap[i] == 1 && viewPosMap[i].w < SentinelDepth)
                {
                    evalIndicesList.Add(i);
                }
            }

            int evalCount = evalIndicesList.Count;
            int[] evalIndices = evalIndicesList.ToArray();
            float[] evalMinSectors = new float[evalCount * 8];

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

                for (int s = 0; s < 8; s++)
                {
                    evalMinSectors[baseOut + s] = 2.0f;
                }

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
                        int tx = (lv == 0) ? (px + offset.x) : (baseX + offset.x);
                        int ty = (lv == 0) ? (py + offset.y) : (baseY + offset.y);

                        if (tx < 0 || ty < 0 || tx >= lw || ty >= lh)
                            continue;

                        int targetIdx = ty * lw + tx;
                        if (lv == 0 && useTagOpt && originTypeMap[targetIdx] != 0)
                            continue;

                        Vector4 neighborPos = lvMap[targetIdx];
                        if (neighborPos.w < SentinelDepth && (currentPos.w - neighborPos.w) > DepthEpsilon)
                        {
                            Vector3 yVec = new Vector3(neighborPos.x, neighborPos.y, neighborPos.z);
                            float occVal = ComputeSingleOcclusionValue(
                                kernelType, alphaParam, xVec, lenX, currentPosSq, invCurrentPosSq, yVec);

                            if (occVal < evalMinSectors[baseOut + s])
                            {
                                evalMinSectors[baseOut + s] = occVal;
                            }
                        }
                    }
                }
            }

            return new EyeEvaluationCache
            {
                width = width,
                height = height,
                searchLevels = searchLevels,
                evaluatedPixelIndices = evalIndices,
                evaluatedMinSectorOcclusion = evalMinSectors
            };
        }

        private static void CheckPhysicalL0(
            Vector4[] posMap,
            byte[] typeMap,
            int w,
            int h,
            int x,
            int y,
            bool useTagOpt,
            ref Vector4 minP)
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

        /// <summary>
        /// CalculateGridZMin -> CalculateDensity -> CalculateGridLevel -> GridMedianFilter -> ApplyAdaptiveGradientCorrection
        /// を忠実に実行し、ピクセル単位の探索レベル (0..6) を算出します。
        /// </summary>
        private static byte[] ComputeSearchLevelMap(
            uint[] depthUintMap,
            byte[] originTypeMap,
            Vector4[][] pyramid,
            int[] levelWidths,
            int[] levelHeights,
            PCDRendererFeature.PCDRenderSettings settings,
            int width,
            int height)
        {
            int totalPixels = width * height;
            byte[] resultLevels = new byte[totalPixels];
            int minSearchLevel = Mathf.Clamp(settings.minSearchLevel, 0, MaxPyramidLevel);

            if (!settings.enableDensityBasedLOD && !settings.enableGradientCorrection)
            {
                byte fixedLv = (byte)minSearchLevel;
                for (int i = 0; i < totalPixels; i++) resultLevels[i] = fixedLv;
                return resultLevels;
            }

            int gridSize = (int)settings.gridSize;
            if (gridSize <= 0) gridSize = 16;

            int gridW = (width + gridSize - 1) / gridSize;
            int gridH = (height + gridSize - 1) / gridSize;
            int[,] filteredGridLevels = new int[gridW, gridH];

            if (settings.enableDensityBasedLOD)
            {
                int[,] rawGridLevels = new int[gridW, gridH];
                uint thresholdUint = (uint)(Mathf.Clamp01(settings.densityThreshold_e) * (double)DepthMaxUint);
                bool typeAware = settings.enableTypeAwareDensity;
                float gridArea = (float)(gridSize * gridSize);
                float minDensityThreshold = 0.5f / gridArea;
                float pPrime = settings.neighborhoodParam_p_prime;

                for (int gy = 0; gy < gridH; gy++)
                {
                    int yStart = gy * gridSize;
                    int yEnd = Mathf.Min(height, yStart + gridSize);

                    for (int gx = 0; gx < gridW; gx++)
                    {
                        int xStart = gx * gridSize;
                        int xEnd = Mathf.Min(width, xStart + gridSize);

                        // 1. Grid Z-Min
                        uint zMin = DepthMaxUint;
                        for (int py = yStart; py < yEnd; py++)
                        {
                            int rowOff = py * width;
                            for (int px = xStart; px < xEnd; px++)
                            {
                                uint d = depthUintMap[rowOff + px];
                                if (d < zMin) zMin = d;
                            }
                        }

                        // 2. Grid Density
                        uint pointCountInGrid = 0;
                        if (zMin < DepthMaxUint)
                        {
                            for (int py = yStart; py < yEnd; py++)
                            {
                                int rowOff = py * width;
                                for (int px = xStart; px < xEnd; px++)
                                {
                                    int idx = rowOff + px;
                                    uint d = depthUintMap[idx];
                                    if (d < DepthMaxUint && (d - zMin) < thresholdUint)
                                    {
                                        if (!typeAware || originTypeMap[idx] == 0)
                                        {
                                            pointCountInGrid++;
                                        }
                                    }
                                }
                            }
                        }

                        float density = pointCountInGrid / gridArea;
                        int level = minSearchLevel;
                        if (density > minDensityThreshold)
                        {
                            float L = pPrime / Mathf.Sqrt(density);
                            int calcLv = Mathf.FloorToInt(Mathf.Log(L, 2f));
                            level = Mathf.Min(MaxPyramidLevel, calcLv);
                        }
                        rawGridLevels[gx, gy] = Mathf.Max(minSearchLevel, level);
                    }
                }

                // 3. 3x3 Grid Median Filter
                int[] neighborVals = new int[9];
                for (int gy = 0; gy < gridH; gy++)
                {
                    for (int gx = 0; gx < gridW; gx++)
                    {
                        int k = 0;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int cy = Mathf.Clamp(gy + dy, 0, gridH - 1);
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int cx = Mathf.Clamp(gx + dx, 0, gridW - 1);
                                neighborVals[k++] = rawGridLevels[cx, cy];
                            }
                        }
                        Array.Sort(neighborVals);
                        filteredGridLevels[gx, gy] = neighborVals[4];
                    }
                }
            }
            else
            {
                for (int gy = 0; gy < gridH; gy++)
                {
                    for (int gx = 0; gx < gridW; gx++)
                    {
                        filteredGridLevels[gx, gy] = minSearchLevel;
                    }
                }
            }

            // 4. ピクセル単位への展開 & 適応的勾配補正 (ApplyAdaptiveGradientCorrection)
            bool useGrad = settings.enableGradientCorrection;
            float gTh = settings.gradientThreshold_g_th;

            for (int py = 0; py < height; py++)
            {
                int gy = py / gridSize;
                int rowOff = py * width;

                for (int px = 0; px < width; px++)
                {
                    int gx = px / gridSize;
                    int idx = rowOff + px;
                    int baseLv = Mathf.Max(minSearchLevel, filteredGridLevels[gx, gy]);

                    if (!useGrad || originTypeMap[idx] != 1)
                    {
                        resultLevels[idx] = (byte)Mathf.Clamp(baseLv, 0, MaxPyramidLevel);
                        continue;
                    }

                    int correctedLv = baseLv;
                    for (int l = baseLv; l > 0; l--)
                    {
                        int uvX = px >> l;
                        int uvY = py >> l;
                        float grad = ComputeSobelOnPyramid(pyramid[l], levelWidths[l], levelHeights[l], uvX, uvY);
                        if (grad > gTh)
                        {
                            correctedLv = l - 1;
                        }
                        else
                        {
                            break;
                        }
                    }

                    resultLevels[idx] = (byte)Mathf.Clamp(correctedLv, 0, MaxPyramidLevel);
                }
            }

            return resultLevels;
        }

        private static float ComputeSobelOnPyramid(Vector4[] pyrTex, int w, int h, int x, int y)
        {
            const float SentinelThreshold = 1e8f;

            float tl = pyrTex[Mathf.Clamp(y - 1, 0, h - 1) * w + Mathf.Clamp(x - 1, 0, w - 1)].w;
            if (tl > SentinelThreshold) return 0f;
            float t  = pyrTex[Mathf.Clamp(y - 1, 0, h - 1) * w + Mathf.Clamp(x,     0, w - 1)].w;
            if (t  > SentinelThreshold) return 0f;
            float tr = pyrTex[Mathf.Clamp(y - 1, 0, h - 1) * w + Mathf.Clamp(x + 1, 0, w - 1)].w;
            if (tr > SentinelThreshold) return 0f;
            float l  = pyrTex[Mathf.Clamp(y,     0, h - 1) * w + Mathf.Clamp(x - 1, 0, w - 1)].w;
            if (l  > SentinelThreshold) return 0f;
            float r  = pyrTex[Mathf.Clamp(y,     0, h - 1) * w + Mathf.Clamp(x + 1, 0, w - 1)].w;
            if (r  > SentinelThreshold) return 0f;
            float bl = pyrTex[Mathf.Clamp(y + 1, 0, h - 1) * w + Mathf.Clamp(x - 1, 0, w - 1)].w;
            if (bl > SentinelThreshold) return 0f;
            float b  = pyrTex[Mathf.Clamp(y + 1, 0, h - 1) * w + Mathf.Clamp(x,     0, w - 1)].w;
            if (b  > SentinelThreshold) return 0f;
            float br = pyrTex[Mathf.Clamp(y + 1, 0, h - 1) * w + Mathf.Clamp(x + 1, 0, w - 1)].w;
            if (br > SentinelThreshold) return 0f;

            float gx = (tr + 2.0f * r + br) - (tl + 2.0f * l + bl);
            float gy = (bl + 2.0f * b + br) - (tl + 2.0f * t + tr);
            return Mathf.Sqrt(gx * gx + gy * gy);
        }

        private static float ComputeSingleOcclusionValue(
            int kernelType,
            float alphaParam,
            Vector3 x,
            float lenX,
            float currentPosSq,
            float invCurrentPosSq,
            Vector3 y)
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
            else if (kernelType == 4) // DepthOnly
            {
                return 0.0f;
            }
            else // Exponential (1) or Linear (2)
            {
                float sqY = Vector3.Dot(y, y);
                float dotP = Vector3.Dot(x, y);
                float dOrthoSq = Mathf.Max(0.0f, sqY - (dotP * dotP * invCurrentPosSq));
                float val = 1.0f - Mathf.Exp(-alphaParam * dOrthoSq);
                return val > 0.0f ? Mathf.Clamp01(Mathf.Max(1e-7f, val)) : 0.0f;
            }
        }
    }
}

