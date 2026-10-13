using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// 選択されたプリセットに対する一括実行アクション
    /// （検証用: リアルタイムIoU計測 / 最適化用: 8枚二値マスク一括収集＆Python最適化）
    /// のボタン描画、状態判定、確認ダイアログ、および実行ディスパッチを担当する Drawer クラス。
    /// </summary>
    public static class SICESI_PresetExecutionActionDrawer
    {
        /// <summary>
        /// 一括実行ボタンおよび結果カードを描画します。
        /// </summary>
        public static void Draw(
            SICESI_StereoEvaluationController controller,
            List<SICESI_PresetPoseManager.PresetEntry> targetPresets,
            string targetCategoryName,
            bool canExecute)
        {
            if (controller == null || targetPresets == null) return;

            var selectedPresets = SICESI_PresetSelectionManager.GetSelectedEntries(targetPresets);
            bool hasSelection = selectedPresets.Count > 0;
            bool isOpt = targetCategoryName == "最適化用";

            bool isBusy = isOpt
                ? controller.BatchReSweepRunner.isRunning || controller.isCapturing
                : controller.GeneralizationRunner.isRunning || controller.isCapturing;

            GUI.enabled = canExecute && hasSelection && !isBusy;
            GUI.backgroundColor = isOpt ? new Color(0.2f, 0.9f, 0.5f) : new Color(1.0f, 0.45f, 0.2f);

            int densityCount = controller.sweepDensities != null ? controller.sweepDensities.Length : 1;

            if (isOpt)
            {
                DrawOptimizationButton(controller, targetPresets, selectedPresets, densityCount, hasSelection);
            }
            else
            {
                DrawEvaluationButton(controller, targetPresets, selectedPresets, densityCount, hasSelection);
            }

            // 直近の再検証（ベンチマーク）5手法平均結果カードの描画
            SICESI_PresetBenchmarkResultsCardDrawer.Draw(controller);
        }

        private static void DrawOptimizationButton(
            SICESI_StereoEvaluationController controller,
            List<SICESI_PresetPoseManager.PresetEntry> targetPresets,
            List<SICESI_PresetPoseManager.PresetEntry> selectedPresets,
            int densityCount,
            bool hasSelection)
        {
            string btnText = hasSelection
                ? $"🪐 最適化用プリセット (選択中 {selectedPresets.Count} / 全 {targetPresets.Count} 件) の 8枚二値マスク一括収集 & 最適化\n(選択{selectedPresets.Count}配置 × {densityCount}密度: 8枚の二値マスク自動収集 ➔ 256パターンLUT最適化 ➔ 自動反映)"
                : $"⚠️ 実行対象の最適化用プリセットが選択されていません (0 件)";

            if (GUILayout.Button(btnText, GUILayout.Height(52)))
            {
                if (EditorUtility.DisplayDialog(
                    "最適化用プリセット 8枚二値マスク一括収集 & 最適化の開始",
                    $"対象: 選択中 {selectedPresets.Count} 件 (全 {targetPresets.Count} 件中)\n" +
                    $"点群密度: {densityCount} 段階\n\n" +
                    $"【処理内容】\n" +
                    $"1. 選択された各配置 (Case 1〜6等) をシーンに自動復元\n" +
                    $"2. 各 Case ごとに 8枚の二値マスク (sector_0〜7_mask) と GPU真値マスクを一括自動撮影 (Sector8MaskSweep)\n" +
                    $"3. 全配置の収集完了後、Python 256パターンLUT最適化をバックグラウンド実行\n" +
                    $"4. 算出された最適パラメータ (Mode 8, 20, 36, 256) をパイプラインに自動反映\n\n" +
                    $"選択した {selectedPresets.Count} 件で 8枚二値マスク一括収集と最適化を開始しますか？",
                    "開始", "キャンセル"))
                {
                    controller.Dispatcher.RunOptimizationPresets(selectedPresets, true);
                }
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            if (!hasSelection && targetPresets.Count > 0)
            {
                EditorGUILayout.HelpBox($"⚠️ 実行対象の最適化用プリセットが 0 件です。一覧のチェックボックス (☑️) を ON にしてください。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox($"💡 選択された【{selectedPresets.Count} 件】の各配置から 8枚の二値マスク群を画面保存し、完了後に 256パターンLUT 最適化を自動実行してパイプラインに反映します。", MessageType.None);
            }
        }

        private static void DrawEvaluationButton(
            SICESI_StereoEvaluationController controller,
            List<SICESI_PresetPoseManager.PresetEntry> targetPresets,
            List<SICESI_PresetPoseManager.PresetEntry> selectedPresets,
            int densityCount,
            bool hasSelection)
        {
            string btnText = hasSelection
                ? $"🚀 検証用プリセット (選択中 {selectedPresets.Count} / 全 {targetPresets.Count} 件) の一括IoUベンチマーク実行\n(選択{selectedPresets.Count}配置 × {densityCount}密度 × 5手法: 最終GT比較 & リアルタイムIoU計測)"
                : $"⚠️ 実行対象の検証用プリセットが選択されていません (0 件)";

            if (GUILayout.Button(btnText, GUILayout.Height(52)))
            {
                if (EditorUtility.DisplayDialog(
                    "検証用プリセット一括ベンチマークの開始",
                    $"対象: 選択中 {selectedPresets.Count} 件 (全 {targetPresets.Count} 件中)\n" +
                    $"評価手法: Mode_Average, Mode 8, Mode 20, Mode 36, Mode 256 (計 5 手法)\n" +
                    $"点群密度: {densityCount} 段階\n\n" +
                    $"【処理内容】\n" +
                    $"1. 選択された各配置を自動復元し、最終出力 Ground Truth (GT) 画像をレンダリング撮影\n" +
                    $"2. 5手法でテスト画像を撮影し、GT とのピクセル一致度からリアルタイム IoU を計測\n" +
                    $"3. preset_benchmark_summary.csv に全評価結果を出力\n\n" +
                    $"選択した {selectedPresets.Count} 件で一括ベンチマークを開始しますか？",
                    "開始", "キャンセル"))
                {
                    controller.Dispatcher.RunPresetBenchmark(selectedPresets);
                }
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            if (!hasSelection && targetPresets.Count > 0)
            {
                EditorGUILayout.HelpBox($"⚠️ 実行対象の検証用プリセットが 0 件です。一覧のチェックボックス (☑️) を ON にしてください。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox($"💡 選択された【{selectedPresets.Count} 件】のプリセットを対象に、最終GTと5手法の画像を比較してリアルタイムIoUを計測します。", MessageType.None);
            }
        }
    }
}
