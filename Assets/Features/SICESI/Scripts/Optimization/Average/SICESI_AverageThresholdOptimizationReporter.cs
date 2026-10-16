using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SICESI
{
    /// <summary>
    /// Average 手法 T_ave 最適化スイープの結果 CSV 出力およびサマリーテキスト生成を担う Reporter クラス。
    /// </summary>
    public static class SICESI_AverageThresholdOptimizationReporter
    {
        /// <summary>
        /// 全プリセット平均の T_ave スイープサマリー CSV を出力します。
        /// </summary>
        public static void ExportSummaryCsv(
            string csvPath,
            float[] tValues,
            float[] meanIoU,
            float[] overOcc,
            float[] underOcc,
            float bestTAve,
            float bestMeanIoU)
        {
            var lines = new List<string>
            {
                "# Average Method T_ave Sweep Optimization Summary",
                $"# Best T_ave,{bestTAve:F2}",
                $"# Best Mean IoU,{bestMeanIoU:P4}",
                "T_ave,Mean_IoU,OverOcclusion_FN_Ratio,UnderOcclusion_FP_Ratio,Is_Optimal"
            };

            for (int i = 0; i < tValues.Length; i++)
            {
                bool isOpt = Mathf.Approximately(tValues[i], bestTAve);
                lines.Add($"{tValues[i]:F2},{meanIoU[i]:F6},{overOcc[i]:F6},{underOcc[i]:F6},{(isOpt ? "TRUE" : "FALSE")}");
            }

            File.WriteAllLines(csvPath, lines, Encoding.UTF8);
        }

        /// <summary>
        /// 各プリセットごとの T_ave スイープ IoU 推移 CSV を出力します。
        /// </summary>
        public static void ExportByPresetCsv(
            string csvPath,
            List<SICESI_PresetPoseManager.PresetEntry> presets,
            float[] tValues,
            float[,] matrix,
            int bestTIndex)
        {
            var header = new StringBuilder();
            header.Append("Preset_Name,VirtualObject,HandMesh,Best_TAve_IoU");
            for (int ti = 0; ti < tValues.Length; ti++)
            {
                header.Append($",T_{tValues[ti]:F2}");
            }

            var lines = new List<string> { header.ToString() };

            for (int pi = 0; pi < presets.Count; pi++)
            {
                var p = presets[pi];
                var sb = new StringBuilder();
                sb.Append($"{p.caseName},{p.virtualObjectName},{p.handMeshName},{matrix[pi, bestTIndex]:F6}");
                for (int ti = 0; ti < tValues.Length; ti++)
                {
                    sb.Append($",{matrix[pi, ti]:F6}");
                }
                lines.Add(sb.ToString());
            }

            File.WriteAllLines(csvPath, lines, Encoding.UTF8);
        }

        /// <summary>
        /// 完了サマリー文字列を構築します。
        /// </summary>
        public static string BuildSummaryText(
            int presetCount,
            float startT,
            float endT,
            float stepT,
            int stepCount,
            float bestTAve,
            float bestMeanIoU,
            string baseOutputDir,
            string summaryCsvPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine("🎯 Average 手法 T_ave 最適化スイープ完了サマリー");
            sb.AppendLine("==================================================");
            sb.AppendLine($"  評価プリセット数: {presetCount} 件");
            sb.AppendLine($"  スイープ範囲    : {startT:F2} .. {endT:F2} (step: {stepT:F2}, 全 {stepCount} 点)");
            sb.AppendLine($"  👑 最適 T_ave   : {bestTAve:F2}");
            sb.AppendLine($"  🏆 最高平均 IoU : {bestMeanIoU:P2}");
            sb.AppendLine($"  出力先フォルダー: {baseOutputDir}");
            sb.AppendLine($"  サマリー CSV   : {summaryCsvPath}");
            sb.AppendLine("==================================================");
            return sb.ToString();
        }
    }
}
