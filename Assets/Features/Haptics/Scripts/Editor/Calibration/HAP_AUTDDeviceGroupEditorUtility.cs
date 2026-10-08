#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

#nullable enable

/// <summary>
/// HAP_AUTDDeviceGroup の GUI 描画およびシーン内 AUTD デバイス検索を行う Editor ユーティリティクラス。
/// </summary>
public static class HAP_AUTDDeviceGroupEditorUtility
{
    /// <summary>
    /// シーン内に存在する AUTD3Device の一覧を ID 昇順で取得します。
    /// </summary>
    public static AUTD3Device[] GetSceneAUTDDevices()
    {
        return Object.FindObjectsByType<AUTD3Device>(FindObjectsSortMode.None)
                     .OrderBy(d => d.ID)
                     .ToArray();
    }

    /// <summary>
    /// 各 AUTD デバイスと複数のグループ（例: Contact / Opposite など）の割り当てを
    /// 表（マトリクス）形式のチェックボックスで描画します。
    /// </summary>
    /// <param name="headerLabel">セクションヘッダーラベル</param>
    /// <param name="groups">グループ名と SerializedProperty (selectedDeviceIDs) のタプル配列</param>
    public static void DrawGroupMatrix(string headerLabel, params (string groupName, SerializedProperty groupProp)[] groups)
    {
        var devices = GetSceneAUTDDevices();

        EditorGUILayout.LabelField(headerLabel, EditorStyles.boldLabel);

        if (devices.Length == 0)
        {
            EditorGUILayout.HelpBox("シーン内に AUTD3Device が見つかりません。", MessageType.Warning);
            return;
        }

        EditorGUILayout.BeginVertical(GUI.skin.box);

        // 1. ヘッダー行描画
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("AUTD Device", EditorStyles.miniBoldLabel, GUILayout.Width(180));
        foreach (var (groupName, _) in groups)
        {
            EditorGUILayout.LabelField(groupName, EditorStyles.miniBoldLabel, GUILayout.Width(90));
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);

        // 2. 各デバイス行の描画
        for (int i = 0; i < devices.Length; i++)
        {
            var dev = devices[i];
            int devID = dev.ID;

            EditorGUILayout.BeginHorizontal();
            
            // デバイス名とID
            string devLabel = $"Device #{devID} ({dev.name})";
            EditorGUILayout.LabelField(devLabel, GUILayout.Width(180));

            // 各グループのチェックボックス
            foreach (var (_, groupProp) in groups)
            {
                SerializedProperty idsProp = groupProp.FindPropertyRelative("selectedDeviceIDs");
                bool isSelected = IsIDInList(idsProp, devID);

                EditorGUI.BeginChangeCheck();
                bool newSelected = EditorGUILayout.Toggle(isSelected, GUILayout.Width(90));
                if (EditorGUI.EndChangeCheck())
                {
                    SetIDInList(idsProp, devID, newSelected);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// 単一の HAP_AUTDDeviceGroup のデバイス選択一覧をリスト形式（HAP_AUTDDebugDisabler風）で描画します。
    /// </summary>
    public static void DrawSingleGroupSelector(GUIContent label, SerializedProperty groupProp)
    {
        var devices = GetSceneAUTDDevices();
        SerializedProperty idsProp = groupProp.FindPropertyRelative("selectedDeviceIDs");

        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

        if (devices.Length == 0)
        {
            EditorGUILayout.HelpBox("シーン内に AUTD3Device が見つかりません。", MessageType.Warning);
            return;
        }

        EditorGUI.indentLevel++;
        for (int i = 0; i < devices.Length; i++)
        {
            var dev = devices[i];
            int devID = dev.ID;
            bool isSelected = IsIDInList(idsProp, devID);

            EditorGUI.BeginChangeCheck();
            bool newSelected = EditorGUILayout.ToggleLeft($"Device #{devID} ({dev.name})", isSelected);
            if (EditorGUI.EndChangeCheck())
            {
                SetIDInList(idsProp, devID, newSelected);
            }
        }
        EditorGUI.indentLevel--;
    }

    private static bool IsIDInList(SerializedProperty idsProp, int id)
    {
        if (idsProp == null || !idsProp.isArray) return false;
        for (int i = 0; i < idsProp.arraySize; i++)
        {
            if (idsProp.GetArrayElementAtIndex(i).intValue == id)
            {
                return true;
            }
        }
        return false;
    }

    private static void SetIDInList(SerializedProperty idsProp, int id, bool add)
    {
        if (idsProp == null || !idsProp.isArray) return;

        int existingIndex = -1;
        for (int i = 0; i < idsProp.arraySize; i++)
        {
            if (idsProp.GetArrayElementAtIndex(i).intValue == id)
            {
                existingIndex = i;
                break;
            }
        }

        if (add && existingIndex < 0)
        {
            idsProp.arraySize++;
            idsProp.GetArrayElementAtIndex(idsProp.arraySize - 1).intValue = id;
        }
        else if (!add && existingIndex >= 0)
        {
            idsProp.DeleteArrayElementAtIndex(existingIndex);
        }
    }
}
#endif
