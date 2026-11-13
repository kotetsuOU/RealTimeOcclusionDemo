using UnityEngine;

namespace OPE
{
    /// <summary>
    /// 全ピクセルが埋まった理想深度マップ (L0) から 6 階層 (L1〜L6) の深度ピラミッドを再構築し、
    /// パラメータ依存の探索半径における 8 方向セクター別の理論正解 (Ground Truth) 二値マスクを導出する Pure C# ビルダークラス。
    /// </summary>
    public static class OPE_IdealDepthPyramidBuilder
    {
        public const int MaxPyramidLevel = OPE_IdealPyramidData.MaxPyramidLevel;
        public const float SentinelDepth = OPE_IdealPyramidData.SentinelDepth;
        public const float DepthEpsilon = 1e-5f;

        /// <summary>
        /// 8 方向のサンプリングオフセット（PCD_Occlusion_Kernels_Occlusion.hlsl と完全一致）
        /// 0=North, 1=North-East, 2=East, 3=South-East, 4=South, 5=South-West, 6=West, 7=North-West
        /// </summary>
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
        /// 指定されたカメラ・手メッシュ・仮想物体から、全ピクセル交差判定と 6 階層深度ピラミッドの再構築を実行します。
        /// </summary>
        public static OPE_IdealPyramidData BuildIdealPyramidForCamera(
            Camera camera,
            GameObject handRoot,
            GameObject virtualObjectRoot,
            int screenWidth,
            int screenHeight)
        {
            if (camera == null || screenWidth <= 0 || screenHeight <= 0)
            {
                return null;
            }

            var data = new OPE_IdealPyramidData
            {
                width = screenWidth,
                height = screenHeight
            };

            for (int lv = 0; lv <= MaxPyramidLevel; lv++)
            {
                int lw = Mathf.Max(1, screenWidth >> lv);
                int lh = Mathf.Max(1, screenHeight >> lv);
                data.levelWidths[lv] = lw;
                data.levelHeights[lv] = lh;
                data.handPyramid[lv] = CreateSentinelArray(lw * lh);
            }

            data.virtualObjectMap = CreateSentinelArray(screenWidth * screenHeight);

            Matrix4x4 viewMatrix = camera.worldToCameraMatrix;
            Matrix4x4 projMatrix = camera.projectionMatrix;

            OPE_MeshFirstHitRasterizer.RasterizeGameObjectFirstHitToL0(
                handRoot, viewMatrix, projMatrix, screenWidth, screenHeight, data.handPyramid[0]);

            OPE_MeshFirstHitRasterizer.RasterizeGameObjectFirstHitToL0(
                virtualObjectRoot, viewMatrix, projMatrix, screenWidth, screenHeight, data.virtualObjectMap);

            BuildDownsampledPyramidLevels(data);
            return data;
        }

        /// <summary>
        /// 構築済みの <see cref="OPE_IdealPyramidData"/> と各ピクセルの探索レベルから、
        /// 8 方向それぞれの理論正解二値マスク (bit 0..7) および評価対象フラグ (bit 12) を格納した uint[] を生成します。
        /// </summary>
        public static uint[] ComputeTheoreticalGroundTruthMasks(
            OPE_IdealPyramidData pyramid,
            byte[] searchLevels,
            int defaultSearchLevel = 2)
        {
            if (pyramid == null) return null;

            int w = pyramid.width;
            int h = pyramid.height;
            int totalPixels = w * h;
            uint[] gtMaskBuffer = new uint[totalPixels];

            bool hasSearchLevelMap = searchLevels != null && searchLevels.Length == totalPixels;
            int fallbackLevel = Mathf.Clamp(defaultSearchLevel, 0, MaxPyramidLevel);
            Vector4[] voMap = pyramid.virtualObjectMap;

            for (int y = 0; y < h; y++)
            {
                int rowOffset = y * w;
                for (int x = 0; x < w; x++)
                {
                    int idx = rowOffset + x;
                    Vector4 voPos = voMap[idx];
                    if (voPos.w >= SentinelDepth)
                    {
                        gtMaskBuffer[idx] = 0u;
                        continue;
                    }

                    int level = hasSearchLevelMap
                        ? Mathf.Clamp((int)searchLevels[idx], 0, MaxPyramidLevel)
                        : fallbackLevel;

                    uint gtSectorBits = EvaluatePixelGroundTruthBits(pyramid, x, y, level, voPos.w);
                    gtMaskBuffer[idx] = (gtSectorBits & 0xFFu)
                                      | (1u << 12)
                                      | ((gtSectorBits & 0xFFu) << 16);
                }
            }

            return gtMaskBuffer;
        }

        private static uint EvaluatePixelGroundTruthBits(
            OPE_IdealPyramidData pyramid, int x, int y, int level, float voDepth)
        {
            uint gtSectorBits = 0u;
            for (int s = 0; s < 8; s++)
            {
                Vector2Int offset = SectorOffsets[s];
                for (int lv = 0; lv <= level; lv++)
                {
                    Vector4 neighbor = pyramid.FetchHandPyramidPosition(lv, x, y, offset);
                    if (neighbor.w < SentinelDepth && (voDepth - neighbor.w) > DepthEpsilon)
                    {
                        gtSectorBits |= (1u << s);
                        break;
                    }
                }
            }
            return gtSectorBits;
        }

        private static void BuildDownsampledPyramidLevels(OPE_IdealPyramidData data)
        {
            for (int lv = 1; lv <= MaxPyramidLevel; lv++)
            {
                Vector4[] prevMap = data.handPyramid[lv - 1];
                int prevW = data.levelWidths[lv - 1];
                int prevH = data.levelHeights[lv - 1];

                Vector4[] currMap = data.handPyramid[lv];
                int currW = data.levelWidths[lv];
                int currH = data.levelHeights[lv];

                for (int y = 0; y < currH; y++)
                {
                    int srcY0 = y * 2;
                    int srcY1 = srcY0 + 1;
                    int rowOffset = y * currW;

                    for (int x = 0; x < currW; x++)
                    {
                        int srcX0 = x * 2;
                        int srcX1 = srcX0 + 1;

                        Vector4 minP = SampleWithBounds(prevMap, prevW, prevH, srcX0, srcY0);
                        Vector4 p1 = SampleWithBounds(prevMap, prevW, prevH, srcX1, srcY0);
                        if (p1.w < minP.w) minP = p1;
                        Vector4 p2 = SampleWithBounds(prevMap, prevW, prevH, srcX0, srcY1);
                        if (p2.w < minP.w) minP = p2;
                        Vector4 p3 = SampleWithBounds(prevMap, prevW, prevH, srcX1, srcY1);
                        if (p3.w < minP.w) minP = p3;

                        currMap[rowOffset + x] = minP;
                    }
                }
            }
        }

        private static Vector4[] CreateSentinelArray(int length)
        {
            var arr = new Vector4[length];
            Vector4 sentinel = new Vector4(0f, 0f, 0f, SentinelDepth);
            for (int i = 0; i < length; i++) arr[i] = sentinel;
            return arr;
        }

        private static Vector4 SampleWithBounds(Vector4[] map, int w, int h, int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h)
            {
                return new Vector4(0f, 0f, 0f, SentinelDepth);
            }
            return map[y * w + x];
        }
    }
}
