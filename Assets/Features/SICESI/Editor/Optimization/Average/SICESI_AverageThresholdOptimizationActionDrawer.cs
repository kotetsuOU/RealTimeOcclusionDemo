using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// Average 手法 T_ave 最適化スイープの実行アクション（開始ボタン、確認ダイアログ、進捗表示）を描画・管理する Drawer クラス。
    /// </summary>
    public static class SICESI_AverageThresholdOptimizationActionDrawer
    {
        /// <summary>
        /// 実行ボタンまたは進捗バーを描画します。
        /// </summary>
        public static void DrawAction(SICESI_StereoEvaluationController controller, bool canExecute)
        {
            if (controller == null) return;

            var runner = controller.AverageThresholdOptimizationRunner;

            if (runner != null && runner.isRunning)
            {
                DrawRunningProgress(runner);
            }
            else
            {
                DrawStartButton(controller, runner, canExecute);
            }
        }

        private static void DrawRunningProgress(SICESI_AverageThresholdOptimizationRunner runner)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUI.color = new Color(0.3f, 0.8f, 1f);
                EditorGUILayout.LabelField("⏳ T_ave 最適化スイープを実行中...", EditorStyles.boldLabel);
                GUI.color = Color.white;

                EditorGUI.ProgressBar(
                    EditorGUILayout.GetControlRect(false, 20),
                    runner.progress,
                    runner.statusMessage);

                EditorGUILayout.Space(2);
                EditorGUILayout.HelpBox(
                    "各配置に対するメモリ内リアルタイムレンダリングと IoU 計算を行っています。しばらくお待ちください。",
                    MessageType.None);
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawStartButton(
            SICESI_StereoEvaluationController controller,
            SICESI_AverageThresholdOptimizationRunner runner,
            bool canExecute)
        {
            var targetPresets = SICESI_AverageThresholdOptimizationSettingsDrawer.GetTargetPresets(controller);
            int presetCount = targetPresets != null ? targetPresets.Count : 0;
            int totalSteps = SICESI_AverageThresholdOptimizationSettingsDrawer.GetTotalSteps();

            GUI.enabled = canExecute && presetCount > 0 && runner != null;
            GUI.backgroundColor = new Color(0.2f, 0.75f, 0.4f);

            string btnLabel = $"🚀 T_ave 最適化スイープを開始 ({presetCount} 配置 × {totalSteps} ステップ)";
            if (GUILayout.Button(btnLabel, GUILayout.Height(36)))
            {
                float startT = SICESI_AverageThresholdOptimizationSettingsDrawer.StartT;
                float endT = SICESI_AverageThresholdOptimizationSettingsDrawer.EndT;
                float stepT = SICESI_AverageThresholdOptimizationSettingsDrawer.StepT;
                bool autoApply = SICESI_AverageThresholdOptimizationSettingsDrawer.AutoApply;

                if (EditorUtility.DisplayDialog(
                    "T_ave 最適化スイープの開始",
                    $"対象プリセット: {presetCount} 件\n" +
                    $"スイープ範囲: {startT:F2} 〜 {endT:F2} (step: {stepT:F2})\n\n" +
                    "画像は保存せずメモリ内で高速に IoU を計算し、最適値における画像と CSV のみを出力します。\n" +
                    "開始しますか？",
                    "開始", "キャンセル"))
                {
                    controller.StartCoroutine(runner.RunOptimizationRoutine(
                        targetPresets, startT, endT, stepT, autoApply));
                }
            }

            GUI.backgroundColor = Color.white;
            GUI.enabled = canExecute;

            if (presetCount == 0)
            {
                EditorGUILayout.HelpBox("実行対象のプリセットが存在しません。上の保存セクションでプリセットを作成してください。", MessageType.Warning);
            }
        }
    }
}
