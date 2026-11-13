using UnityEngine;

namespace OPE
{
    /// <summary>
    /// 8 セクター二値マスクおよび評価キャッシュと正解（GT）8 方向マスクとの間で、
    /// セクター別 (0..7) および 8 方向統合のピクセル単位一致度 (IoU) を算出する Pure C# 計算クラス。
    /// </summary>
    public static class OPE_Sector8IoUCalculator
    {
        /// <summary>
        /// GPU 生データバッファ（NeighborCountMap の uint[]）から直接ステレオ 8 方向 IoU メトリクスを計算します。
        /// </summary>
        public static Stereo8MetricsResult CalculateStereoFromGpuData(
            uint[] gtLeft, uint[] testLeft,
            uint[] gtRight, uint[] testRight)
        {
            var leftResult = CalculateSingleEyeFromGpuData(gtLeft, testLeft);
            var rightResult = CalculateSingleEyeFromGpuData(gtRight, testRight);
            return Stereo8MetricsResult.FromStereo(leftResult, rightResult);
        }

        /// <summary>
        /// 単眼の GPU 生データバッファ（NeighborCountMap の uint[]）から 8 方向 IoU を計算します。
        /// </summary>
        public static Sector8MetricsResult CalculateSingleEyeFromGpuData(uint[] gtData, uint[] testData)
        {
            if (gtData == null || testData == null) return new Sector8MetricsResult();

            int total = gtData.Length;
            if (testData.Length != total)
            {
                Debug.LogError($"[OPE_Sector8IoUCalculator] GPUバッファ長が不一致です: GT({total}) vs Test({testData.Length})");
                return new Sector8MetricsResult();
            }

            int[] tp = new int[8];
            int[] fp = new int[8];
            int[] fn = new int[8];
            int evaluatedCount = 0;

            for (int i = 0; i < total; i++)
            {
                uint testVal = testData[i];
                if (((testVal >> 12) & 1u) == 0u) continue; // isEvaluated (bit 12) == 1

                evaluatedCount++;
                uint gtPattern = (gtData[i] >> 16) & 0xFFu;
                uint testPattern = testVal & 0xFFu;
                AccumulateSectorCounts(gtPattern, testPattern, tp, fp, fn);
            }

            return BuildSectorMetricsResult(tp, fp, fn, evaluatedCount);
        }

        /// <summary>
        /// 左右眼の画像配列からステレオ 8 方向 IoU メトリクスを計算します。
        /// </summary>
        public static Stereo8MetricsResult CalculateStereo(
            Color32[] gtLeft, Color32[] testLeft, Color32[] voLeft,
            Color32[] gtRight, Color32[] testRight, Color32[] voRight,
            byte voThreshold = 128)
        {
            var leftResult = CalculateSingleEye(gtLeft, testLeft, voLeft, voThreshold);
            var rightResult = CalculateSingleEye(gtRight, testRight, voRight, voThreshold);
            return Stereo8MetricsResult.FromStereo(leftResult, rightResult);
        }

        /// <summary>
        /// 単眼の 8-bit パターン画像 (GT vs Test) と評価領域マスクから 8 方向 IoU を計算します。
        /// </summary>
        public static Sector8MetricsResult CalculateSingleEye(
            Color32[] gtPixels,
            Color32[] testPixels,
            Color32[] voPixels,
            byte voThreshold = 128)
        {
            if (gtPixels == null || testPixels == null) return new Sector8MetricsResult();

            int total = gtPixels.Length;
            if (testPixels.Length != total)
            {
                Debug.LogError($"[OPE_Sector8IoUCalculator] ピクセル長が不一致です: GT({total}) vs Test({testPixels.Length})");
                return new Sector8MetricsResult();
            }

            bool hasVoMask = voPixels != null && voPixels.Length == total;
            int[] tp = new int[8];
            int[] fp = new int[8];
            int[] fn = new int[8];
            int evaluatedCount = 0;

            for (int i = 0; i < total; i++)
            {
                if (hasVoMask)
                {
                    Color32 vc = voPixels[i];
                    if (vc.r <= voThreshold && vc.g <= voThreshold && vc.b <= voThreshold) continue;
                }

                evaluatedCount++;
                AccumulateSectorCounts(gtPixels[i].r, testPixels[i].r, tp, fp, fn);
            }

            return BuildSectorMetricsResult(tp, fp, fn, evaluatedCount);
        }

        /// <summary>
        /// 事前計算済みの評価キャッシュと正解マスクから、指定閾値 T_loc におけるステレオ 8 方向 IoU を計算します。
        /// </summary>
        public static Stereo8MetricsResult CalculateStereoFromEvaluationCache(
            uint[] gtLeftData,
            OPE_EyeEvaluationCache testLeftCache,
            uint[] gtRightData,
            OPE_EyeEvaluationCache testRightCache,
            float tLoc)
        {
            var leftResult = CalculateSingleEyeFromEvaluationCache(gtLeftData, testLeftCache, tLoc);
            var rightResult = CalculateSingleEyeFromEvaluationCache(gtRightData, testRightCache, tLoc);
            return Stereo8MetricsResult.FromStereo(leftResult, rightResult);
        }

        /// <summary>
        /// 事前計算済みの評価キャッシュと正解マスクから、8 セクター個別閾値ベクトル sectorTLoc のステレオ 8 方向 IoU を計算します。
        /// </summary>
        public static Stereo8MetricsResult CalculateStereoFromEvaluationCache(
            uint[] gtLeftData,
            OPE_EyeEvaluationCache testLeftCache,
            uint[] gtRightData,
            OPE_EyeEvaluationCache testRightCache,
            float[] sectorTLoc)
        {
            var leftResult = CalculateSingleEyeFromEvaluationCache(gtLeftData, testLeftCache, sectorTLoc);
            var rightResult = CalculateSingleEyeFromEvaluationCache(gtRightData, testRightCache, sectorTLoc);
            return Stereo8MetricsResult.FromStereo(leftResult, rightResult);
        }

        /// <summary>
        /// 単眼の評価キャッシュと正解マスクから指定 T_loc の 8 方向 IoU を計算します。
        /// </summary>
        public static Sector8MetricsResult CalculateSingleEyeFromEvaluationCache(
            uint[] gtData,
            OPE_EyeEvaluationCache testCache,
            float tLoc)
        {
            return CalculateFromEvaluationCacheCore(gtData, testCache, tLoc, null);
        }

        /// <summary>
        /// 単眼の評価キャッシュと正解マスクから、8 セクター個別閾値ベクトル sectorTLoc の 8 方向 IoU を計算します。
        /// </summary>
        public static Sector8MetricsResult CalculateSingleEyeFromEvaluationCache(
            uint[] gtData,
            OPE_EyeEvaluationCache testCache,
            float[] sectorTLoc)
        {
            return CalculateFromEvaluationCacheCore(gtData, testCache, 0.5f, sectorTLoc);
        }

        private static Sector8MetricsResult CalculateFromEvaluationCacheCore(
            uint[] gtData,
            OPE_EyeEvaluationCache testCache,
            float scalarTLoc,
            float[] sectorTLoc)
        {
            if (gtData == null || testCache == null ||
                testCache.evaluatedPixelIndices == null || testCache.evaluatedMinSectorOcclusion == null)
            {
                return new Sector8MetricsResult();
            }

            int[] evalIndices = testCache.evaluatedPixelIndices;
            float[] minOcc = testCache.evaluatedMinSectorOcclusion;
            int evalCount = evalIndices.Length;
            bool useVector = sectorTLoc != null;

            int[] tp = new int[8];
            int[] fp = new int[8];
            int[] fn = new int[8];

            for (int i = 0; i < evalCount; i++)
            {
                uint gtPattern = (gtData[evalIndices[i]] >> 16) & 0xFFu;
                int baseOffset = i * 8;

                for (int s = 0; s < 8; s++)
                {
                    float threshold = useVector
                        ? (s < sectorTLoc.Length ? sectorTLoc[s] : 0.5f)
                        : scalarTLoc;
                    bool gtOccupied = ((gtPattern >> s) & 1u) != 0u;
                    bool testOccupied = minOcc[baseOffset + s] < threshold;

                    if (gtOccupied && testOccupied) tp[s]++;
                    else if (!gtOccupied && testOccupied) fp[s]++;
                    else if (gtOccupied && !testOccupied) fn[s]++;
                }
            }

            return BuildSectorMetricsResult(tp, fp, fn, evalCount);
        }

        private static void AccumulateSectorCounts(uint gtPattern, uint testPattern, int[] tp, int[] fp, int[] fn)
        {
            for (int s = 0; s < 8; s++)
            {
                bool gtOccupied = ((gtPattern >> s) & 1u) != 0u;
                bool testOccupied = ((testPattern >> s) & 1u) != 0u;

                if (gtOccupied && testOccupied) tp[s]++;
                else if (!gtOccupied && testOccupied) fp[s]++;
                else if (gtOccupied && !testOccupied) fn[s]++;
            }
        }

        private static Sector8MetricsResult BuildSectorMetricsResult(int[] tp, int[] fp, int[] fn, int evaluatedCount)
        {
            var result = new Sector8MetricsResult { evaluatedPixels = evaluatedCount };
            float sumIoU = 0f;
            float sumOverOcc = 0f;
            float sumUnderOcc = 0f;
            int totalTp = 0;
            int totalFp = 0;
            int totalFn = 0;

            for (int s = 0; s < 8; s++)
            {
                int u = tp[s] + fp[s] + fn[s];
                float iou = u > 0 ? (float)tp[s] / u : 1f;
                float over = u > 0 ? (float)fp[s] / u : 0f;
                float under = u > 0 ? (float)fn[s] / u : 0f;

                result.sectors[s] = new SingleSectorMetrics
                {
                    sectorId = s,
                    tpPixels = tp[s],
                    fpPixels = fp[s],
                    fnPixels = fn[s],
                    unionPixels = u,
                    iou = iou,
                    overOccRatio = over,
                    underOccRatio = under
                };

                sumIoU += iou;
                sumOverOcc += over;
                sumUnderOcc += under;

                totalTp += tp[s];
                totalFp += fp[s];
                totalFn += fn[s];
            }

            result.macroMeanIoU = sumIoU / 8f;
            result.meanOverOcc = sumOverOcc / 8f;
            result.meanUnderOcc = sumUnderOcc / 8f;

            int totalUnion = totalTp + totalFp + totalFn;
            result.microIoU = totalUnion > 0 ? (float)totalTp / totalUnion : 1f;
            return result;
        }
    }
}
