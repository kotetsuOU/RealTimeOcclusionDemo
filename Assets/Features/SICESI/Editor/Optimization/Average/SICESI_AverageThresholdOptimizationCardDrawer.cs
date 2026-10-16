using System.IO;
using Core.Logging;
using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// Average 手法 T_ave 最適化スイープの直近結果サマリーカードを描画し、
    /// パイプラインへの再適用や成果物フォルダー・CSVの展開を担う Drawer クラス。
    /// </summary>
    public static class SICESI_AverageThresholdOptimizationCardDrawer
    {
        /// <summary>
        /// 最適化結果サマリーカードを描画します。
        /// </summary>
        public static void DrawResultCard(SICESI_StereoEvaluationController controller, SICESI_AverageThresholdOptimizationRunner runner)
        {
            if (controller == null || runner == null || runner.lastBestMeanIoU <= 0f) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUI.color = new Color(0.9f, 1f, 0.9f);
                EditorGUILayout.LabelField("🏆 T_ave 最適化結果サマリー", EditorStyles.boldLabel);
                GUI.color = Color.white;

                EditorGUILayout.HelpBox(
                    $"👑 最適オクルージョン閾値 T_ave:  {runner.lastBestTAve:F2}\n" +
                    $"📊 全配置平均 IoU (Mean IoU):    {runner.lastBestMeanIoU:P2}\n" +
                    $"📁 出力先フォルダー:            {runner.lastOutputDir}",
                    MessageType.Info);

                EditorGUILayout.BeginHorizontal();
                {
                    DrawApplyButton(controller, runner);
                    DrawOpenFolderButtons(runner);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawApplyButton(SICESI_StereoEvaluationController controller, SICESI_AverageThresholdOptimizationRunner runner)
        {
            if (controller.occlusionPipelineController == null) return;

            if (GUILayout.Button($"📥 パイプラインに T_ave = {runner.lastBestTAve:F2} を再適用", GUILayout.Height(26)))
            {
                Undo.RecordObject(controller.occlusionPipelineController, "Apply Best T_ave");
                controller.occlusionPipelineController.T_ave = runner.lastBestTAve;
                controller.occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.Average;
                EditorUtility.SetDirty(controller.occlusionPipelineController);

                if (SICESI_BenchmarkMethodRegistry.ActiveOptimizedParameters == null)
                {
                    SICESI_BenchmarkMethodRegistry.ActiveOptimizedParameters = new SICESI_OptimizationResult();
                }
                SICESI_BenchmarkMethodRegistry.ActiveOptimizedParameters.averageBestTAve = runner.lastBestTAve;
                SICESI_BenchmarkMethodRegistry.ActiveOptimizedParameters.averageMeanIoU = runner.lastBestMeanIoU;

                AppLogger.Log(controller, $"[TAveOpt] パイプラインコントローラーの T_ave を {runner.lastBestTAve:F2} に適用しました。", SICESI_AverageThresholdOptimizationRunner.Tag);
            }
        }

        private static void DrawOpenFolderButtons(SICESI_AverageThresholdOptimizationRunner runner)
        {
            if (string.IsNullOrEmpty(runner.lastOutputDir) || !Directory.Exists(runner.lastOutputDir)) return;

            if (GUILayout.Button("📂 最適値画像フォルダーを開く", GUILayout.Height(26)))
            {
                EditorUtility.RevealInFinder(runner.lastOutputDir);
            }

            string summaryCsv = Path.Combine(runner.lastOutputDir, "t_ave_sweep_summary.csv");
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
