#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(HAP_HapticsIllusionCustomController))]
public class HAP_HapticsIllusionCustomControllerEditor : Editor
{
    private SerializedProperty autdControllerProp;
    private SerializedProperty focusConfigsProp;
    private SerializedProperty drawGizmosProp;

    private void OnEnable()
    {
        autdControllerProp = serializedObject.FindProperty("autdController");
        focusConfigsProp = serializedObject.FindProperty("focusConfigs");
        drawGizmosProp = serializedObject.FindProperty("drawGizmos");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox("【Haptics Illusion Custom Mode】\n各AUTDデバイスに任意の独立焦点/STMターゲットを割り当てて照射します。チェックボックスで対象AUTDデバイスを選択できます。", MessageType.Info);
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(autdControllerProp);
        EditorGUILayout.PropertyField(drawGizmosProp);
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Illusion Focus Target Configurations", EditorStyles.boldLabel);

        for (int i = 0; i < focusConfigsProp.arraySize; i++)
        {
            SerializedProperty elementProp = focusConfigsProp.GetArrayElementAtIndex(i);
            SerializedProperty focusNameProp = elementProp.FindPropertyRelative("focusName");
            SerializedProperty targetTransformProp = elementProp.FindPropertyRelative("targetTransform");
            SerializedProperty assignedDeviceGroupProp = elementProp.FindPropertyRelative("assignedDeviceGroup");
            SerializedProperty offsetPositionProp = elementProp.FindPropertyRelative("offsetPosition");
            SerializedProperty isEnabledProp = elementProp.FindPropertyRelative("isEnabled");
            SerializedProperty useSTMProp = elementProp.FindPropertyRelative("useSTM");
            SerializedProperty stmFrequencyProp = elementProp.FindPropertyRelative("stmFrequency");
            SerializedProperty stmRadiusProp = elementProp.FindPropertyRelative("stmRadius");
            SerializedProperty stmPointsProp = elementProp.FindPropertyRelative("stmPoints");
            SerializedProperty focusIntensityPascalProp = elementProp.FindPropertyRelative("focusIntensityPascal");

            string title = string.IsNullOrEmpty(focusNameProp.stringValue) ? $"Target [{i}]" : focusNameProp.stringValue;
            
            EditorGUILayout.BeginVertical(GUI.skin.box);
            elementProp.isExpanded = EditorGUILayout.Foldout(elementProp.isExpanded, title, true);

            if (elementProp.isExpanded)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(focusNameProp);
                EditorGUILayout.PropertyField(isEnabledProp);
                EditorGUILayout.PropertyField(targetTransformProp);

                // AUTD Device Selection Checkboxes
                HAP_AUTDDeviceGroupEditorUtility.DrawSingleGroupSelector(new GUIContent("Assigned AUTD Devices"), assignedDeviceGroupProp);
                EditorGUILayout.Space(4);

                EditorGUILayout.PropertyField(offsetPositionProp);
                EditorGUILayout.PropertyField(useSTMProp);
                if (useSTMProp.boolValue)
                {
                    EditorGUILayout.PropertyField(stmFrequencyProp);
                    EditorGUILayout.PropertyField(stmRadiusProp);
                    EditorGUILayout.PropertyField(stmPointsProp);
                }
                EditorGUILayout.PropertyField(focusIntensityPascalProp);

                EditorGUILayout.Space(4);
                if (GUILayout.Button("Remove Target", GUILayout.Width(140)))
                {
                    focusConfigsProp.DeleteArrayElementAtIndex(i);
                    EditorGUI.indentLevel--;
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        if (GUILayout.Button("Add New Target Configuration"))
        {
            focusConfigsProp.arraySize++;
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
