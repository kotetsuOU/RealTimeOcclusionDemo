using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PointCloudViewer))]
public class PointCloudViewerEditor : Editor
{
    private PointCloudViewer viewer;
    private SerializedObject settingsObject;

    private SerializedProperty fileSettingsProp;
    private SerializedProperty pointSizeProp;
    private SerializedProperty outlineProp, outlineColorProp;
    private SerializedProperty voxelSizeProp, searchRadiusProp, neighborColorProp, neighborThresholdProp;
    private SerializedProperty pointCloudFilterShaderProp;

    void OnEnable()
    {
        viewer = (PointCloudViewer)target;
        var settingsComponent = viewer.GetComponent<PCV_Settings>();
        if (settingsComponent != null)
        {
            settingsObject = new SerializedObject(settingsComponent);
            fileSettingsProp = settingsObject.FindProperty("fileSettings");
            pointSizeProp = settingsObject.FindProperty("pointSize");
            outlineProp = settingsObject.FindProperty("outline");
            outlineColorProp = settingsObject.FindProperty("outlineColor");
            voxelSizeProp = settingsObject.FindProperty("voxelSize");
            searchRadiusProp = settingsObject.FindProperty("searchRadius");
            neighborColorProp = settingsObject.FindProperty("neighborColor");
            neighborThresholdProp = settingsObject.FindProperty("neighborThreshold");
            pointCloudFilterShaderProp = settingsObject.FindProperty("pointCloudFilterShader");
        }
    }

    public override void OnInspectorGUI()
    {
        if (settingsObject == null)
        {
            EditorGUILayout.HelpBox("PCV_Settings component is missing.", MessageType.Error);
            return;
        }
        settingsObject.Update();

        EditorGUILayout.PropertyField(pointSizeProp);
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(fileSettingsProp, true);
        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("すべてON")) SetAllFileUsage(true);
        GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
        if (GUILayout.Button("すべてOFF")) SetAllFileUsage(false);
        EditorGUILayout.EndHorizontal();

        GUI.backgroundColor = new Color(0.8f, 0.8f, 0.6f);
        if (GUILayout.Button("点群を再構築")) viewer.RebuildPointCloud();
        EditorGUILayout.Space();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.PropertyField(outlineProp);
        EditorGUILayout.PropertyField(outlineColorProp);
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(voxelSizeProp);
        EditorGUILayout.PropertyField(searchRadiusProp);
        EditorGUILayout.PropertyField(neighborColorProp);
        EditorGUILayout.PropertyField(neighborThresholdProp);
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(pointCloudFilterShaderProp);

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
        GUI.backgroundColor = Color.white;

        settingsObject.ApplyModifiedProperties();
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