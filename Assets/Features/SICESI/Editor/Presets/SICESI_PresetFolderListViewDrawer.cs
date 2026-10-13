using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// 保存済み JSON プリセットのフォルダー階層グループ表示、
    /// スクロールリスト、および各プリセット行の UI 描画を担当する Drawer クラス。
    /// </summary>
    public static class SICESI_PresetFolderListViewDrawer
    {
        private static Vector2 _scrollPos = Vector2.zero;

        /// <summary>
        /// フォルダー別グループ化スクロールリストを描画します。
        /// </summary>
        public static void Draw(
            SICESI_StereoEvaluationController controller,
            List<SICESI_PresetPoseManager.PresetEntry> displayPresets,
            int currentFilterTab)
        {
            if (controller == null || displayPresets == null) return;

            // フォルダーごとにグルーピング
            var folderGroups = new Dictionary<string, List<SICESI_PresetPoseManager.PresetEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var preset in displayPresets)
            {
                string fName = string.IsNullOrEmpty(preset.folderName) ? "(ルート)" : preset.folderName;
                if (!folderGroups.TryGetValue(fName, out var groupList))
                {
                    groupList = new List<SICESI_PresetPoseManager.PresetEntry>();
                    folderGroups[fName] = groupList;
                }
                groupList.Add(preset);
            }

            int estimatedHeight = Mathf.Min(displayPresets.Count * 46 + folderGroups.Count * 28 + 20, 320);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(estimatedHeight));
            {
                foreach (var kvp in folderGroups)
                {
                    string folderName = kvp.Key;
                    var groupPresets = kvp.Value;

                    DrawFolderGroup(controller, folderName, groupPresets, currentFilterTab);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static void DrawFolderGroup(
            SICESI_StereoEvaluationController controller,
            string folderName,
            List<SICESI_PresetPoseManager.PresetEntry> groupPresets,
            int currentFilterTab)
        {
            int folderSelectedCount = 0;
            foreach (var p in groupPresets)
            {
                if (SICESI_PresetSelectionManager.IsSelected(p.jsonPath)) folderSelectedCount++;
            }

            string foldoutKey = $"Folder_{currentFilterTab}_{folderName}";
            bool isExpanded = SICESI_PresetSelectionManager.IsFolderExpanded(foldoutKey);

            // フォルダーヘッダー
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                bool newExpanded = EditorGUILayout.Foldout(
                    isExpanded,
                    $"📁 <b>{folderName}</b>  ({folderSelectedCount}/{groupPresets.Count}件 選択中)",
                    true,
                    new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold, richText = true });

                if (newExpanded != isExpanded)
                {
                    SICESI_PresetSelectionManager.SetFolderExpanded(foldoutKey, newExpanded);
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("✔", EditorStyles.toolbarButton, GUILayout.Width(26)))
                {
                    SICESI_PresetSelectionManager.SelectAll(groupPresets);
                }

                if (GUILayout.Button("✖", EditorStyles.toolbarButton, GUILayout.Width(26)))
                {
                    SICESI_PresetSelectionManager.DeselectAll(groupPresets);
                }
            }
            EditorGUILayout.EndHorizontal();

            // フォルダー展開時のアイテム一覧
            if (isExpanded)
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < groupPresets.Count; i++)
                {
                    DrawPresetRow(controller, groupPresets[i]);
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2);
            }
        }

        private static void DrawPresetRow(
            SICESI_StereoEvaluationController controller,
            SICESI_PresetPoseManager.PresetEntry preset)
        {
            bool isSelected = SICESI_PresetSelectionManager.IsSelected(preset.jsonPath);
            bool isEval = preset.category == SICESI_PresetPoseManager.PresetCategory.Generalization;

            // 選択されていない行は視覚的に少しトーンダウン
            var rowStyle = new GUIStyle(EditorStyles.textArea);
            EditorGUILayout.BeginHorizontal(rowStyle);
            {
                // チェックボックス
                bool newSelected = EditorGUILayout.Toggle(isSelected, GUILayout.Width(20));
                if (newSelected != isSelected)
                {
                    SICESI_PresetSelectionManager.SetSelected(preset.jsonPath, newSelected);
                }

                // カテゴリバッジ & 詳細
                EditorGUILayout.BeginVertical();
                {
                    string catTag = isEval ? "<color=#D35400><b>[🔬 検証用]</b></color>" : "<color=#1E8449><b>[🪐 最適化用]</b></color>";
                    string titleName = isSelected
                        ? $"<b>{preset.caseName}</b>"
                        : $"<color=gray><s>{preset.caseName}</s> (除外)</color>";

                    EditorGUILayout.LabelField($"{catTag} {titleName}", new GUIStyle(EditorStyles.label) { richText = true });

                    string textColor = isSelected ? "#333333" : "#888888";
                    string detailStr = $"<color={textColor}>VO: {preset.virtualObjectName} ({preset.virtualObjectPosition.x:F2}, {preset.virtualObjectPosition.y:F2}, {preset.virtualObjectPosition.z:F2}) | Hand: {preset.handMeshName}</color>";
                    EditorGUILayout.LabelField(detailStr, new GUIStyle(EditorStyles.miniLabel) { richText = true });
                }
                EditorGUILayout.EndVertical();

                GUILayout.FlexibleSpace();

                // シーン適用ボタン
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
                    string targetFolder = string.IsNullOrEmpty(preset.folderName) ? "" : $" ({preset.folderName})";
                    if (EditorUtility.DisplayDialog("プリセット削除の確認", $"[{preset.category}]{targetFolder} プリセット [{preset.caseName}] を削除しますか？", "削除", "キャンセル"))
                    {
                        SICESI_PresetPoseManager.DeletePreset(preset.jsonPath);
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(1);
        }
    }
}
