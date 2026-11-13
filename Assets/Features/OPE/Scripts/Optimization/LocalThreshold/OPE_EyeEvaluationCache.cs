namespace OPE
{
    /// <summary>
    /// 単眼カメラに対する疎な実測点群の評価結果キャッシュ。
    /// 各仮想物体ピクセルの 8 方向最小局所評価値とパラメータ依存探索レベルを保持し、
    /// 任意の閾値 T_loc に対する NeighborCountMap 互換パック配列を生成します。
    /// </summary>
    public class OPE_EyeEvaluationCache
    {
        public int width;
        public int height;

        /// <summary> パラメータ依存で決定された各ピクセルの探索レベル (0..6) </summary>
        public byte[] searchLevels;

        /// <summary> 評価対象ピクセル（仮想物体領域かつ手前点群に直接覆われていない画素）のインデックス配列 </summary>
        public int[] evaluatedPixelIndices;

        /// <summary>
        /// <see cref="evaluatedPixelIndices"/> の各要素に対応する 8 方向の最小局所評価値。
        /// 長さは evaluatedPixelIndices.Length * 8。未ヒット方向は 2.0f。
        /// </summary>
        public float[] evaluatedMinSectorOcclusion;

        /// <summary>
        /// 指定した単一閾値 T_loc における全ピクセルの NeighborCountMap 互換パック配列を生成します。
        /// </summary>
        public uint[] BuildPackedTestDataForThreshold(float tLoc)
        {
            return BuildPackedTestDataCore(tLoc, null);
        }

        /// <summary>
        /// 8 セクター個別の閾値ベクトル sectorTLoc (サイズ 8) における NeighborCountMap 互換パック配列を生成します。
        /// </summary>
        public uint[] BuildPackedTestDataForThreshold(float[] sectorTLoc)
        {
            return BuildPackedTestDataCore(0.5f, sectorTLoc);
        }

        private uint[] BuildPackedTestDataCore(float scalarTLoc, float[] sectorTLoc)
        {
            uint[] data = new uint[width * height];
            int evalCount = evaluatedPixelIndices != null ? evaluatedPixelIndices.Length : 0;
            bool useVector = sectorTLoc != null;

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
                    float sTh = useVector
                        ? (s < sectorTLoc.Length ? sectorTLoc[s] : 0.5f)
                        : scalarTLoc;

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
}

