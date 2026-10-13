using System;
using UnityEngine;

namespace SICESI
{
    /// <summary>
    /// ステレオ画像（Ground Truth vs Test レンダリング画像）間の
    /// ピクセル単位の一致度・IoU (Intersection over Union) および
    /// 過剰遮蔽 (FN)・遮蔽漏れ (FP) を計算する Pure C# ユーティリティクラス。
    /// Python 側の sicesi_core.metrics.UnionMaskMetrics と同等の計算を行います。
    /// </summary>
    public static class SICESI_IoUCalculator
    {
        [Serializable]
        public struct MetricsResult
        {
            public int tpPixels;
            public int fpPixels;
            public int fnPixels;
            public int unionPixels;
            public int gtVisiblePixels;
            public float iou;
            public float overOcclusionRatio;  // FN / Union
            public float underOcclusionRatio; // FP / Union

            public override string ToString()
            {
                return $"IoU: {iou:P2} (TP: {tpPixels}, FP: {fpPixels}, FN: {fnPixels}, Union: {unionPixels})";
            }
        }

        [Serializable]
        public struct StereoMetricsResult
        {
            public MetricsResult left;
            public MetricsResult right;
            public float meanIoU;
            public float totalIoU;

            public static StereoMetricsResult FromStereo(MetricsResult left, MetricsResult right)
            {
                int totalTp = left.tpPixels + right.tpPixels;
                int totalFp = left.fpPixels + right.fpPixels;
                int totalFn = left.fnPixels + right.fnPixels;
                int totalUnion = left.unionPixels + right.unionPixels;

                float totIoU = totalUnion > 0 ? (float)totalTp / totalUnion : 0f;
                float mIoU = (left.iou + right.iou) * 0.5f;

                return new StereoMetricsResult
                {
                    left = left,
                    right = right,
                    meanIoU = mIoU,
                    totalIoU = totIoU
                };
            }
        }

        /// <summary>
        /// 仮想物体 (Unlit 白) のピクセルかを判定します。
        /// 点群スプライトが黒色化された撮影画像において、Python 側の sicesi_core.metrics.UnionMaskMetrics
        /// (np.any(img > threshold, axis=-1), threshold=128) と完全同一の二値化判定を行います。
        /// </summary>
        public static bool IsVirtualObjectPixel(Color32 c, byte threshold = 128)
        {
            return (c.r > threshold) || (c.g > threshold) || (c.b > threshold);
        }

        /// <summary>
        /// 2枚の Texture2D (GT と Test) から IoU メトリクスを計算します。
        /// </summary>
        public static MetricsResult Calculate(Texture2D gtTex, Texture2D testTex, byte threshold = 128)
        {
            if (gtTex == null || testTex == null)
            {
                return default;
            }

            int w = gtTex.width;
            int h = gtTex.height;
            if (testTex.width != w || testTex.height != h)
            {
                Debug.LogError($"[SICESI_IoUCalculator] 画像解像度が一致しません: GT({w}x{h}) vs Test({testTex.width}x{testTex.height})");
                return default;
            }

            Color32[] gtPixels = gtTex.GetPixels32();
            Color32[] testPixels = testTex.GetPixels32();

            return Calculate(gtPixels, testPixels, threshold);
        }

        /// <summary>
        /// ピクセル配列から IoU メトリクスを計算します。
        /// </summary>
        public static MetricsResult Calculate(Color32[] gtPixels, Color32[] testPixels, byte threshold = 128)
        {
            int total = gtPixels.Length;
            int tp = 0;
            int fp = 0;
            int fn = 0;

            for (int i = 0; i < total; i++)
            {
                // Unlit 白オブジェクト判定: 全チャンネルが threshold を超えているか
                bool gtVisible = IsVirtualObjectPixel(gtPixels[i], threshold);
                bool testVisible = IsVirtualObjectPixel(testPixels[i], threshold);

                if (gtVisible && testVisible)
                {
                    tp++;
                }
                else if (!gtVisible && testVisible)
                {
                    fp++; // 遮蔽漏れ (本来隠れるはずが透過)
                }
                else if (gtVisible && !testVisible)
                {
                    fn++; // 過剰遮蔽 (本来見えるはずが遮蔽)
                }
            }

            int union = tp + fp + fn;
            float iou = union > 0 ? (float)tp / union : 0f;
            float overOcc = union > 0 ? (float)fn / union : 0f;
            float underOcc = union > 0 ? (float)fp / union : 0f;
            int gtVis = tp + fn;

            return new MetricsResult
            {
                tpPixels = tp,
                fpPixels = fp,
                fnPixels = fn,
                unionPixels = union,
                gtVisiblePixels = gtVis,
                iou = iou,
                overOcclusionRatio = overOcc,
                underOcclusionRatio = underOcc
            };
        }
    }
}
