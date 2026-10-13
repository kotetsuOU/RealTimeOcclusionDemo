using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SICESI
{
    /// <summary>
    /// SICE SI ベンチマーク評価結果の CSV サマリー記録および全体平均テキスト生成を担当するレポータークラス。
    /// 画像の読み込み・削除・差分マップ出力処理は <see cref="SICESI_BenchmarkImageIO"/> に委譲されています。
    /// </summary>
    public static class SICESI_BenchmarkReporter
    {
        // 後方互換用型エイリアス
        public class MethodMetricsAccumulator : SICESI_MethodMetricsAccumulator
        {
            public MethodMetricsAccumulator(string name) : base(name) { }
        }

        /// <summary>
        /// CSV ファイルが存在しない場合にヘッダー行を初期化作成します。
        /// </summary>
        public static void InitializeCsvHeader(string csvPath)
        {
            if (!File.Exists(csvPath))
            {
                string header = "Timestamp,VirtualObject,HandMesh,Density,Method,Mean_IoU,Total_IoU,Left_IoU,Right_IoU,Left_TP,Left_FP,Left_FN,Right_TP,Right_FP,Right_FN\n";
                File.WriteAllText(csvPath, header, Encoding.UTF8);
            }
        }

        /// <summary>
        /// 評価結果メトリクスを CSV の末尾に追記します。
        /// </summary>
        public static void AppendCsvRecord(
            string csvPath,
            string voName,
            string handName,
            float density,
            string method,
            SICESI_IoUCalculator.StereoMetricsResult res)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{voName},{handName},{density:F2},{method}," +
                          $"{res.meanIoU:F4},{res.totalIoU:F4}," +
                          $"{res.left.iou:F4},{res.right.iou:F4}," +
                          $"{res.left.tpPixels},{res.left.fpPixels},{res.left.fnPixels}," +
                          $"{res.right.tpPixels},{res.right.fpPixels},{res.right.fnPixels}\n";
            File.AppendAllText(csvPath, line, Encoding.UTF8);
        }

        /// <summary>
        /// 全評価完了時に、5手法それぞれの全体平均行をサマリー CSV の末尾に追記します。
        /// </summary>
        public static void AppendOverallAverageSummary(
            string csvPath,
            Dictionary<string, SICESI_MethodMetricsAccumulator> accumulators)
        {
            if (accumulators == null || accumulators.Count == 0 || !File.Exists(csvPath)) return;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("# ==========================================================================================");
            sb.AppendLine("# === OVERALL 5-METHODS AVERAGE SUMMARY (全Case・全密度 平均値サマリー) ===");
            sb.AppendLine("# ==========================================================================================");
            sb.AppendLine("Timestamp,VirtualObject,HandMesh,Density,Method,Mean_IoU,Total_IoU,Left_IoU,Right_IoU,Left_TP,Left_FP,Left_FN,Right_TP,Right_FP,Right_FN");

            string nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            foreach (var kvp in accumulators)
            {
                var acc = kvp.Value;
                if (acc.count == 0) continue;

                sb.AppendLine($"{nowStr},[OVERALL_AVERAGE],[ALL_CASES],(AllDensities),{acc.methodName}," +
                              $"{acc.AvgMeanIoU:F4},{acc.AvgTotalIoU:F4}," +
                              $"{acc.AvgLeftIoU:F4},{acc.AvgRightIoU:F4}," +
                              $"{acc.AvgLeftTp:F1},{acc.AvgLeftFp:F1},{acc.AvgLeftFn:F1}," +
                              $"{acc.AvgRightTp:F1},{acc.AvgRightFp:F1},{acc.AvgRightFn:F1}");
            }

            File.AppendAllText(csvPath, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// 5手法の全体平均サマリー専用の独立した CSV ファイルを書き出します。
        /// </summary>
        public static void SaveOverallAverageCsv(
            string dedicatedCsvPath,
            Dictionary<string, SICESI_MethodMetricsAccumulator> accumulators)
        {
            if (accumulators == null || accumulators.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("Method,SamplesCount,Mean_IoU,Total_IoU,Left_IoU,Right_IoU,Left_TP_Avg,Left_FP_Avg(漏れ),Left_FN_Avg(過剰),Right_TP_Avg,Right_FP_Avg(漏れ),Right_FN_Avg(過剰)");

            foreach (var kvp in accumulators)
            {
                var acc = kvp.Value;
                sb.AppendLine($"\"{acc.methodName}\",{acc.count}," +
                              $"{acc.AvgMeanIoU:F4},{acc.AvgTotalIoU:F4}," +
                              $"{acc.AvgLeftIoU:F4},{acc.AvgRightIoU:F4}," +
                              $"{acc.AvgLeftTp:F1},{acc.AvgLeftFp:F1},{acc.AvgLeftFn:F1}," +
                              $"{acc.AvgRightTp:F1},{acc.AvgRightFp:F1},{acc.AvgRightFn:F1}");
            }

            File.WriteAllText(dedicatedCsvPath, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// コンソールログや UI 表示用に 5 手法の平均値を整形したサマリー文字列を生成します。
        /// </summary>
        public static string FormatOverallAverageSummaryString(
            Dictionary<string, SICESI_MethodMetricsAccumulator> accumulators)
        {
            if (accumulators == null || accumulators.Count == 0) return "5手法の平均データはありません。";

            var sb = new StringBuilder();
            sb.AppendLine("==========================================================================================");
            sb.AppendLine("=== [Benchmark Summary] 5手法の全体平均 IoU 計測結果 ===");
            sb.AppendLine("==========================================================================================");

            int idx = 1;
            foreach (var kvp in accumulators)
            {
                var acc = kvp.Value;
                sb.AppendLine($"  {idx}. {acc.methodName,-35} : Mean IoU = {acc.AvgMeanIoU:P2} (L: {acc.AvgLeftIoU:P2}, R: {acc.AvgRightIoU:P2}) [N={acc.count}]");
                idx++;
            }
            sb.AppendLine("==========================================================================================");
            return sb.ToString();
        }

        #region 画像 I/O 委譲メソッド (SICESI_BenchmarkImageIO 委譲)

        public static void SaveDiffImage(Texture2D gtTex, Texture2D testTex, string path, byte threshold = 128)
            => SICESI_BenchmarkImageIO.SaveDiffImage(gtTex, testTex, path, threshold);

        public static void DeleteStereoImages(string targetDir, string filePrefix)
            => SICESI_BenchmarkImageIO.DeleteStereoImages(targetDir, filePrefix);

        public static (Texture2D leftTex, Texture2D rightTex) LoadStereoTextures(string targetDir, string filePrefix, int maxWaitMs = 2000)
            => SICESI_BenchmarkImageIO.LoadStereoTextures(targetDir, filePrefix, maxWaitMs);

        public static Texture2D LoadTextureFromDisk(string path, int maxWaitMs = 2000)
            => SICESI_BenchmarkImageIO.LoadTextureFromDisk(path, maxWaitMs);

        #endregion
    }
}
