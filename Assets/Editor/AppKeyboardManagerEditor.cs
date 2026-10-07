using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Core.Keyboard;

namespace Core.Editor
{
    [CustomEditor(typeof(AppKeyboardManager))]
    public class AppKeyboardManagerEditor : UnityEditor.Editor
    {
        private readonly Dictionary<string, bool> _foldoutStates = new Dictionary<string, bool>();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var manager = (AppKeyboardManager)target;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "AppKeyboardManager は、アプリケーション全体のキーボード入力を一元集中管理します。\n" +
                "各機能コンポーネント（Weather, PCD, PR等）のキー指定をまとめ、重複（競合）を自動検知します。",
                MessageType.Info);

            EditorGUILayout.Space();
            SerializedProperty globalEnableProp = serializedObject.FindProperty("globalEnableInput");
            EditorGUILayout.PropertyField(globalEnableProp, new GUIContent("Global Enable Keyboard Input"));

            EditorGUILayout.Space();

            // 操作ボタン類
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🔄 Reset to Default Bindings", GUILayout.Height(28)))
            {
                if (EditorUtility.DisplayDialog("デフォルト設定にリセット", "全てのキーバインディングを標準デフォルト値にリセットしますか？", "OK", "キャンセル"))
                {
                    Undo.RecordObject(manager, "Reset to Default Key Bindings");
                    manager.ResetToDefaults();
                    EditorUtility.SetDirty(manager);
                }
            }
            if (GUILayout.Button("🔍 Rebuild Lookup & Check Conflicts", GUILayout.Height(28)))
            {
                manager.BuildLookup();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // キー重複（衝突）の警告表示
            var conflicts = manager.GetConflicts();
            if (conflicts != null && conflicts.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    $"⚠️ キーの重複（衝突）が {conflicts.Count} 件検出されました！\n" +
                    "同一キーが複数アクションに割り当てられているため、動作が意図せず干渉する可能性があります。",
                    MessageType.Warning);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.color = new Color(1f, 0.4f, 0.4f);
                EditorGUILayout.LabelField("【重複リスト】", EditorStyles.boldLabel);
                GUI.color = Color.white;

                foreach (var conflict in conflicts)
                {
                    EditorGUILayout.LabelField($"• {conflict.Key} : [{conflict.CategoryA}] {conflict.ActionA}  ↔  [{conflict.CategoryB}] {conflict.ActionB}");
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
            else
            {
                EditorGUILayout.HelpBox("✅ キー重複はありません（正常）", MessageType.None);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("キーバインド グループ設定", EditorStyles.boldLabel);

            SerializedProperty categoryGroupsProp = serializedObject.FindProperty("categoryGroups");
            if (categoryGroupsProp != null)
            {
                // 衝突キーのハッシュセット
                var conflictedKeys = new HashSet<KeyCode>();
                if (conflicts != null)
                {
                    foreach (var c in conflicts)
                    {
                        conflictedKeys.Add(c.Key);
                    }
                }

                for (int i = 0; i < categoryGroupsProp.arraySize; i++)
                {
                    SerializedProperty groupProp = categoryGroupsProp.GetArrayElementAtIndex(i);
                    SerializedProperty nameProp = groupProp.FindPropertyRelative("categoryName");
                    SerializedProperty isGroupEnabledProp = groupProp.FindPropertyRelative("isCategoryEnabled");
                    SerializedProperty entriesProp = groupProp.FindPropertyRelative("entries");

                    string categoryName = nameProp.stringValue;
                    if (!_foldoutStates.ContainsKey(categoryName))
                    {
                        _foldoutStates[categoryName] = true;
                    }

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    EditorGUILayout.BeginHorizontal();
                    _foldoutStates[categoryName] = EditorGUILayout.Foldout(_foldoutStates[categoryName], categoryName, true, EditorStyles.foldoutHeader);
                    isGroupEnabledProp.boolValue = EditorGUILayout.ToggleLeft("Group Enabled", isGroupEnabledProp.boolValue, GUILayout.Width(110));
                    EditorGUILayout.EndHorizontal();

                    if (_foldoutStates[categoryName])
                    {
                        EditorGUI.indentLevel++;
                        for (int j = 0; j < entriesProp.arraySize; j++)
                        {
                            SerializedProperty entryProp = entriesProp.GetArrayElementAtIndex(j);
                            SerializedProperty actionProp = entryProp.FindPropertyRelative("action");
                            SerializedProperty primaryKeyProp = entryProp.FindPropertyRelative("primaryKey");
                            SerializedProperty secondaryKeyProp = entryProp.FindPropertyRelative("secondaryKey");
                            SerializedProperty displayNameProp = entryProp.FindPropertyRelative("displayName");
                            SerializedProperty descriptionProp = entryProp.FindPropertyRelative("description");
                            SerializedProperty isEntryEnabledProp = entryProp.FindPropertyRelative("isEnabled");

                            KeyCode primaryKey = (KeyCode)primaryKeyProp.intValue;
                            bool isPrimaryConflict = primaryKey != KeyCode.None && conflictedKeys.Contains(primaryKey);

                            KeyCode secondaryKey = (KeyCode)secondaryKeyProp.intValue;
                            bool isSecondaryConflict = secondaryKey != KeyCode.None && conflictedKeys.Contains(secondaryKey);

                            EditorGUILayout.BeginVertical("box");
                            EditorGUILayout.BeginHorizontal();
                            isEntryEnabledProp.boolValue = EditorGUILayout.Toggle(isEntryEnabledProp.boolValue, GUILayout.Width(20));
                            EditorGUILayout.LabelField($"{displayNameProp.stringValue} ({actionProp.enumDisplayNames[actionProp.enumValueIndex]})", EditorStyles.boldLabel);
                            EditorGUILayout.EndHorizontal();

                            if (!string.IsNullOrEmpty(descriptionProp.stringValue))
                            {
                                EditorGUILayout.LabelField(descriptionProp.stringValue, EditorStyles.miniLabel);
                            }

                            EditorGUILayout.BeginHorizontal();
                            if (isPrimaryConflict)
                            {
                                GUI.color = new Color(1f, 0.6f, 0.6f);
                            }
                            EditorGUILayout.PropertyField(primaryKeyProp, new GUIContent("Primary Key"));
                            GUI.color = Color.white;

                            if (isSecondaryConflict)
                            {
                                GUI.color = new Color(1f, 0.6f, 0.6f);
                            }
                            EditorGUILayout.PropertyField(secondaryKeyProp, new GUIContent("Secondary Key"));
                            GUI.color = Color.white;
                            EditorGUILayout.EndHorizontal();

                            EditorGUILayout.EndVertical();
                        }
                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(2);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
