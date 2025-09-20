#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PointCloudViewer))]
public class PointCloudViewerrEditor : Editor
{
    // Foldout 管理用
    private bool showFileSettings = true;
    private bool showPerFileSettings = true;
    private bool showOutlineSettings = true;

    // SerializedProperties
    SerializedProperty filePath1Prop, filePath2Prop, filePath3Prop, filePath4Prop;
    SerializedProperty pointSizeProp;

    SerializedProperty useFile1Prop, color1Prop;
    SerializedProperty useFile2Prop, color2Prop;
    SerializedProperty useFile3Prop, color3Prop;
    SerializedProperty useFile4Prop, color4Prop;

    SerializedProperty outlineProp, outlineColorProp;

    void OnEnable()
    {
        filePath1Prop = serializedObject.FindProperty("filePath1");
        filePath2Prop = serializedObject.FindProperty("filePath2");
        filePath3Prop = serializedObject.FindProperty("filePath3");
        filePath4Prop = serializedObject.FindProperty("filePath4");
        pointSizeProp = serializedObject.FindProperty("pointSize");

        useFile1Prop = serializedObject.FindProperty("useFile1");
        color1Prop = serializedObject.FindProperty("color1");
        useFile2Prop = serializedObject.FindProperty("useFile2");
        color2Prop = serializedObject.FindProperty("color2");
        useFile3Prop = serializedObject.FindProperty("useFile3");
        color3Prop = serializedObject.FindProperty("color3");
        useFile4Prop = serializedObject.FindProperty("useFile4");
        color4Prop = serializedObject.FindProperty("color4");

        outlineProp = serializedObject.FindProperty("outline");
        outlineColorProp = serializedObject.FindProperty("outlineColor");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Point Cloud Files セクション
        showFileSettings = EditorGUILayout.Foldout(showFileSettings, "Point Cloud Files", true);
        if (showFileSettings)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(filePath1Prop);
            EditorGUILayout.PropertyField(filePath2Prop);
            EditorGUILayout.PropertyField(filePath3Prop);
            EditorGUILayout.PropertyField(filePath4Prop);
            EditorGUILayout.PropertyField(pointSizeProp);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        // Per-File Settings セクション
        showPerFileSettings = EditorGUILayout.Foldout(showPerFileSettings, "Per-File Settings", true);
        if (showPerFileSettings)
        {
            EditorGUI.indentLevel++;
            DrawFileToggleWithColor(useFile1Prop, color1Prop, "Use File 1", "Color 1");
            DrawFileToggleWithColor(useFile2Prop, color2Prop, "Use File 2", "Color 2");
            DrawFileToggleWithColor(useFile3Prop, color3Prop, "Use File 3", "Color 3");
            DrawFileToggleWithColor(useFile4Prop, color4Prop, "Use File 4", "Color 4");

            EditorGUILayout.Space();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("すべてON"))
            {
                useFile1Prop.boolValue = true;
                useFile2Prop.boolValue = true;
                useFile3Prop.boolValue = true;
                useFile4Prop.boolValue = true;
            }
            if (GUILayout.Button("すべてOFF"))
            {
                useFile1Prop.boolValue = false;
                useFile2Prop.boolValue = false;
                useFile3Prop.boolValue = false;
                useFile4Prop.boolValue = false;
            }
            GUILayout.EndHorizontal();

            EditorGUILayout.Space();

            if (GUILayout.Button("再読み込み"))
            {
                ((PointCloudViewer)target).RebuildMesh();
            }

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        // Outline Settings セクション
        showOutlineSettings = EditorGUILayout.Foldout(showOutlineSettings, "Outline Settings", true);
        if (showOutlineSettings)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(outlineProp);
            EditorGUILayout.PropertyField(outlineColorProp);
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawFileToggleWithColor(SerializedProperty toggle, SerializedProperty color, string toggleLabel, string colorLabel)
    {
        EditorGUILayout.PropertyField(toggle, new GUIContent(toggleLabel));
        if (toggle.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(color, new GUIContent(colorLabel));
            EditorGUI.indentLevel--;
        }
    }
}
#endif