using System;
using UnityEngine;

namespace OPE
{
    /// <summary>
    /// 8セクター二値マスク（debugSectorId = 8 の統合8bitパターン画像）と
    /// 深度ピラミッドから得られた正解（GT）8方向マスクとの間で、
    /// セクター別 (0..7) および8方向統合のピクセル単位一致度 (IoU) を算出する Pure C# クラス。
    /// </summary>
    public static class OPE_Sector8IoUCalculator
    {
        [Serializable]
        public struct SingleSectorMetrics
        {
            public int sectorId;
            public int tpPixels;
            public int fpPixels;
            public int fnPixels;
            public int unionPixels;
            public float iou;
            public float overOccRatio;  // FP / Union (本来黒なのに白=過剰支持)
            public float underOccRatio; // FN / Union (本来白なのに黒=支持漏れ)

            public override string ToString()
            {
                return $"Sector {sectorId} IoU: {iou:P2} (TP:{tpPixels}, FP:{fpPixels}, FN:{fnPixels})";
            }
        }

        [Serializable]
        public class Sector8MetricsResult
        {
            public SingleSectorMetrics[] sectors = new SingleSectorMetrics[8];
            public float macroMeanIoU;
            public float microIoU;
            public float meanOverOcc;
            public float meanUnderOcc;
            public int evaluatedPixels;

            public SingleSectorMetrics this[int index] => sectors[index];
        }

        [Serializable]
        public class Stereo8MetricsResult
        {
            public Sector8MetricsResult left;
            public Sector8MetricsResult right;
            public float meanIoU;
            public float[] sectorMeanIoU = new float[8];

            public static Stereo8MetricsResult FromStereo(Sector8MetricsResult left, Sector8MetricsResult right)
            {
                var result = new Stereo8MetricsResult
                {
                    left = left,
                    right = right,
                    meanIoU = (left.macroMeanIoU + right.macroMeanIoU) * 0.5f,
                    sectorMeanIoU = new float[8]
                };

                for (int s = 0; s < 8; s++)
                {
                    result.sectorMeanIoU[s] = (left.sectors[s].iou + right.sectors[s].iou) * 0.5f;
                }

                return result;
            }
        }

        /// <summary>
        /// GPU生データバッファ（NeighborCountMap の uint[]）から直接ステレオ 8方向 IoU メトリクスを計算します。
        /// ガンマ変換や画面 Blit を介さないためビット完全性が 100% 保証され、超高速です。
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
        /// 単眼の GPU 生データバッファ（NeighborCountMap の uint[]）から 8方向 IoU を計算します。
        /// </summary>
        public static Sector8MetricsResult CalculateSingleEyeFromGpuData(
            uint[] gtData,
            uint[] testData)
        {
            var result = new Sector8MetricsResult();
            if (gtData == null || testData == null) return result;

            int total = gtData.Length;
            if (testData.Length != total)
            {
                Debug.LogError($"[OPE_Sector8IoUCalculator] GPUバッファ長が不一致です: GT({total}) vs Test({testData.Length})");
                return result;
            }

            int[] tp = new int[8];
            int[] fp = new int[8];
            int[] fn = new int[8];
            int evaluatedCount = 0;

            for (int i = 0; i < total; i++)
            {
                uint testVal = testData[i];
                // 評価領域判定: isEvaluated (bit 12) == 1
                if (((testVal >> 12) & 1u) == 0u)
                {
                    continue;
                }

                evaluatedCount++;

                uint gtVal = gtData[i];
                // GTマスク: bit 16..23 (手前に点群が存在したか: sVal < 1.999)
                uint gtPattern = (gtVal >> 16) & 0xFFu;
                // Testマスク: bit 0..7 (sVal < T_loc)
                uint testPattern = testVal & 0xFFu;

                for (int s = 0; s < 8; s++)
                {
                    bool gtOccupied = ((gtPattern >> s) & 1u) != 0u;
                    bool testOccupied = ((testPattern >> s) & 1u) != 0u;

                    if (gtOccupied && testOccupied)
                    {
                        tp[s]++;
                    }
                    else if (!gtOccupied && testOccupied)
                    {
                        fp[s]++; // 偽陽性: 本来手前に点がないのに占有ありと判定
                    }
                    else if (gtOccupied && !testOccupied)
                    {
                        fn[s]++; // 偽陰性: 本来手前に点があるのに占有なしと判定
                    }
                }
            }

            result.evaluatedPixels = evaluatedCount;

            float sumIoU = 0f;
            float sumOverOcc = 0f;
            float sumUnderOcc = 0f;
            int totalTp = 0;
            int totalFp = 0;
            int totalFn = 0;

            for (int s = 0; s < 8; s++)
            {
                int u = tp[s] + fp[s] + fn[s];
                float iou = (u > 0) ? (float)tp[s] / u : 1f;
                float over = (u > 0) ? (float)fp[s] / u : 0f;
                float under = (u > 0) ? (float)fn[s] / u : 0f;

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
            result.microIoU = (totalUnion > 0) ? (float)totalTp / totalUnion : 1f;

            return result;
        }

        /// <summary>
        /// 左右眼の画像配列からステレオ 8方向 IoU メトリクスを計算します。
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
        /// 単眼の 8-bit パターン画像 (GT vs Test) と評価領域マスクから 8方向 IoU を計算します。
        /// </summary>
        public static Sector8MetricsResult CalculateSingleEye(
            Color32[] gtPixels,
            Color32[] testPixels,
            Color32[] voPixels,
            byte voThreshold = 128)
        {
            var result = new Sector8MetricsResult();
            if (gtPixels == null || testPixels == null) return result;

            int total = gtPixels.Length;
            if (testPixels.Length != total)
            {
                Debug.LogError($"[OPE_Sector8IoUCalculator] ピクセル長が不一致です: GT({total}) vs Test({testPixels.Length})");
                return result;
            }

            bool hasVoMask = (voPixels != null && voPixels.Length == total);

            int[] tp = new int[8];
            int[] fp = new int[8];
            int[] fn = new int[8];
            int evaluatedCount = 0;

            for (int i = 0; i < total; i++)
            {
                // 評価領域判定: 仮想オブジェクトの投影領域 (isEvaluated)
                if (hasVoMask)
                {
                    Color32 vc = voPixels[i];
                    if (vc.r <= voThreshold && vc.g <= voThreshold && vc.b <= voThreshold)
                    {
                        continue;
                    }
                }

                evaluatedCount++;

                byte gtPattern = gtPixels[i].r;
                byte testPattern = testPixels[i].r;

                for (int s = 0; s < 8; s++)
                {
                    bool gtOccupied = ((gtPattern >> s) & 1) != 0;
                    bool testOccupied = ((testPattern >> s) & 1) != 0;

                    if (gtOccupied && testOccupied)
                    {
                        tp[s]++;
                    }
                    else if (!gtOccupied && testOccupied)
                    {
                        fp[s]++; // 偽陽性: 本来占有なしなのに占有ありと判定
                    }
                    else if (gtOccupied && !testOccupied)
                    {
                        fn[s]++; // 偽陰性: 本来占有ありなのに占有なしと判定
                    }
                }
            }

            result.evaluatedPixels = evaluatedCount;

            float sumIoU = 0f;
            float sumOverOcc = 0f;
            float sumUnderOcc = 0f;
            int totalTp = 0;
            int totalFp = 0;
            int totalFn = 0;

            for (int s = 0; s < 8; s++)
            {
                int u = tp[s] + fp[s] + fn[s];
                float iou = (u > 0) ? (float)tp[s] / u : 1f; // 双方0かつ評価領域なら完全一致として1.0
                float over = (u > 0) ? (float)fp[s] / u : 0f;
                float under = (u > 0) ? (float)fn[s] / u : 0f;

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
            result.microIoU = (totalUnion > 0) ? (float)totalTp / totalUnion : 1f;

            return result;
        }

        /// <summary>
        /// 事前計算済みの評価キャッシュ (EyeEvaluationCache) と理想深度ピラミッド正解マスク (gtData) から、
        /// 指定した閾値 T_loc におけるステレオ 8方向 IoU を GPU 再描画なしで瞬時に計算します。
        /// </summary>
        public static Stereo8MetricsResult CalculateStereoFromEvaluationCache(
            uint[] gtLeftData,
            OPE_SparsePointCloudPyramidEvaluator.EyeEvaluationCache testLeftCache,
            uint[] gtRightData,
            OPE_SparsePointCloudPyramidEvaluator.EyeEvaluationCache testRightCache,
            float tLoc)
        {
            var leftResult = CalculateSingleEyeFromEvaluationCache(gtLeftData, testLeftCache, tLoc);
            var rightResult = CalculateSingleEyeFromEvaluationCache(gtRightData, testRightCache, tLoc);
            return Stereo8MetricsResult.FromStereo(leftResult, rightResult);
        }

        /// <summary>
        /// 事前計算済みの評価キャッシュと正解マスクから、8セクター個別の閾値ベクトル sectorTLoc (サイズ8) における
        /// ステレオ 8方向 IoU を瞬時に計算します。
        /// </summary>
        public static Stereo8MetricsResult CalculateStereoFromEvaluationCache(
            uint[] gtLeftData,
            OPE_SparsePointCloudPyramidEvaluator.EyeEvaluationCache testLeftCache,
            uint[] gtRightData,
            OPE_SparsePointCloudPyramidEvaluator.EyeEvaluationCache testRightCache,
            float[] sectorTLoc)
        {
            var leftResult = CalculateSingleEyeFromEvaluationCache(gtLeftData, testLeftCache, sectorTLoc);
            var rightResult = CalculateSingleEyeFromEvaluationCache(gtRightData, testRightCache, sectorTLoc);
            return Stereo8MetricsResult.FromStereo(leftResult, rightResult);
        }

        /// <summary>
        /// 単眼の評価キャッシュと正解マスクから指定 T_loc の 8方向 IoU を計算します。
        /// </summary>
        public static Sector8MetricsResult CalculateSingleEyeFromEvaluationCache(
            uint[] gtData,
            OPE_SparsePointCloudPyramidEvaluator.EyeEvaluationCache testCache,
            float tLoc)
        {
            var result = new Sector8MetricsResult();
            if (gtData == null || testCache == null || testCache.evaluatedPixelIndices == null || testCache.evaluatedMinSectorOcclusion == null)
            {
                return result;
            }

            int[] evalIndices = testCache.evaluatedPixelIndices;
            float[] minOcc = testCache.evaluatedMinSectorOcclusion;
            int evalCount = evalIndices.Length;

            int[] tp = new int[8];
            int[] fp = new int[8];
            int[] fn = new int[8];

            for (int i = 0; i < evalCount; i++)
            {
                int pixelIdx = evalIndices[i];
                uint gtPattern = (gtData[pixelIdx] >> 16) & 0xFFu;
                int baseOffset = i * 8;

                for (int s = 0; s < 8; s++)
                {
                    bool gtOccupied = ((gtPattern >> s) & 1u) != 0u;
                    bool testOccupied = minOcc[baseOffset + s] < tLoc;

                    if (gtOccupied && testOccupied)
                    {
                        tp[s]++;
                    }
                    else if (!gtOccupied && testOccupied)
                    {
                        fp[s]++; // 偽陽性: 本来手前に手メッシュ点がないのにノイズ等で占有ありと判定
                    }
                    else if (gtOccupied && !testOccupied)
                    {
                        fn[s]++; // 偽陰性: 本来手前に手メッシュ点があるのに疎または閾値不足で占有なしと判定
                    }
                }
            }

            result.evaluatedPixels = evalCount;

            float sumIoU = 0f;
            float sumOverOcc = 0f;
            float sumUnderOcc = 0f;
            int totalTp = 0;
            int totalFp = 0;
            int totalFn = 0;

            for (int s = 0; s < 8; s++)
            {
                int u = tp[s] + fp[s] + fn[s];
                float iou = (u > 0) ? (float)tp[s] / u : 1f;
                float over = (u > 0) ? (float)fp[s] / u : 0f;
                float under = (u > 0) ? (float)fn[s] / u : 0f;

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
            result.microIoU = (totalUnion > 0) ? (float)totalTp / totalUnion : 1f;

            return result;
        }

        /// <summary>
        /// 単眼の評価キャッシュと正解マスクから、8セクター個別の閾値ベクトル sectorTLoc (サイズ8) を用いて 8方向 IoU を計算します。
        /// </summary>
        public static Sector8MetricsResult CalculateSingleEyeFromEvaluationCache(
            uint[] gtData,
            OPE_SparsePointCloudPyramidEvaluator.EyeEvaluationCache testCache,
            float[] sectorTLoc)
        {
            var result = new Sector8MetricsResult();
            if (gtData == null || testCache == null || testCache.evaluatedPixelIndices == null || testCache.evaluatedMinSectorOcclusion == null)
            {
                return result;
            }

            int[] evalIndices = testCache.evaluatedPixelIndices;
            float[] minOcc = testCache.evaluatedMinSectorOcclusion;
            int evalCount = evalIndices.Length;

            int[] tp = new int[8];
            int[] fp = new int[8];
            int[] fn = new int[8];

            for (int i = 0; i < evalCount; i++)
            {
                int pixelIdx = evalIndices[i];
                uint gtPattern = (gtData[pixelIdx] >> 16) & 0xFFu;
                int baseOffset = i * 8;

                for (int s = 0; s < 8; s++)
                {
                    float sTh = (sectorTLoc != null && sectorTLoc.Length > s) ? sectorTLoc[s] : 0.5f;
                    bool gtOccupied = ((gtPattern >> s) & 1u) != 0u;
                    bool testOccupied = minOcc[baseOffset + s] < sTh;

                    if (gtOccupied && testOccupied)
                    {
                        tp[s]++;
                    }
                    else if (!gtOccupied && testOccupied)
                    {
                        fp[s]++; // 偽陽性: 本来手前に手メッシュ点がないのにノイズ等で占有ありと判定
                    }
                    else if (gtOccupied && !testOccupied)
                    {
                        fn[s]++; // 偽陰性: 本来手前に手メッシュ点があるのに疎または閾値不足で占有なしと判定
                    }
                }
            }

            result.evaluatedPixels = evalCount;

            float sumIoU = 0f;
            float sumOverOcc = 0f;
            float sumUnderOcc = 0f;
            int totalTp = 0;
            int totalFp = 0;
            int totalFn = 0;

            for (int s = 0; s < 8; s++)
            {
                int u = tp[s] + fp[s] + fn[s];
                float iou = (u > 0) ? (float)tp[s] / u : 1f;
                float over = (u > 0) ? (float)fp[s] / u : 0f;
                float under = (u > 0) ? (float)fn[s] / u : 0f;

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
            result.microIoU = (totalUnion > 0) ? (float)totalTp / totalUnion : 1f;

            return result;
        }
    }
}
