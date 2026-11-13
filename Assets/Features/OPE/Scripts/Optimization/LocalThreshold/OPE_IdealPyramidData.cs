using UnityEngine;

namespace OPE
{
    /// <summary>
    /// 単眼カメラに対する理想深度ピラミッド (L0〜L6) および仮想物体深度マップの保持データ。
    /// </summary>
    public class OPE_IdealPyramidData
    {
        public const int MaxPyramidLevel = 6;
        public const float SentinelDepth = 1e9f;

        public int width;
        public int height;

        /// <summary>
        /// 手メッシュの深度ピラミッド L0〜L6。
        /// 各要素は (viewPos.x, viewPos.y, viewPos.z, linearNdcDepth) を格納（未交差時は (0,0,0,SentinelDepth)）。
        /// </summary>
        public Vector4[][] handPyramid = new Vector4[MaxPyramidLevel + 1][];
        public int[] levelWidths = new int[MaxPyramidLevel + 1];
        public int[] levelHeights = new int[MaxPyramidLevel + 1];

        /// <summary>
        /// 仮想物体のフル解像度 (L0) ビュー空間座標と深度 (viewPos.x, viewPos.y, viewPos.z, linearNdcDepth)。
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
}

