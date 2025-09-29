using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PointCloudViewer))]
public class PointCloudViewerEditor : Editor
{
    // SerializedProperties
    SerializedProperty fileSettingsProp;
    SerializedProperty pointSizeProp;
    SerializedProperty outlineProp, outlineColorProp;
    SerializedProperty voxelSizeProp, searchRadiusProp, neighborColorProp, neighborThresholdProp;

    void OnEnable()
    {
        fileSettingsProp = serializedObject.FindProperty("fileSettings");
        pointSizeProp = serializedObject.FindProperty("pointSize");
        outlineProp = serializedObject.FindProperty("outline");
        outlineColorProp = serializedObject.FindProperty("outlineColor");
        voxelSizeProp = serializedObject.FindProperty("voxelSize");
        searchRadiusProp = serializedObject.FindProperty("searchRadius");
        neighborColorProp = serializedObject.FindProperty("neighborColor");
        neighborThresholdProp = serializedObject.FindProperty("neighborThreshold");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        PointCloudViewer viewer = (PointCloudViewer)target;

        EditorGUILayout.LabelField("Rendering Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(pointSizeProp);
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Data Files", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(fileSettingsProp, true);
        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("すべてON"))
        {
            SetAllFileUsage(true);
        }
        GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
        if (GUILayout.Button("すべてOFF"))
        {
            SetAllFileUsage(false);
        }
        EditorGUILayout.EndHorizontal();

        GUI.backgroundColor = new Color(0.8f, 0.8f, 0.6f);
        if (GUILayout.Button("点群を再構築"))
        {
            viewer.RebuildPointCloud();
        }
        EditorGUILayout.Space();

        GUI.backgroundColor = Color.white;

        EditorGUILayout.LabelField("Outline Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(outlineProp);
        EditorGUILayout.PropertyField(outlineColorProp);
        EditorGUILayout.Space();


        EditorGUILayout.LabelField("Neighbor Search & Filtering", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(voxelSizeProp);
        EditorGUILayout.PropertyField(searchRadiusProp);
        EditorGUILayout.PropertyField(neighborColorProp);
        EditorGUILayout.PropertyField(neighborThresholdProp);

        GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
        if (GUILayout.Button("ノイズ除去を実行"))
        {
            if (UnityEngine.Application.isPlaying)
            {
                viewer.StartNoiseFiltering();
            }
            else
            {
                UnityEngine.Debug.LogWarning("ノイズ除去はプレイモード中のみ実行可能です。");
            }
        }
        
        GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);

        serializedObject.ApplyModifiedProperties();
    }

    private void SetAllFileUsage(bool value)
    {
        for (int i = 0; i < fileSettingsProp.arraySize; i++)
        {
            SerializedProperty element = fileSettingsProp.GetArrayElementAtIndex(i);
            SerializedProperty useFile = element.FindPropertyRelative("useFile");
            useFile.boolValue = value;
        }
    }
}