using System;

namespace OPE
{
    /// <summary>
    /// 単一セクター (0..7) の占有マスク一致度メトリクス。
    /// </summary>
    [Serializable]
    public struct SingleSectorMetrics
    {
        public int sectorId;
        public int tpPixels;
        public int fpPixels;
        public int fnPixels;
        public int unionPixels;
        public float iou;
        public float overOccRatio;  // FP / Union (本来非占有だが占有と判定)
        public float underOccRatio; // FN / Union (本来占有だが非占有と判定)

        public override string ToString()
        {
            return $"Sector {sectorId} IoU: {iou:P2} (TP:{tpPixels}, FP:{fpPixels}, FN:{fnPixels})";
        }
    }

    /// <summary>
    /// 単眼分の 8 方向セクター IoU 集計結果。
    /// </summary>
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

    /// <summary>
    /// 左右眼ステレオ統合の 8 方向セクター IoU 集計結果。
    /// </summary>
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
}

