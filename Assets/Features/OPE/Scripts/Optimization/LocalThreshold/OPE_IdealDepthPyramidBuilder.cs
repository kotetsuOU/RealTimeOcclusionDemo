using System;
using System.Collections.Generic;
using UnityEngine;

namespace OPE
{
    /// <summary>
    /// 手メッシュ (groundTruthObject) および仮想物体 (virtualObject) から、
    /// 「各ピクセルの中心とカメラ視点を結ぶ直線がメッシュと初めて交差する点」を仮想ダミー実測点として計算し、
    /// 全ピクセルが埋まった理想深度マップ (L0) から 6階層 (L1〜L6) の深度ピラミッドを独自に再構築する Pure C# ビルダークラス。
    /// さらに、再構築した理想深度ピラミッドを用いて、パラメータ依存の探索半径 (level) における
    /// 8方向セクター別の理論正解 (Ground Truth) 二値マスクを導出します。
    /// </summary>
    public static class OPE_IdealDepthPyramidBuilder
    {
        public const int MaxPyramidLevel = 6;
        public const float SentinelDepth = 1e9f;
        public const float DepthEpsilon = 1e-5f;

        /// <summary>
        /// 8方向のサンプリングオフセット（PCD_Occlusion_Kernels_Occlusion.hlsl と完全一致）
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
        /// 単眼カメラに対する理想深度ピラミッド (L0〜L6) および仮想物体深度マップの保持データ
        /// </summary>
        public class IdealPyramidData
        {
            public int width;
            public int height;

            /// <summary>
            /// 手メッシュの深度ピラミッド L0〜L6。
            /// handPyramid[lv] は Vector4[levelWidth[lv] * levelHeight[lv]] であり、
            /// 各要素は (viewPos.x, viewPos.y, viewPos.z, linearNdcDepth) を格納（未交差時は (0,0,0,1e9)）。
            /// </summary>
            public Vector4[][] handPyramid = new Vector4[MaxPyramidLevel + 1][];
            public int[] levelWidths = new int[MaxPyramidLevel + 1];
            public int[] levelHeights = new int[MaxPyramidLevel + 1];

            /// <summary>
            /// 仮想物体のフル解像度 (L0) ビュー空間座標と深度 (viewPos.x, viewPos.y, viewPos.z, linearNdcDepth)。
            /// 未交差ピクセルは w = 1e9。
            /// </summary>
            public Vector4[] virtualObjectMap;

            /// <summary>
            /// 指定階層・座標のピラミッド値を取得します（PCD_Occlusion_Helpers.hlsl の FetchPyramidPosition と完全一致）。
            /// </summary>
            public Vector4 FetchHandPyramidPosition(int level, int baseX, int baseY, Vector2Int offset)
            {
                int clampedLv = Mathf.Clamp(level, 0, MaxPyramidLevel);
                int targetX = (baseX >> clampedLv) + offset.x;
                int targetY = (baseY >> clampedLv) + offset.y;

                int w = levelWidths[clampedLv];
                int h = levelHeights[clampedLv];

                if (targetX < 0 || targetY < 0 || targetX >= w || targetY >= h)
                {
                    return new Vector4(0f, 0f, 0f, SentinelDepth);
                }

                return handPyramid[clampedLv][targetY * w + targetX];
            }
        }

        /// <summary>
        /// 指定されたカメラ・手メッシュ・仮想物体から、全ピクセル交差判定と 6階層深度ピラミッドの再構築を実行します。
        /// </summary>
        public static IdealPyramidData BuildIdealPyramidForCamera(
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

            var data = new IdealPyramidData
            {
                width = screenWidth,
                height = screenHeight
            };

            // 1. 各階層 L0〜L6 の解像度計算と配列初期化
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
            // GPU投影行列 (renderIntoTexture = false) ではなく標準のカメラ投影行列から NDC [-1, 1] を計算する。
            // Unity の camera.projectionMatrix は OpenGL 規約 (NDC x,y,z ∈ [-1, 1]) であり、
            // GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) + ReversedZ 解除後の depth ∈ [0, 1] は
            // 0.5 * ndcZ_GL + 0.5 と数学的に完全一致する。
            Matrix4x4 projMatrix = camera.projectionMatrix;

            // 2. 手メッシュとピクセル中心レイの最初の交差点を全ピクセル L0 にラスタライズ
            if (handRoot != null)
            {
                RasterizeGameObjectFirstHitToL0(
                    handRoot,
                    viewMatrix,
                    projMatrix,
                    screenWidth,
                    screenHeight,
                    data.handPyramid[0]);
            }

            // 3. 仮想物体メッシュとピクセル中心レイの最初の交差点を仮想物体マップにラスタライズ
            if (virtualObjectRoot != null)
            {
                RasterizeGameObjectFirstHitToL0(
                    virtualObjectRoot,
                    viewMatrix,
                    projMatrix,
                    screenWidth,
                    screenHeight,
                    data.virtualObjectMap);
            }

            // 4. L0 から L1〜L6 の深度ピラミッドを 2x2 最小深度ルール (ZMinPositionDownsample) で再構築
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

            return data;
        }

        /// <summary>
        /// 構築済みの IdealPyramidData と各ピクセルの探索レベル (searchLevels) から、
        /// 8方向それぞれの理論正解二値マスク (bit 0..7) および評価対象フラグ (bit 12) を格納した uint[] を生成します。
        /// 探索ブロック (lv = 0..level) の最小値候補箇所に 1つでも手前の手メッシュ仮想点が存在すれば真 (1: 白) とします。
        /// </summary>
        /// <param name="pyramid">再構築された理想深度ピラミッド</param>
        /// <param name="searchLevels">実測点群側から決まるピクセルごとの探索レベル (0..6)。null の場合は defaultSearchLevel を使用</param>
        /// <param name="defaultSearchLevel">searchLevels が null の場合に使用する探索レベル</param>
        public static uint[] ComputeTheoreticalGroundTruthMasks(
            IdealPyramidData pyramid,
            byte[] searchLevels,
            int defaultSearchLevel = 2)
        {
            if (pyramid == null) return null;

            int w = pyramid.width;
            int h = pyramid.height;
            int totalPixels = w * h;
            uint[] gtMaskBuffer = new uint[totalPixels];

            bool hasSearchLevelMap = (searchLevels != null && searchLevels.Length == totalPixels);
            int fallbackLevel = Mathf.Clamp(defaultSearchLevel, 0, MaxPyramidLevel);

            Vector4[] voMap = pyramid.virtualObjectMap;
            Vector4[] handL0 = pyramid.handPyramid[0];

            for (int y = 0; y < h; y++)
            {
                int rowOffset = y * w;
                for (int x = 0; x < w; x++)
                {
                    int idx = rowOffset + x;
                    Vector4 voPos = voMap[idx];

                    // 仮想物体が存在しないピクセルは評価対象外
                    if (voPos.w >= SentinelDepth)
                    {
                        gtMaskBuffer[idx] = 0u;
                        continue;
                    }

                    int level = hasSearchLevelMap
                        ? Mathf.Clamp((int)searchLevels[idx], 0, MaxPyramidLevel)
                        : fallbackLevel;

                    uint gtSectorBits = 0u;
                    float voDepth = voPos.w;

                    // 各セクター s (0..7) について、階層 lv = 0..level の候補箇所に
                    // 1つでも仮想物体より手前の手メッシュ点があれば真 (bit s = 1)
                    for (int s = 0; s < 8; s++)
                    {
                        Vector2Int offset = SectorOffsets[s];
                        bool hasFrontHandPoint = false;

                        for (int lv = 0; lv <= level; lv++)
                        {
                            Vector4 neighbor = pyramid.FetchHandPyramidPosition(lv, x, y, offset);
                            if (neighbor.w < SentinelDepth && (voDepth - neighbor.w) > DepthEpsilon)
                            {
                                hasFrontHandPoint = true;
                                break;
                            }
                        }

                        if (hasFrontHandPoint)
                        {
                            gtSectorBits |= (1u << s);
                        }
                    }

                    // 既存の NeighborCountMap フォーマットと互換性を保つため:
                    // bit 0..7   : gtSectorBits
                    // bit 12     : isEvaluated (1u)
                    // bit 16..23 : gtSectorBits
                    uint packed = (gtSectorBits & 0xFFu)
                                | (1u << 12)
                                | ((gtSectorBits & 0xFFu) << 16);

                    gtMaskBuffer[idx] = packed;
                }
            }

            return gtMaskBuffer;
        }

        private static Vector4[] CreateSentinelArray(int length)
        {
            var arr = new Vector4[length];
            Vector4 sentinel = new Vector4(0f, 0f, 0f, SentinelDepth);
            for (int i = 0; i < length; i++)
            {
                arr[i] = sentinel;
            }
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

        /// <summary>
        /// 指定した GameObject 配下の全メッシュについて、ピクセル中心 (x + 0.5, y + 0.5) とカメラ視点を結ぶ直線が
        /// メッシュと初めて交差する最小深度点の (viewPos.xyz, depth) を targetL0Map に書き込みます。
        /// 非アクティブ状態や Layer 設定に依存せず、Transform のみから厳密に計算します。
        /// </summary>
        private static void RasterizeGameObjectFirstHitToL0(
            GameObject root,
            Matrix4x4 viewMatrix,
            Matrix4x4 projMatrix,
            int width,
            int height,
            Vector4[] targetL0Map)
        {
            Mesh sharedBakedMesh = new Mesh();
            try
            {
                // MeshFilter の収集 (非アクティブな子要素は除外、ただし root 自身が評価用に一時非表示の場合は含める)
                var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
                foreach (var mf in meshFilters)
                {
                    if (mf == null || mf.sharedMesh == null) continue;
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr != null && !mr.enabled) continue;
                    if (!IsSelfAndAncestorsActiveUpToRoot(mf.transform, root.transform)) continue;

                    RasterizeMeshFirstHit(
                        mf.sharedMesh,
                        mf.transform.localToWorldMatrix,
                        viewMatrix,
                        projMatrix,
                        width,
                        height,
                        targetL0Map);
                }

                // SkinnedMeshRenderer の収集
                var skinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in skinnedRenderers)
                {
                    if (smr == null || !smr.enabled || smr.sharedMesh == null) continue;
                    if (!IsSelfAndAncestorsActiveUpToRoot(smr.transform, root.transform)) continue;

                    smr.BakeMesh(sharedBakedMesh);
                    Matrix4x4 localToWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);

                    RasterizeMeshFirstHit(
                        sharedBakedMesh,
                        localToWorld,
                        viewMatrix,
                        projMatrix,
                        width,
                        height,
                        targetL0Map);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sharedBakedMesh);
            }
        }

        private static bool IsSelfAndAncestorsActiveUpToRoot(Transform current, Transform root)
        {
            Transform t = current;
            while (t != null && t != root)
            {
                if (!t.gameObject.activeSelf) return false;
                t = t.parent;
            }
            return true;
        }

        /// <summary>
        /// 単一メッシュの全三角形をビュー空間・クリップ空間へ投影し、
        /// 各ピクセル中心 (px + 0.5, py + 0.5) を通る視線レイとの正確な交点（最小深度 = 最初の交差点）を求めます。
        /// </summary>
        private static void RasterizeMeshFirstHit(
            Mesh mesh,
            Matrix4x4 localToWorld,
            Matrix4x4 viewMatrix,
            Matrix4x4 projMatrix,
            int width,
            int height,
            Vector4[] targetL0Map)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices == null || triangles == null || triangles.Length < 3) return;

            int vCount = vertices.Length;
            Matrix4x4 modelView = viewMatrix * localToWorld;

            Vector3[] viewVerts = new Vector3[vCount];
            Vector4[] clipVerts = new Vector4[vCount];

            for (int i = 0; i < vCount; i++)
            {
                Vector3 vp = modelView.MultiplyPoint3x4(vertices[i]);
                viewVerts[i] = vp;
                clipVerts[i] = projMatrix * new Vector4(vp.x, vp.y, vp.z, 1f);
            }

            float halfW = width * 0.5f;
            float halfH = height * 0.5f;

            int triCount = triangles.Length;
            for (int i = 0; i < triCount; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                Vector4 c0 = clipVerts[i0];
                Vector4 c1 = clipVerts[i1];
                Vector4 c2 = clipVerts[i2];

                // カメラのニアクリップより手前 (w <= 0) の頂点を含む極端な三角形はスキップ（手や仮想物体は全てカメラ前方にある）
                if (c0.w <= 1e-4f || c1.w <= 1e-4f || c2.w <= 1e-4f)
                    continue;

                float invW0 = 1.0f / c0.w;
                float invW1 = 1.0f / c1.w;
                float invW2 = 1.0f / c2.w;

                // NDC 座標 [-1, 1]
                float ndcX0 = c0.x * invW0;
                float ndcY0 = c0.y * invW0;
                float ndcZ0 = c0.z * invW0;

                float ndcX1 = c1.x * invW1;
                float ndcY1 = c1.y * invW1;
                float ndcZ1 = c1.z * invW2; // note: corrected below
                ndcZ1 = c1.z * invW1;

                float ndcX2 = c2.x * invW2;
                float ndcY2 = c2.y * invW2;
                float ndcZ2 = c2.z * invW2;

                // スクリーン座標 [0, width], [0, height]
                float sx0 = (ndcX0 + 1.0f) * halfW;
                float sy0 = (ndcY0 + 1.0f) * halfH;

                float sx1 = (ndcX1 + 1.0f) * halfW;
                float sy1 = (ndcY1 + 1.0f) * halfH;

                float sx2 = (ndcX2 + 1.0f) * halfW;
                float sy2 = (ndcY2 + 1.0f) * halfH;

                // 2D バウンディングボックス
                float minX = Mathf.Min(sx0, Mathf.Min(sx1, sx2));
                float maxX = Mathf.Max(sx0, Mathf.Max(sx1, sx2));
                float minY = Mathf.Min(sy0, Mathf.Min(sy1, sy2));
                float maxY = Mathf.Max(sy0, Mathf.Max(sy1, sy2));

                if (maxX < 0f || minX >= width || maxY < 0f || minY >= height)
                    continue;

                int xStart = Mathf.Clamp(Mathf.FloorToInt(minX), 0, width - 1);
                int xEnd = Mathf.Clamp(Mathf.FloorToInt(maxX), 0, width - 1);
                int yStart = Mathf.Clamp(Mathf.FloorToInt(minY), 0, height - 1);
                int yEnd = Mathf.Clamp(Mathf.FloorToInt(maxY), 0, height - 1);

                // 重心座標計算用の分母 (符号付き面積の2倍)
                float denom = (sy1 - sy2) * (sx0 - sx2) + (sx2 - sx1) * (sy0 - sy2);
                if (Mathf.Abs(denom) < 1e-8f)
                    continue; // 退化三角形

                float invDenom = 1.0f / denom;

                // OpenGL NDC Z [-1, 1] をパイプライン共通の正規化深度 [0, 1] (0=Near, 1=Far) に変換
                float d0 = ndcZ0 * 0.5f + 0.5f;
                float d1 = ndcZ1 * 0.5f + 0.5f;
                float d2 = ndcZ2 * 0.5f + 0.5f;

                Vector3 v0 = viewVerts[i0];
                Vector3 v1 = viewVerts[i1];
                Vector3 v2 = viewVerts[i2];

                // 透視補正補間用の (viewPos / w)
                Vector3 v0OverW = v0 * invW0;
                Vector3 v1OverW = v1 * invW1;
                Vector3 v2OverW = v2 * invW2;

                for (int py = yStart; py <= yEnd; py++)
                {
                    float sampleY = py + 0.5f;
                    int rowIdx = py * width;

                    for (int px = xStart; px <= xEnd; px++)
                    {
                        float sampleX = px + 0.5f;

                        // スクリーン空間での重心座標 (b0, b1, b2)
                        float b0 = ((sy1 - sy2) * (sampleX - sx2) + (sx2 - sx1) * (sampleY - sy2)) * invDenom;
                        if (b0 < -1e-5f) continue;

                        float b1 = ((sy2 - sy0) * (sampleX - sx2) + (sx0 - sx2) * (sampleY - sy2)) * invDenom;
                        if (b1 < -1e-5f) continue;

                        float b2 = 1.0f - b0 - b1;
                        if (b2 < -1e-5f) continue;

                        // クリップ空間 Z/W はスクリーン空間で線形補間される
                        float depth = b0 * d0 + b1 * d1 + b2 * d2;
                        if (depth < 0f || depth > 1f) continue;

                        int pixelIdx = rowIdx + px;
                        if (depth < targetL0Map[pixelIdx].w)
                        {
                            // 透視補正補間により、ピクセル中心レイと三角形平面の厳密な3D交差点 (viewPos) を復元
                            float interpInvW = b0 * invW0 + b1 * invW1 + b2 * invW2;
                            if (interpInvW > 1e-7f)
                            {
                                Vector3 hitViewPos = (b0 * v0OverW + b1 * v1OverW + b2 * v2OverW) / interpInvW;
                                targetL0Map[pixelIdx] = new Vector4(hitViewPos.x, hitViewPos.y, hitViewPos.z, depth);
                            }
                        }
                    }
                }
            }
        }
    }
}

