using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// SICE SI 追加実験における「保存済み JSON プリセット一覧および一括ベンチマーク実行」セクションを描画するクラス。
    /// カテゴリ別タブフィルター、プリセット一覧のスクロール表示、シーンへの復元適用、削除、および一括実行を担当します。
    /// </summary>
    public static class SICESI_PresetListSectionDrawer
    {
        private static int _listFilterTab = 0; // 0: 検証用, 1: 最適化用, 2: すべて
        private static Vector2 _presetListScroll = Vector2.zero;

        /// <summary>
        /// プリセット一覧および一括実行セクションを描画します。
        /// </summary>
        public static void Draw(SICESI_StereoEvaluationController controller, bool canExecute)
        {
            if (controller == null) return;

            var allPresets = SICESI_PresetPoseManager.LoadPresetEntries(controller.outputDirectory);
            var evalPresets = allPresets.FindAll(p => p.category == SICESI_PresetPoseManager.PresetCategory.Generalization);
            var optPresets = allPresets.FindAll(p => p.category == SICESI_PresetPoseManager.PresetCategory.Optimization);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                // フィルター切り替えタブ
                DrawFilterToolbar(evalPresets.Count, optPresets.Count, allPresets.Count);

                EditorGUILayout.Space(4);

                // 表示対象プリセットの決定
                List<SICESI_PresetPoseManager.PresetEntry> displayPresets = _listFilterTab == 0
                    ? evalPresets
                    : (_listFilterTab == 1 ? optPresets : allPresets);

                if (displayPresets.Count == 0)
                {
                    string emptyMsg = _listFilterTab == 0
                        ? "検証用プリセットがまだありません。上の保存セクションで「🔬 検証用」を選んで保存してください。"
                        : "該当する配置プリセットがありません。";
                    EditorGUILayout.HelpBox(emptyMsg, MessageType.None);
                }
                else
                {
                    DrawPresetScrollList(controller, displayPresets);
                }

                EditorGUILayout.Space(8);

                // 一括実行ボタン
                var targetExecutePresets = _listFilterTab == 1 ? optPresets : evalPresets;
                string targetCategoryName = _listFilterTab == 1 ? "最適化用" : "検証用";
                DrawBenchmarkExecutionButton(controller, targetExecutePresets, targetCategoryName, canExecute);
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawFilterToolbar(int evalCount, int optCount, int allCount)
        {
            EditorGUILayout.BeginHorizontal();
            {
                string[] tabTitles = new string[]
                {
                    $"🔬 検証用 ({evalCount}件)",
                    $"🪐 最適化用 ({optCount}件)",
                    $"すべて ({allCount}件)"
                };
                _listFilterTab = GUILayout.Toolbar(_listFilterTab, tabTitles, GUILayout.Height(24));

                if (GUILayout.Button("🔄", GUILayout.Width(28), GUILayout.Height(24)))
                {
                    GUI.FocusControl(null);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawPresetScrollList(
            SICESI_StereoEvaluationController controller,
            List<SICESI_PresetPoseManager.PresetEntry> displayPresets)
        {
            int displayHeight = Mathf.Min(displayPresets.Count * 48 + 10, 240);
            _presetListScroll = EditorGUILayout.BeginScrollView(_presetListScroll, GUILayout.Height(displayHeight));
            {
                for (int i = 0; i < displayPresets.Count; i++)
                {
                    var preset = displayPresets[i];
                    bool isEval = preset.category == SICESI_PresetPoseManager.PresetCategory.Generalization;

                    EditorGUILayout.BeginHorizontal(EditorStyles.textArea);
                    {
                        // 左側: カテゴリバッジ & 詳細
                        EditorGUILayout.BeginVertical();
                        {
                            string catTag = isEval ? "<color=#D35400><b>[🔬 検証用]</b></color>" : "<color=#1E8449><b>[🪐 最適化用]</b></color>";
                            EditorGUILayout.LabelField($"{catTag} <b>{preset.caseName}</b>", new GUIStyle(EditorStyles.label) { richText = true });

                            string detailStr = $"VO: {preset.virtualObjectName} ({preset.virtualObjectPosition.x:F2}, {preset.virtualObjectPosition.y:F2}, {preset.virtualObjectPosition.z:F2}) | Hand: {preset.handMeshName}";
                            EditorGUILayout.LabelField(detailStr, EditorStyles.miniLabel);
                        }
                        EditorGUILayout.EndVertical();

                        GUILayout.FlexibleSpace();

                        // 右側: シーン適用ボタン
                        GUI.backgroundColor = new Color(0.85f, 0.95f, 0.85f);
                        if (GUILayout.Button("👁️ シーンに適用", GUILayout.Width(95), GUILayout.Height(30)))
                        {
                            SICESI_PresetPoseManager.ApplyPresetToScene(controller, preset.jsonPath);
                            EditorUtility.SetDirty(controller);
                        }

                        // 削除ボタン
                        GUI.backgroundColor = new Color(1.0f, 0.7f, 0.7f);
                        if (GUILayout.Button("🗑️", GUILayout.Width(30), GUILayout.Height(30)))
                        {
                            if (EditorUtility.DisplayDialog("プリセット削除の確認", $"[{preset.category}] プリセット [{preset.caseName}] を削除しますか？", "削除", "キャンセル"))
                            {
                                SICESI_PresetPoseManager.DeletePreset(preset.jsonPath);
                            }
                        }
                        GUI.backgroundColor = Color.white;
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(2);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static void DrawBenchmarkExecutionButton(
            SICESI_StereoEvaluationController controller,
            List<SICESI_PresetPoseManager.PresetEntry> targetPresets,
            string targetCategoryName,
            bool canExecute)
        {
            GUI.enabled = canExecute && targetPresets.Count > 0 && !controller.GeneralizationRunner.isRunning;
            GUI.backgroundColor = targetCategoryName == "最適化用" ? new Color(0.3f, 0.85f, 0.5f) : new Color(1.0f, 0.45f, 0.2f);

            int densityCount = controller.sweepDensities != null ? controller.sweepDensities.Length : 1;
            string btnText = $"🚀 {targetCategoryName}プリセット ({targetPresets.Count} 件) を一括自動実行\n(全{targetPresets.Count}配置 × {densityCount}密度 × 5手法: 最終GT比較 & IoU計測)";

            if (GUILayout.Button(btnText, GUILayout.Height(50)))
            {
                if (EditorUtility.DisplayDialog(
                    $"{targetCategoryName}プリセット一括ベンチマークの開始",
                    $"対象: {targetCategoryName}プリセット {targetPresets.Count} 件\n" +
                    $"評価手法: Mode_Average, Mode 8, Mode 20, Mode 36, Mode 256 (計 5 手法)\n" +
                    $"点群密度: {densityCount} 段階\n\n" +
                    $"【処理内容】\n" +
                    $"1. 各配置を自動復元し、最終出力 Ground Truth (GT) 画像をレンダリング撮影\n" +
                    $"2. 5手法でテスト画像を撮影し、GT とのピクセル一致度からリアルタイム IoU を計測\n" +
                    $"3. preset_benchmark_summary.csv に全評価結果を出力\n\n" +
                    $"一括ベンチマークを開始しますか？",
                    "開始", "キャンセル"))
                {
                    controller.RunPresetBenchmark(targetPresets);
                }
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            EditorGUILayout.HelpBox($"💡 現在【{targetCategoryName}】として保存された JSON プリセット群のみを対象にして一括自動実行します。", MessageType.None);
        }
    }
}
