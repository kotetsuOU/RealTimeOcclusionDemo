using System;

namespace OPE
{
    /// <summary>
    /// 8セクター二値化閾値 T_loc スイープ測定の結果データモデル。
    /// 全プリセットおよびセクター別 (0..7) の IoU 推移・最適値を保持します。
    /// </summary>
    [Serializable]
    public class OPE_LocalThresholdSweepResult
    {
        /// <summary> 探索した T_loc の値配列 (例: 0.00 〜 1.00) </summary>
        public float[] tLocValues;

        /// <summary> [プリセット番号, T_locインデックス] の 8方向平均 IoU 行列 </summary>
        public float[,] presetIoUMatrix;

        /// <summary> [セクター番号 0..7, T_locインデックス] の 全プリセット平均セクター別 IoU 行列 </summary>
        public float[,] sectorIoUMatrix;

        /// <summary> 各 T_loc における全プリセット・8セクター全体の平均 IoU 配列 </summary>
        public float[] overallMeanIoU;

        /// <summary> 各 T_loc における偽陽性率 (FP / Union: 本来黒なのに白=過剰支持) </summary>
        public float[] overallOverOccRatio;

        /// <summary> 各 T_loc における偽陰性率 (FN / Union: 本来白なのに黒=支持漏れ) </summary>
        public float[] overallUnderOccRatio;

        /// <summary> 導出された最適値 T_loc* </summary>
        public float bestTLoc;

        /// <summary> 最適値における最大平均 IoU </summary>
        public float bestMeanIoU;

        /// <summary> 最適値のステップインデックス </summary>
        public int bestTIndex;

        /// <summary> 最適 T_loc* におけるセクター別 (0..7) の平均 IoU </summary>
        public float[] bestSectorIoU = new float[8];

        /// <summary> 最適 T_loc* におけるセクター別 (0..7) の過剰支持率 (FP) </summary>
        public float[] bestSectorOverOcc = new float[8];

        /// <summary> 最適 T_loc* におけるセクター別 (0..7) の支持漏れ率 (FN) </summary>
        public float[] bestSectorUnderOcc = new float[8];

        /// <summary> 8方向各セクター独立で最適化した T_loc* 配列 (サイズ 8) </summary>
        public float[] bestSectorIndependentTLoc = new float[8];

        /// <summary> 8方向各セクター独立で最適化した各方向の最大 IoU 配列 (サイズ 8) </summary>
        public float[] bestSectorIndependentIoU = new float[8];

        /// <summary> 8方向各セクター独立の最適閾値を組み合わせた場合の全方向平均 IoU </summary>
        public float bestSectorIndependentMeanIoU;
    }
}
