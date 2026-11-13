using System.IO;
using Core.Logging;
using UnityEditor;
using UnityEngine;

namespace OPE.Editor
{
    /// <summary>
    /// 8セクター二値化閾値 T_loc 最適化スイープの直近結果サマリーカードを描画し、
    /// パイプラインへの再適用や成果物フォルダー・CSVの展開を担う Drawer クラス。
    /// </summary>
    public static class OPE_LocalThresholdOptimizationCardDrawer
    {
        private static readonly string[] SectorShortNames = new string[]
        {
            "Sec0(N)", "Sec1(NE)", "Sec2(E)", "Sec3(SE)", "Sec4(S)", "Sec5(SW)", "Sec6(W)", "Sec7(NW)"
        };

        /// <summary>
        /// 最適化結果サマリーカードを描画します。
        /// </summary>
        public static void DrawResultCard(OPE_StereoEvaluationController controller, OPE_LocalThresholdOptimizationRunner runner)
        {
            if (controller == null || runner == null || runner.lastBestMeanIoU <= 0f) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUI.color = new Color(0.9f, 1f, 0.9f);
                EditorGUILayout.LabelField("🏆 T_loc 最適化結果サマリー", EditorStyles.boldLabel);
                GUI.color = Color.white;

                string sectorText = "";
                if (runner.lastBestSectorIoU != null && runner.lastBestSectorIoU.Length == 8)
                {
                    sectorText = "\n🌐 セクター別 IoU (全体共通 T_loc* 時):\n  ";
                    for (int s = 0; s < 8; s++)
                    {
                        sectorText += $"{SectorShortNames[s]}: {runner.lastBestSectorIoU[s]:P1}   ";
                        if (s == 3) sectorText += "\n  ";
                    }
                }

                string indepText = "";
                if (runner.lastBestSectorIndependentTLoc != null && runner.lastBestSectorIndependentTLoc.Length == 8 && runner.lastBestSectorIndependentMeanIoU > 0f)
                {
                    indepText = $"\n\n🎯 8セクター独立 最適 T_loc,s* (平均IoU: {runner.lastBestSectorIndependentMeanIoU:P2}):\n  ";
                    for (int s = 0; s < 8; s++)
                    {
                        indepText += $"{SectorShortNames[s]}: {runner.lastBestSectorIndependentTLoc[s]:F2} ({runner.lastBestSectorIndependentIoU[s]:P1})   ";
                        if (s == 3) indepText += "\n  ";
                    }
                }

                EditorGUILayout.HelpBox(
                    $"👑 全体共通 最適二値化閾値 T_loc:   {runner.lastBestTLoc:F2}\n" +
                    $"📊 8方向平均 IoU (Mean IoU):       {runner.lastBestMeanIoU:P2}" +
                    sectorText + indepText + "\n\n" +
                    $"📁 出力先フォルダー:               {runner.lastOutputDir}",
                    MessageType.Info);

                EditorGUILayout.BeginHorizontal();
                {
                    DrawApplyButton(controller, runner);
                    DrawOpenFolderButtons(runner);
                }
                EditorGUILayout.EndHorizontal();

                if (runner.lastBestSectorIndependentTLoc != null && runner.lastBestSectorIndependentTLoc.Length == 8 && runner.lastBestSectorIndependentMeanIoU > 0f)
                {
                    EditorGUILayout.Space(2);
                    DrawApplySectorIndependentButton(controller, runner);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawApplyButton(OPE_StereoEvaluationController controller, OPE_LocalThresholdOptimizationRunner runner)
        {
            if (controller.occlusionPipelineController == null) return;

            if (GUILayout.Button($"📥 パイプラインに全体共通 T_loc = {runner.lastBestTLoc:F2} を適用", GUILayout.Height(26)))
            {
                Undo.RecordObject(controller.occlusionPipelineController, "Apply Best T_loc");
                runner.ApplyBestValueToPipeline(runner.lastBestTLoc, runner.lastBestMeanIoU);

                if (OPE_BenchmarkMethodRegistry.ActiveOptimizedParameters == null)
                {
                    OPE_BenchmarkMethodRegistry.ActiveOptimizedParameters = new OPE_OptimizationResult();
                }
                OPE_BenchmarkMethodRegistry.ActiveOptimizedParameters.localThresholdBestTLoc = runner.lastBestTLoc;
                OPE_BenchmarkMethodRegistry.ActiveOptimizedParameters.localThresholdMeanIoU = runner.lastBestMeanIoU;
            }
        }

        private static void DrawApplySectorIndependentButton(OPE_StereoEvaluationController controller, OPE_LocalThresholdOptimizationRunner runner)
        {
            if (controller.occlusionPipelineController == null) return;

            GUI.color = new Color(0.85f, 0.95f, 1f);
            if (GUILayout.Button($"🌐 パイプラインに 8セクター個別 T_loc ベクトルを適用 (平均IoU: {runner.lastBestSectorIndependentMeanIoU:P2})", GUILayout.Height(28)))
            {
                Undo.RecordObject(controller.occlusionPipelineController, "Apply Sector Independent T_loc");
                runner.ApplySectorIndependentToPipeline(runner.lastBestSectorIndependentTLoc, runner.lastBestSectorIndependentMeanIoU);
            }
            GUI.color = Color.white;
        }

        private static void DrawOpenFolderButtons(OPE_LocalThresholdOptimizationRunner runner)
        {
            if (string.IsNullOrEmpty(runner.lastOutputDir) || !Directory.Exists(runner.lastOutputDir)) return;

            if (GUILayout.Button("📂 最適値画像フォルダーを開く", GUILayout.Height(26)))
            {
                EditorUtility.RevealInFinder(runner.lastOutputDir);
            }

            string summaryCsv = Path.Combine(runner.lastOutputDir, "t_loc_sweep_summary.csv");
            if (File.Exists(summaryCsv))
            {
                if (GUILayout.Button("📄 IoU 推移 CSV を開く", GUILayout.Height(26)))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(summaryCsv) { UseShellExecute = true });
                }
            }
        }
    }
}
