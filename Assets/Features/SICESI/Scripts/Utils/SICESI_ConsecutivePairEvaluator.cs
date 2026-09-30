using System.Collections.Generic;

namespace SICESI
{
    /// <summary>
    /// SICE 2026 で提案する「連続非占有セクター許容規則」における
    /// 数学的・幾何学的な重複・等価条件の判定および探索パラメータペアの生成を担当するユーティリティクラス。
    /// </summary>
    public static class SICESI_ConsecutivePairEvaluator
    {
        /// <summary>
        /// 指定されたセクター占有閾値 R_th と最大連続非占有許容数 L_th の組み合わせが、
        /// 幾何学的・数学的に冗長（他の条件と等価）であるかを判定します。
        /// </summary>
        /// <param name="rTh">必要占有セクター数閾値 R_th (1〜8)</param>
        /// <param name="lTh">最大許容連続非占有セクター数 L_th (0〜8)</param>
        /// <param name="K">総セクター数 (デフォルト 8)</param>
        /// <returns>冗長（スキップ対象）の場合は true</returns>
        public static bool IsRedundantCombination(int rTh, int lTh, int K = 8)
        {
            // 1. L_th = 0 は全セクター占有 (N_occ = 8) と等価
            //    R_th = 8, L_th = 8 (全占有) が代表として存在するため、それ以外の L_th = 0 はスキップ
            if (lTh == 0)
            {
                return true;
            }

            // 2. L_th >= K - rTh の領域は、すべて「占有数 rTh のみ」と等価
            //    代表値として L_th = K (8: 方向条件無効化) のみを残し、それ以外の K - rTh <= lTh < K はスキップ
            if (lTh >= K - rTh && lTh < K)
            {
                return true;
            }

            // 3. 最大連続数が lTh 以下という幾何学的制約より、数学的・必然的に保証される最小占有数 minOccForL:
            //    - lTh = 1: 0同士が隣接不可 -> 0は最大4個 -> N_occ >= 4. (R_th < 4 は R_th = 4 と同一)
            //    - lTh = 2: 0が最大2連続 -> 0は最大5個 (00100101) -> N_occ >= 3. (R_th < 3 は R_th = 3 と同一)
            //    - lTh = 3: 0が最大3連続 -> 0は最大6個 (00010001) -> N_occ >= 2. (R_th < 2 は R_th = 2 と同一)
            if (lTh < K)
            {
                int maxZerosPossible = (K * lTh) / (lTh + 1);
                int implicitMinOcc = K - maxZerosPossible;
                if (rTh < implicitMinOcc)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 設定配列から、冗長条件をスキップした（または全件含む）有効な (R_th, L_th) ペアリストを生成します。
        /// </summary>
        public static List<(int sector, int maxZero)> GenerateValidPairs(int[] sweepSectors, int[] sweepMaxConsecutiveZeros, bool skipRedundantConditions, int K = 8)
        {
            var pairs = new List<(int sector, int maxZero)>();
            if (sweepSectors == null || sweepMaxConsecutiveZeros == null) return pairs;

            for (int s = 0; s < sweepSectors.Length; s++)
            {
                int sector = sweepSectors[s];
                for (int z = 0; z < sweepMaxConsecutiveZeros.Length; z++)
                {
                    int maxZero = sweepMaxConsecutiveZeros[z];
                    if (skipRedundantConditions && IsRedundantCombination(sector, maxZero, K))
                    {
                        continue;
                    }
                    pairs.Add((sector, maxZero));
                }
            }
            return pairs;
        }
    }
}
