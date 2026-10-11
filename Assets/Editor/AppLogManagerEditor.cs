using UnityEditor;
using UnityEngine;
using Core.Logging;

namespace Core.Editor
{
    [CustomEditor(typeof(AppLogManager))]
    public class AppLogManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var manager = (AppLogManager)target;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "AppLogManager は、対象コンポーネント (HCD 等) の Inspector を一切汚さず、\n" +
                "本マネージャー上でモジュール機能ごとにフォルダ階層化してログを集中コントロールします。",
                MessageType.Info);

            manager.EnsureDefaultProfiles();

            EditorGUILayout.Space();
            if (GUILayout.Button("🔍 Scan Scene Components", GUILayout.Height(30)))
            {
                Undo.RecordObject(manager, "Scan Components");
                manager.ScanSceneComponents();
                manager.SaveCurrentProfile();
                EditorUtility.SetDirty(manager);
            }

            EditorGUILayout.Space(4);

            // --- Profile Tabs Section ---
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("📁 Log Profiles (タブ切り替え)", EditorStyles.boldLabel);
            if (GUILayout.Button("＋ 新規プロファイル", EditorStyles.miniButton, GUILayout.Width(120)))
            {
                Undo.RecordObject(manager, "Add Log Profile");
                manager.AddNewProfile($"バグ調査 {manager.profiles.Count}");
                EditorUtility.SetDirty(manager);
            }
            EditorGUILayout.EndHorizontal();

            if (manager.profiles != null && manager.profiles.Count > 0)
            {
                string[] tabTitles = new string[manager.profiles.Count];
                for (int p = 0; p < manager.profiles.Count; p++)
                {
                    tabTitles[p] = string.IsNullOrEmpty(manager.profiles[p].profileName)
                        ? $"Profile {p + 1}"
                        : manager.profiles[p].profileName;
                }

                EditorGUI.BeginChangeCheck();
                int currentTabIndex = Mathf.Clamp(manager.activeProfileIndex, 0, manager.profiles.Count - 1);
                int selectedTab = GUILayout.Toolbar(currentTabIndex, tabTitles);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(manager, "Switch Profile");
                    manager.SwitchProfile(selectedTab);
                    EditorUtility.SetDirty(manager);
                }

                // 現在のプロファイル操作ツールバー
                if (manager.activeProfileIndex >= 0 && manager.activeProfileIndex < manager.profiles.Count)
                {
                    var curProfile = manager.profiles[manager.activeProfileIndex];
                    EditorGUILayout.Space(2);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("名前変更:", GUILayout.Width(55));
                    EditorGUI.BeginChangeCheck();
                    string editedName = EditorGUILayout.TextField(curProfile.profileName);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(manager, "Rename Profile");
                        curProfile.profileName = editedName;
                        EditorUtility.SetDirty(manager);
                    }

                    if (GUILayout.Button("複製", EditorStyles.miniButton, GUILayout.Width(45)))
                    {
                        Undo.RecordObject(manager, "Duplicate Profile");
                        manager.DuplicateProfile(manager.activeProfileIndex);
                        EditorUtility.SetDirty(manager);
                    }

                    if (manager.profiles.Count > 1)
                    {
                        if (GUILayout.Button("削除", EditorStyles.miniButton, GUILayout.Width(45)))
                        {
                            if (EditorUtility.DisplayDialog("プロファイル削除", $"プロファイル「{curProfile.profileName}」を削除しますか？", "削除", "キャンセル"))
                            {
                                Undo.RecordObject(manager, "Remove Profile");
                                manager.RemoveProfile(manager.activeProfileIndex);
                                EditorUtility.SetDirty(manager);
                            }
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("All Categories Level Control", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Log ON", EditorStyles.miniButtonLeft)) { Undo.RecordObject(manager, "Log All ON"); manager.SetAllLevelEnabled(AppLogLevel.Info, true); manager.SaveCurrentProfile(); EditorUtility.SetDirty(manager); }
            if (GUILayout.Button("Log OFF", EditorStyles.miniButtonRight)) { Undo.RecordObject(manager, "Log All OFF"); manager.SetAllLevelEnabled(AppLogLevel.Info, false); manager.SaveCurrentProfile(); EditorUtility.SetDirty(manager); }
            GUILayout.Space(6);
            if (GUILayout.Button("Warn ON", EditorStyles.miniButtonLeft)) { Undo.RecordObject(manager, "Warn All ON"); manager.SetAllLevelEnabled(AppLogLevel.Warning, true); manager.SaveCurrentProfile(); EditorUtility.SetDirty(manager); }
            if (GUILayout.Button("Warn OFF", EditorStyles.miniButtonRight)) { Undo.RecordObject(manager, "Warn All OFF"); manager.SetAllLevelEnabled(AppLogLevel.Warning, false); manager.SaveCurrentProfile(); EditorUtility.SetDirty(manager); }
            GUILayout.Space(6);
            if (GUILayout.Button("Err ON", EditorStyles.miniButtonLeft)) { Undo.RecordObject(manager, "Err All ON"); manager.SetAllLevelEnabled(AppLogLevel.Error, true); manager.SaveCurrentProfile(); EditorUtility.SetDirty(manager); }
            if (GUILayout.Button("Err OFF", EditorStyles.miniButtonRight)) { Undo.RecordObject(manager, "Err All OFF"); manager.SetAllLevelEnabled(AppLogLevel.Error, false); manager.SaveCurrentProfile(); EditorUtility.SetDirty(manager); }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Log Target Category Groups", EditorStyles.boldLabel);

            SerializedProperty groupsProp = serializedObject.FindProperty("categoryGroups");
            if (groupsProp != null && groupsProp.arraySize > 0)
            {
                for (int g = 0; g < groupsProp.arraySize; g++)
                {
                    SerializedProperty groupProp = groupsProp.GetArrayElementAtIndex(g);
                    SerializedProperty catNameProp = groupProp.FindPropertyRelative("categoryName");
                    SerializedProperty isExpandedProp = groupProp.FindPropertyRelative("isExpanded");
                    SerializedProperty entriesProp = groupProp.FindPropertyRelative("entries");

                    int count = entriesProp?.arraySize ?? 0;
                    string catName = catNameProp.stringValue;

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    // --- Group Header ---
                    EditorGUILayout.BeginHorizontal();
                    isExpandedProp.boolValue = EditorGUILayout.Foldout(isExpandedProp.boolValue, $"📂 {catName} ({count})", true, EditorStyles.foldoutHeader);

                    if (GUILayout.Button("All ON", EditorStyles.miniButtonLeft, GUILayout.Width(55)))
                    {
                        Undo.RecordObject(manager, "Group All ON");
                        manager.SetGroupEnabled(catName, true);
                        manager.SaveCurrentProfile();
                        EditorUtility.SetDirty(manager);
                    }
                    if (GUILayout.Button("All OFF", EditorStyles.miniButtonRight, GUILayout.Width(55)))
                    {
                        Undo.RecordObject(manager, "Group All OFF");
                        manager.SetGroupEnabled(catName, false);
                        manager.SaveCurrentProfile();
                        EditorUtility.SetDirty(manager);
                    }
                    EditorGUILayout.EndHorizontal();

                    // --- Group Items ---
                    if (isExpandedProp.boolValue && entriesProp != null)
                    {
                        EditorGUILayout.Space(2);

                        // 列ヘッダー
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(18);
                        if (GUILayout.Button(new GUIContent("Log", "情報ログ (Info) 一括切り替え"), EditorStyles.miniButton, GUILayout.Width(30)))
                        {
                            Undo.RecordObject(manager, "Toggle Group Log");
                            bool enable = ToggleGroupLevel(groupProp, "enableInfo");
                            manager.SetGroupEnabled(catName, enable, AppLogLevel.Info);
                            manager.SaveCurrentProfile();
                            EditorUtility.SetDirty(manager);
                        }
                        if (GUILayout.Button(new GUIContent("Warn", "警告ログ (Warning) 一括切り替え"), EditorStyles.miniButton, GUILayout.Width(34)))
                        {
                            Undo.RecordObject(manager, "Toggle Group Warn");
                            bool enable = ToggleGroupLevel(groupProp, "enableWarning");
                            manager.SetGroupEnabled(catName, enable, AppLogLevel.Warning);
                            manager.SaveCurrentProfile();
                            EditorUtility.SetDirty(manager);
                        }
                        if (GUILayout.Button(new GUIContent("Err", "エラーログ (Error) 一括切り替え"), EditorStyles.miniButton, GUILayout.Width(30)))
                        {
                            Undo.RecordObject(manager, "Toggle Group Err");
                            bool enable = ToggleGroupLevel(groupProp, "enableError");
                            manager.SetGroupEnabled(catName, enable, AppLogLevel.Error);
                            manager.SaveCurrentProfile();
                            EditorUtility.SetDirty(manager);
                        }
                        GUILayout.Space(6);
                        EditorGUILayout.LabelField("Target / Label", EditorStyles.miniBoldLabel);
                        EditorGUILayout.EndHorizontal();

                        // 各エントリ行
                        for (int i = 0; i < entriesProp.arraySize; i++)
                        {
                            SerializedProperty entryProp = entriesProp.GetArrayElementAtIndex(i);
                            SerializedProperty labelProp = entryProp.FindPropertyRelative("label");
                            SerializedProperty targetProp = entryProp.FindPropertyRelative("target");
                            SerializedProperty infoProp = entryProp.FindPropertyRelative("enableInfo");
                            SerializedProperty warnProp = entryProp.FindPropertyRelative("enableWarning");
                            SerializedProperty errProp = entryProp.FindPropertyRelative("enableError");

                            EditorGUILayout.BeginHorizontal();
                            GUILayout.Space(18);

                            EditorGUI.BeginChangeCheck();
                            bool newInfo = EditorGUILayout.Toggle(infoProp?.boolValue ?? true, GUILayout.Width(30));
                            bool newWarn = EditorGUILayout.Toggle(warnProp?.boolValue ?? true, GUILayout.Width(34));
                            bool newErr = EditorGUILayout.Toggle(errProp?.boolValue ?? true, GUILayout.Width(30));

                            if (EditorGUI.EndChangeCheck())
                            {
                                if (infoProp != null) infoProp.boolValue = newInfo;
                                if (warnProp != null) warnProp.boolValue = newWarn;
                                if (errProp != null) errProp.boolValue = newErr;
                                serializedObject.ApplyModifiedProperties();
                                manager.SaveCurrentProfile();
                                EditorUtility.SetDirty(manager);
                                manager.BuildLookup();
                            }

                            GUILayout.Space(6);
                            string labelText = !string.IsNullOrEmpty(labelProp?.stringValue)
                                ? labelProp.stringValue
                                : (targetProp.objectReferenceValue != null ? targetProp.objectReferenceValue.name : "Unassigned");

                            EditorGUILayout.LabelField(labelText, GUILayout.MinWidth(140), GUILayout.MaxWidth(260));
                            EditorGUILayout.PropertyField(targetProp, GUIContent.none);

                            if (GUILayout.Button("✕", GUILayout.Width(22)))
                            {
                                entriesProp.DeleteArrayElementAtIndex(i);
                                manager.SaveCurrentProfile();
                                break;
                            }

                            EditorGUILayout.EndHorizontal();
                        }
                    }

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(2);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("ターゲットがありません。[Scan Scene Components] ボタンを押して自動検出してください。", MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();

            if (GUI.changed)
            {
                manager.SaveCurrentProfile();
                manager.BuildLookup();
            }
        }

        private bool ToggleGroupLevel(SerializedProperty groupProp, string propName)
        {
            SerializedProperty entriesProp = groupProp.FindPropertyRelative("entries");
            if (entriesProp == null || entriesProp.arraySize == 0) return true;
            for (int i = 0; i < entriesProp.arraySize; i++)
            {
                SerializedProperty entryProp = entriesProp.GetArrayElementAtIndex(i);
                SerializedProperty p = entryProp.FindPropertyRelative(propName);
                if (p != null && !p.boolValue) return true;
            }
            return false;
        }
    }
}
