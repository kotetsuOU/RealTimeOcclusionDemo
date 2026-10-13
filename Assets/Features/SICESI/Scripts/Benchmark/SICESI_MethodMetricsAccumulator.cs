using System;

namespace SICESI
{
    /// <summary>
    /// SICE SI ベンチマーク評価における手法ごとの累積メトリクス（IoU, TP, FP, FN）を集計・保持するデータモデルクラス。
    /// </summary>
    public class SICESI_MethodMetricsAccumulator
    {
        public string methodName;
        public int count = 0;
        public double sumMeanIoU = 0.0;
        public double sumTotalIoU = 0.0;
        public double sumLeftIoU = 0.0;
        public double sumRightIoU = 0.0;
        public long sumLeftTp = 0;
        public long sumLeftFp = 0;
        public long sumLeftFn = 0;
        public long sumRightTp = 0;
        public long sumRightFp = 0;
        public long sumRightFn = 0;

        public SICESI_MethodMetricsAccumulator(string name)
        {
            methodName = name;
        }

        /// <summary>
        /// 1回のステレオ計測結果を加算します。
        /// </summary>
        public void Add(SICESI_IoUCalculator.StereoMetricsResult res)
        {
            count++;
            sumMeanIoU += res.meanIoU;
            sumTotalIoU += res.totalIoU;
            sumLeftIoU += res.left.iou;
            sumRightIoU += res.right.iou;
            sumLeftTp += res.left.tpPixels;
            sumLeftFp += res.left.fpPixels;
            sumLeftFn += res.left.fnPixels;
            sumRightTp += res.right.tpPixels;
            sumRightFp += res.right.fpPixels;
            sumRightFn += res.right.fnPixels;
        }

        public double AvgMeanIoU => count > 0 ? sumMeanIoU / count : 0.0;
        public double AvgTotalIoU => count > 0 ? sumTotalIoU / count : 0.0;
        public double AvgLeftIoU => count > 0 ? sumLeftIoU / count : 0.0;
        public double AvgRightIoU => count > 0 ? sumRightIoU / count : 0.0;
        public double AvgLeftTp => count > 0 ? (double)sumLeftTp / count : 0.0;
        public double AvgLeftFp => count > 0 ? (double)sumLeftFp / count : 0.0;
        public double AvgLeftFn => count > 0 ? (double)sumLeftFn / count : 0.0;
        public double AvgRightTp => count > 0 ? (double)sumRightTp / count : 0.0;
        public double AvgRightFp => count > 0 ? (double)sumRightFp / count : 0.0;
        public double AvgRightFn => count > 0 ? (double)sumRightFn / count : 0.0;
    }
}
