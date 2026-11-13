using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OPE.Editor
{
    /// <summary>
    /// 8セクター二値化閾値 T_loc 最適化スイープの実行アクション（開始ボタン、確認ダイアログ、進捗表示）を描画・管理する Drawer クラス。
    /// </summary>
    public static class OPE_LocalThresholdOptimizationActionDrawer
    {
        /// <summary>
        /// 実行ボタンまたは進捗バーを描画します。
        /// </summary>
        public static void DrawAction(OPE_StereoEvaluationController controller, bool canExecute)
        {
            if (controller == null) return;

            var runner = controller.LocalThresholdOptimizationRunner;

            if (runner != null && runner.isRunning)
            {
                DrawRunningProgress(runner);
            }
            else
            {
                DrawStartButton(controller, runner, canExecute);
            }
        }

        private static void DrawRunningProgress(OPE_LocalThresholdOptimizationRunner runner)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUI.color = new Color(0.3f, 0.8f, 1f);
                EditorGUILayout.LabelField("⏳ T_loc 最適化スイープを実行中...", EditorStyles.boldLabel);
                GUI.color = Color.white;

                EditorGUI.ProgressBar(
                    EditorGUILayout.GetControlRect(false, 20),
                    runner.progress,
                    runner.statusMessage);

                EditorGUILayout.Space(2);
                EditorGUILayout.HelpBox(
                    "各配置について理想深度ピラミッドを再構築し、メモリ上で超高速に 8方向 GT マスクとの IoU スイープを計算しています。しばらくお待ちください。",
                    MessageType.None);
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawStartButton(
            OPE_StereoEvaluationController controller,
            OPE_LocalThresholdOptimizationRunner runner,
            bool canExecute)
        {
            var targetPresets = OPE_LocalThresholdOptimizationSettingsDrawer.GetTargetPresets(controller);
            int presetCount = targetPresets != null ? targetPresets.Count : 0;
            int totalSteps = OPE_LocalThresholdOptimizationSettingsDrawer.GetTotalSteps();

            GUI.enabled = canExecute && presetCount > 0 && runner != null;
            GUI.backgroundColor = new Color(0.2f, 0.75f, 0.4f);

            string btnLabel = $"🚀 T_loc 最適化スイープを開始 ({presetCount} 配置 × {totalSteps} ステップ)";
            if (GUILayout.Button(btnLabel, GUILayout.Height(36)))
            {
                float startT = OPE_LocalThresholdOptimizationSettingsDrawer.StartT;
                float endT = OPE_LocalThresholdOptimizationSettingsDrawer.EndT;
                float stepT = OPE_LocalThresholdOptimizationSettingsDrawer.StepT;
                bool autoApply = OPE_LocalThresholdOptimizationSettingsDrawer.AutoApply;
                bool ensureNoise = OPE_LocalThresholdOptimizationSettingsDrawer.EnsureNoiseEnabled;
                float noiseMm = OPE_LocalThresholdOptimizationSettingsDrawer.OverrideNoiseMm;

                if (EditorUtility.DisplayDialog(
                    "T_loc 最適化スイープの開始",
                    $"対象プリセット: {presetCount} 件\n" +
                    $"スイープ範囲: {startT:F2} 〜 {endT:F2} (step: {stepT:F2})\n" +
                    $"ノイズ強制有効化: {ensureNoise} ({noiseMm:F1} mm)\n\n" +
                    "手メッシュから全ピクセルが埋まった理想深度ピラミッド (L0〜L6) を独自再構築し、\n" +
                    "パラメータ依存の探索半径における 8方向理論正解 (GT) との一致度 (IoU) をメモリ内で超高速計算します。\n" +
                    "開始しますか？",
                    "開始", "キャンセル"))
                {
                    controller.StartCoroutine(runner.RunOptimizationRoutine(
                        targetPresets, startT, endT, stepT, autoApply, ensureNoise, noiseMm));
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

