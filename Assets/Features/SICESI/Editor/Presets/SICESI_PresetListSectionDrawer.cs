using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// SICE SI 追加実験における「保存済み JSON プリセット一覧および一括ベンチマーク実行」セクションを描画する Facade クラス。
    /// フィルター切り替えツールバー、全体一括選択バー、一覧描画 (<see cref="SICESI_PresetFolderListViewDrawer"/>)、
    /// および一括実行アクション (<see cref="SICESI_PresetExecutionActionDrawer"/>) を統合して描画します。
    /// </summary>
    public static class SICESI_PresetListSectionDrawer
    {
        private static int _listFilterTab = 0; // 0: 検証用, 1: 最適化用, 2: すべて

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
                // 1. フィルター切り替えタブ
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
                        : (_listFilterTab == 1
                            ? "最適化用プリセットがまだありません。上の保存セクションで「🪐 最適化用」を選んで保存してください。"
                            : "該当する配置プリセットがありません。");
                    EditorGUILayout.HelpBox(emptyMsg, MessageType.None);
                }
                else
                {
                    // 2. 全体一括選択バー (選択件数 & 全選択 / 全解除)
                    DrawSelectionToolbar(displayPresets);

                    EditorGUILayout.Space(2);

                    // 3. フォルダー別グループ化スクロールリスト描画
                    SICESI_PresetFolderListViewDrawer.Draw(controller, displayPresets, _listFilterTab);
                }

                EditorGUILayout.Space(8);

                // 4. 一括実行ボタン & 結果カード描画
                var targetExecutePresets = _listFilterTab == 1 ? optPresets : evalPresets;
                string targetCategoryName = _listFilterTab == 1 ? "最適化用" : "検証用";
                SICESI_PresetExecutionActionDrawer.Draw(controller, targetExecutePresets, targetCategoryName, canExecute);
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

        private static void DrawSelectionToolbar(List<SICESI_PresetPoseManager.PresetEntry> displayPresets)
        {
            int selectedCount = 0;
            foreach (var p in displayPresets)
            {
                if (SICESI_PresetSelectionManager.IsSelected(p.jsonPath)) selectedCount++;
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                string badgeColor = selectedCount > 0 ? "#00AA55" : "#CC3333";
                EditorGUILayout.LabelField(
                    $"<b>選択中: <color={badgeColor}>{selectedCount}</color> / {displayPresets.Count} 件</b>",
                    new GUIStyle(EditorStyles.label) { richText = true },
                    GUILayout.Width(150));

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("✔ 全選択", EditorStyles.toolbarButton, GUILayout.Width(65)))
                {
                    SICESI_PresetSelectionManager.SelectAll(displayPresets);
                }

                if (GUILayout.Button("✖ 全解除", EditorStyles.toolbarButton, GUILayout.Width(65)))
                {
                    SICESI_PresetSelectionManager.DeselectAll(displayPresets);
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
