using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace OPE
{
    /// <summary>
    /// T_loc 最適化スイープの結果 CSV 出力およびサマリーテキスト生成を担う Reporter クラス。
    /// </summary>
    public static class OPE_LocalThresholdOptimizationReporter
    {
        private static readonly string[] SectorDirections = new string[]
        {
            "N (北)", "NE (北東)", "E (東)", "SE (南東)", "S (南)", "SW (南西)", "W (西)", "NW (北西)"
        };

        /// <summary>
        /// 全プリセット平均の T_loc スイープサマリー CSV を出力します。
        /// </summary>
        public static void ExportSummaryCsv(
            string csvPath,
            float[] tLocValues,
            float[] overallMeanIoU,
            float[] overallOverOcc,
            float[] overallUnderOcc,
            float[,] sectorIoUMatrix,
            float bestTLoc,
            float bestMeanIoU,
            float[] bestSectorIndependentTLoc = null,
            float bestSectorIndependentMeanIoU = 0f)
        {
            var lines = new List<string>
            {
                "# Local Threshold (T_loc) Sweep Optimization Summary",
                $"# Best Global Scalar T_loc,{bestTLoc:F2}",
                $"# Best Global 8-Direction Mean IoU,{bestMeanIoU:P4}"
            };

            if (bestSectorIndependentTLoc != null && bestSectorIndependentTLoc.Length == 8)
            {
                var vecStr = new StringBuilder();
                for (int s = 0; s < 8; s++)
                {
                    if (s > 0) vecStr.Append("; ");
                    vecStr.Append($"Sec{s}={bestSectorIndependentTLoc[s]:F2}");
                }
                lines.Add($"# Best Sector-Independent T_loc Vector,\"{vecStr}\"");
                lines.Add($"# Best Sector-Independent Mean IoU,{bestSectorIndependentMeanIoU:P4}");
            }

            lines.Add("T_loc,Mean_8Dir_IoU,OverOcc_FP_Ratio,UnderOcc_FN_Ratio,Sec0_N,Sec1_NE,Sec2_E,Sec3_SE,Sec4_S,Sec5_SW,Sec6_W,Sec7_NW,Is_Optimal");

            for (int ti = 0; ti < tLocValues.Length; ti++)
            {
                bool isOpt = Mathf.Approximately(tLocValues[ti], bestTLoc);
                var sb = new StringBuilder();
                sb.Append($"{tLocValues[ti]:F2},{overallMeanIoU[ti]:F6},{overallOverOcc[ti]:F6},{overallUnderOcc[ti]:F6}");
                for (int s = 0; s < 8; s++)
                {
                    sb.Append($",{sectorIoUMatrix[s, ti]:F6}");
                }
                sb.Append($",{(isOpt ? "TRUE" : "FALSE")}");
                lines.Add(sb.ToString());
            }

            File.WriteAllLines(csvPath, lines, Encoding.UTF8);
        }

        /// <summary>
        /// セクター別 (0..7) の T_loc スイープ IoU 推移 CSV を出力します。
        /// 全体共通最適値 T_loc* と各セクター独立の最適値 T_loc,s* の両方を記載します。
        /// </summary>
        public static void ExportBySectorCsv(
            string csvPath,
            float[] tLocValues,
            float[,] sectorIoUMatrix,
            float[] bestSectorIoU,
            float[] bestSectorOverOcc,
            float[] bestSectorUnderOcc,
            float[] bestSectorIndependentTLoc = null,
            float[] bestSectorIndependentIoU = null)
        {
            var header = new StringBuilder();
            header.Append("Sector_ID,Direction,Global_Best_IoU,Global_Best_OverOcc_FP,Global_Best_UnderOcc_FN");
            if (bestSectorIndependentTLoc != null && bestSectorIndependentIoU != null)
            {
                header.Append(",Independent_Best_TLoc,Independent_Best_IoU");
            }
            for (int ti = 0; ti < tLocValues.Length; ti++)
            {
                header.Append($",T_{tLocValues[ti]:F2}");
            }

            var lines = new List<string> { header.ToString() };

            for (int s = 0; s < 8; s++)
            {
                var sb = new StringBuilder();
                sb.Append($"{s},{SectorDirections[s]},{bestSectorIoU[s]:F6},{bestSectorOverOcc[s]:F6},{bestSectorUnderOcc[s]:F6}");
                if (bestSectorIndependentTLoc != null && bestSectorIndependentIoU != null)
                {
                    sb.Append($",{bestSectorIndependentTLoc[s]:F2},{bestSectorIndependentIoU[s]:F6}");
                }
                for (int ti = 0; ti < tLocValues.Length; ti++)
                {
                    sb.Append($",{sectorIoUMatrix[s, ti]:F6}");
                }
                lines.Add(sb.ToString());
            }

            File.WriteAllLines(csvPath, lines, Encoding.UTF8);
        }

        /// <summary>
        /// 各プリセットごとの T_loc スイープ IoU 推移 CSV を出力します。
        /// </summary>
        public static void ExportByPresetCsv(
            string csvPath,
            List<OPE_PresetPoseManager.PresetEntry> presets,
            float[] tLocValues,
            float[,] matrix,
            int bestTIndex)
        {
            var header = new StringBuilder();
            header.Append("Preset_Name,VirtualObject,HandMesh,Best_TLoc_IoU");
            for (int ti = 0; ti < tLocValues.Length; ti++)
            {
                header.Append($",T_{tLocValues[ti]:F2}");
            }

            var lines = new List<string> { header.ToString() };

            for (int pi = 0; pi < presets.Count; pi++)
            {
                var p = presets[pi];
                var sb = new StringBuilder();
                sb.Append($"{p.caseName},{p.virtualObjectName},{p.handMeshName},{matrix[pi, bestTIndex]:F6}");
                for (int ti = 0; ti < tLocValues.Length; ti++)
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
            float bestTLoc,
            float bestMeanIoU,
            float[] bestSectorIoU,
            string baseOutputDir,
            string summaryCsvPath,
            float[] bestSectorIndependentTLoc = null,
            float[] bestSectorIndependentIoU = null,
            float bestSectorIndependentMeanIoU = 0f)
        {
            var sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine("🎯 8セクター二値化閾値 T_loc 最適化スイープ完了サマリー");
            sb.AppendLine("==================================================");
            sb.AppendLine($"  評価プリセット数: {presetCount} 件");
            sb.AppendLine($"  スイープ範囲    : {startT:F2} .. {endT:F2} (step: {stepT:F2}, 全 {stepCount} 点)");
            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine($"  👑 全体共通 最適 T_loc : {bestTLoc:F2}");
            sb.AppendLine($"  🏆 8方向平均 IoU       : {bestMeanIoU:P2}");
            sb.AppendLine("  --- セクター別 IoU (共通 T_loc* 時) ---");
            if (bestSectorIoU != null && bestSectorIoU.Length == 8)
            {
                for (int s = 0; s < 8; s++)
                {
                    sb.AppendLine($"    Sector {s} [{SectorDirections[s]}]: {bestSectorIoU[s]:P2}");
                }
            }

            if (bestSectorIndependentTLoc != null && bestSectorIndependentIoU != null && bestSectorIndependentTLoc.Length == 8)
            {
                sb.AppendLine("--------------------------------------------------");
                sb.AppendLine("  🌐 8方向独立 最適 T_loc,s* ベクトル:");
                for (int s = 0; s < 8; s++)
                {
                    sb.AppendLine($"    Sector {s} [{SectorDirections[s]}]: 最適 T_loc = {bestSectorIndependentTLoc[s]:F2} (IoU: {bestSectorIndependentIoU[s]:P2})");
                }
                sb.AppendLine($"  🏆 8方向独立 最適平均 IoU : {bestSectorIndependentMeanIoU:P2}");
            }

            sb.AppendLine("--------------------------------------------------");
            sb.AppendLine($"  出力先フォルダー: {baseOutputDir}");
            sb.AppendLine($"  サマリー CSV   : {summaryCsvPath}");
            sb.AppendLine("==================================================");
            return sb.ToString();
        }
    }
}
