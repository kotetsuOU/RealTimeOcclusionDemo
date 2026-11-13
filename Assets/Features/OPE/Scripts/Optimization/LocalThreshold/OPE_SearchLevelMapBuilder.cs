using System;
using UnityEngine;

namespace OPE
{
    /// <summary>
    /// CalculateGridZMin -> CalculateDensity -> GridMedianFilter -> ApplyAdaptiveGradientCorrection を
    /// Pure C# 上でシェーダーと同一アルゴリズムで再現し、ピクセル単位の近傍探索レベル (0..6) を構築するビルダー。
    /// </summary>
    public static class OPE_SearchLevelMapBuilder
    {
        private const int MaxPyramidLevel = 6;
        private const uint DepthMaxUint = 0xFFFFFFFFu;
        private const float SobelSentinelThreshold = 1e8f;

        /// <summary>
        /// ピクセルごとの探索ピラミッドレベルマップ (0..6) を算出します。
        /// </summary>
        public static byte[] Build(
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
            int[,] filteredGridLevels = BuildFilteredGridLevels(
                depthUintMap, originTypeMap, settings, width, height, gridSize, gridW, gridH, minSearchLevel);

            ExpandAndApplyGradientCorrection(
                resultLevels, originTypeMap, pyramid, levelWidths, levelHeights,
                filteredGridLevels, settings, width, height, gridSize, minSearchLevel);

            return resultLevels;
        }

        private static int[,] BuildFilteredGridLevels(
            uint[] depthUintMap,
            byte[] originTypeMap,
            PCDRendererFeature.PCDRenderSettings settings,
            int width,
            int height,
            int gridSize,
            int gridW,
            int gridH,
            int minSearchLevel)
        {
            int[,] filteredGridLevels = new int[gridW, gridH];
            if (!settings.enableDensityBasedLOD)
            {
                for (int gy = 0; gy < gridH; gy++)
                    for (int gx = 0; gx < gridW; gx++)
                        filteredGridLevels[gx, gy] = minSearchLevel;
                return filteredGridLevels;
            }

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

                    uint zMin = FindGridMinDepth(depthUintMap, width, xStart, xEnd, yStart, yEnd);
                    uint pointCount = CountGridPoints(
                        depthUintMap, originTypeMap, width, xStart, xEnd, yStart, yEnd, zMin, thresholdUint, typeAware);

                    float density = pointCount / gridArea;
                    int level = minSearchLevel;
                    if (density > minDensityThreshold)
                    {
                        float lRadius = pPrime / Mathf.Sqrt(density);
                        int calcLv = Mathf.FloorToInt(Mathf.Log(lRadius, 2f));
                        level = Mathf.Min(MaxPyramidLevel, calcLv);
                    }
                    rawGridLevels[gx, gy] = Mathf.Max(minSearchLevel, level);
                }
            }

            ApplyGridMedianFilter3x3(rawGridLevels, filteredGridLevels, gridW, gridH);
            return filteredGridLevels;
        }

        private static uint FindGridMinDepth(uint[] depthUintMap, int width, int xStart, int xEnd, int yStart, int yEnd)
        {
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
            return zMin;
        }

        private static uint CountGridPoints(
            uint[] depthUintMap,
            byte[] originTypeMap,
            int width,
            int xStart,
            int xEnd,
            int yStart,
            int yEnd,
            uint zMin,
            uint thresholdUint,
            bool typeAware)
        {
            if (zMin >= DepthMaxUint) return 0;

            uint pointCountInGrid = 0;
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
            return pointCountInGrid;
        }

        private static void ApplyGridMedianFilter3x3(int[,] src, int[,] dst, int gridW, int gridH)
        {
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
                            neighborVals[k++] = src[cx, cy];
                        }
                    }
                    Array.Sort(neighborVals);
                    dst[gx, gy] = neighborVals[4];
                }
            }
        }

        private static void ExpandAndApplyGradientCorrection(
            byte[] resultLevels,
            byte[] originTypeMap,
            Vector4[][] pyramid,
            int[] levelWidths,
            int[] levelHeights,
            int[,] filteredGridLevels,
            PCDRendererFeature.PCDRenderSettings settings,
            int width,
            int height,
            int gridSize,
            int minSearchLevel)
        {
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
                        float grad = ComputeSobelOnPyramid(pyramid[l], levelWidths[l], levelHeights[l], px >> l, py >> l);
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
        }

        private static float ComputeSobelOnPyramid(Vector4[] pyrTex, int w, int h, int x, int y)
        {
            float tl = pyrTex[Mathf.Clamp(y - 1, 0, h - 1) * w + Mathf.Clamp(x - 1, 0, w - 1)].w;
            if (tl > SobelSentinelThreshold) return 0f;
            float t  = pyrTex[Mathf.Clamp(y - 1, 0, h - 1) * w + Mathf.Clamp(x,     0, w - 1)].w;
            if (t  > SobelSentinelThreshold) return 0f;
            float tr = pyrTex[Mathf.Clamp(y - 1, 0, h - 1) * w + Mathf.Clamp(x + 1, 0, w - 1)].w;
            if (tr > SobelSentinelThreshold) return 0f;
            float l  = pyrTex[Mathf.Clamp(y,     0, h - 1) * w + Mathf.Clamp(x - 1, 0, w - 1)].w;
            if (l  > SobelSentinelThreshold) return 0f;
            float r  = pyrTex[Mathf.Clamp(y,     0, h - 1) * w + Mathf.Clamp(x + 1, 0, w - 1)].w;
            if (r  > SobelSentinelThreshold) return 0f;
            float bl = pyrTex[Mathf.Clamp(y + 1, 0, h - 1) * w + Mathf.Clamp(x - 1, 0, w - 1)].w;
            if (bl > SobelSentinelThreshold) return 0f;
            float b  = pyrTex[Mathf.Clamp(y + 1, 0, h - 1) * w + Mathf.Clamp(x,     0, w - 1)].w;
            if (b  > SobelSentinelThreshold) return 0f;
            float br = pyrTex[Mathf.Clamp(y + 1, 0, h - 1) * w + Mathf.Clamp(x + 1, 0, w - 1)].w;
            if (br > SobelSentinelThreshold) return 0f;

            float gx = (tr + 2.0f * r + br) - (tl + 2.0f * l + bl);
            float gy = (bl + 2.0f * b + br) - (tl + 2.0f * t + tr);
            return Mathf.Sqrt(gx * gx + gy * gy);
        }
    }
}

