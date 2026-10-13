using System.IO;
using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// Python 最適化のバックグラウンド実行、進捗バー表示、
    /// および既存結果の再読み込み＆パイプライン適用アクションを担当する Drawer クラス。
    /// </summary>
    public static class SICESI_OptimizationActionDrawer
    {
        /// <summary>
        /// アクションセクション（実行中プログレスまたはアクションボタン）を描画します。
        /// </summary>
        public static void Draw(
            SICESI_StereoEvaluationController controller,
            SICESI_PythonOptimizationRunner runner,
            bool canExecute)
        {
            if (controller == null) return;

            if (runner != null && runner.isRunning)
            {
                DrawRunningProgress(runner);
            }
            else
            {
                DrawActionButtons(controller, canExecute);
            }
        }

        private static void DrawRunningProgress(SICESI_PythonOptimizationRunner runner)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUI.color = new Color(0.2f, 0.8f, 1.0f);
                EditorGUILayout.LabelField("⏳ Python 最適化プロセスが実行中です...", EditorStyles.boldLabel);
                GUI.color = Color.white;

                EditorGUILayout.LabelField($"ステータス: {runner.statusMessage}", EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(2);
                Rect r = EditorGUILayout.GetControlRect(false, 20);
                EditorGUI.ProgressBar(r, runner.progress, $"{runner.progress * 100:F0}%");
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawActionButtons(
            SICESI_StereoEvaluationController controller,
            bool canExecute)
        {
            EditorGUILayout.BeginHorizontal();
            {
                string targetDataset = SICESI_OptimizationSettingsDrawer.GetEffectiveDatasetPath(controller);
                string outputDir = SICESI_OptimizationSettingsDrawer.GetEffectiveOutputDir(controller);
                string pythonPath = SICESI_OptimizationSettingsDrawer.PythonPath;
                var objective = SICESI_OptimizationSettingsDrawer.Objective;
                bool autoApply = SICESI_OptimizationSettingsDrawer.AutoApply;
                bool skipDiff = SICESI_OptimizationSettingsDrawer.SkipDiffMaps;
                bool forceRegen = SICESI_OptimizationSettingsDrawer.ForceRegenerate;
                var targetApplyMode = SICESI_OptimizationSettingsDrawer.TargetApplyMode;

                // 1. Python 実行 & 置換ボタン
                GUI.backgroundColor = new Color(0.2f, 0.75f, 0.35f);
                GUI.enabled = canExecute && !controller.isCapturing;
                if (GUILayout.Button("🚀 Python 最適化を実行してパイプラインに自動置換", GUILayout.Height(36)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Python 最適化の実行確認",
                        $"以下の設定で最適化を実行し、パイプラインパラメータを自動置換しますか？\n\n" +
                        $"・Python パス: {pythonPath}\n" +
                        $"・対象データセット: {targetDataset}\n" +
                        $"・解析結果出力先: {outputDir}\n" +
                        $"・目的関数: {objective}\n" +
                        $"・自動置換: {(autoApply ? "有効 (PCDOcclusionPipelineController に即時反映)" : "無効")}\n\n" +
                        "※実行はバックグラウンドで行われ、Unity エディタはフリーズしません。",
                        "実行", "キャンセル"))
                    {
                        controller.Dispatcher.RunPythonOptimization(
                            targetDataset,
                            outputDir,
                            pythonPath,
                            skipDiff,
                            forceRegen,
                            objective,
                            autoApply);
                    }
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;

                // 2. 既存結果の再読み込み & 適用ボタン
                if (GUILayout.Button("📥 既存結果 (JSON/CSV) を\n再読み込みして適用", GUILayout.Width(170), GUILayout.Height(36)))
                {
                    string jsonPath = Path.Combine(outputDir, "optimized_parameters.json");
                    string csvPath = Path.Combine(outputDir, "common_rules_summary.csv");

                    bool ok = SICESI_OptimizationResult.TryLoadFromJson(jsonPath, objective, out var res);
                    if (!ok)
                    {
                        ok = SICESI_OptimizationResult.TryLoadFromCsv(csvPath, objective, out res);
                    }

                    if (ok && res != null)
                    {
                        SICESI_OptimizationApplier.ApplyAll(controller.occlusionPipelineController, res, targetApplyMode);
                        EditorUtility.DisplayDialog("適用完了", $"既存の最適化結果を読み込み、PCDOcclusionPipelineController に適用しました！\n\n読込先: {outputDir}\n\nMode 8: R={res.mode8ThresholdR}\nMode 20: R={res.mode20ThresholdR}, L={res.mode20MaxConsecutiveEmptyL}\nMode 36: 可視 {res.mode36VisibleClasses} クラス\nMode 256: 可視 {res.mode256VisiblePatterns} パターン", "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("読み込みエラー", $"最適化結果ファイル (optimized_parameters.json または common_rules_summary.csv) が見つかりませんでした。\n\n探索先フォルダ:\n{outputDir}\n\n対象データセットパスが正しいか確認してください。", "OK");
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
