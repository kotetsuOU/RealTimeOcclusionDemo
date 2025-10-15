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
    private SerializedProperty voxelDensityThresholdProp;
    private SerializedProperty erosionIterationsProp, dilationIterationsProp;
    private SerializedProperty pointCloudFilterShaderProp;
    private SerializedProperty morpologyOperationShaderProp;

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
            voxelDensityThresholdProp = settingsObject.FindProperty("voxelDensityThreshold");
            erosionIterationsProp = settingsObject.FindProperty("erosionIterations");
            dilationIterationsProp = settingsObject.FindProperty("dilationIterations");
            pointCloudFilterShaderProp = settingsObject.FindProperty("pointCloudFilterShader");
            morpologyOperationShaderProp = settingsObject.FindProperty("morpologyOperationShader");
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
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space();

        GUI.backgroundColor = new Color(0.7f, 0.9f, 0.7f);
        if (GUILayout.Button("Voxelごとの点群数をCSV出力"))
        {
            viewer.ExportVoxelCountsToCSV();
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(voxelSizeProp);
        EditorGUILayout.PropertyField(searchRadiusProp);
        EditorGUILayout.PropertyField(neighborColorProp);
        EditorGUILayout.PropertyField(neighborThresholdProp);
        EditorGUILayout.PropertyField(voxelDensityThresholdProp);
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(erosionIterationsProp);
        EditorGUILayout.PropertyField(dilationIterationsProp);
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(pointCloudFilterShaderProp);
        EditorGUILayout.PropertyField(morpologyOperationShaderProp);

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.8f, 1f, 0.8f);
        if (GUILayout.Button("ボクセル密度フィルタリングを実行"))
        {
            viewer.StartVoxelDensityFiltering();
        }
        GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
        if (GUILayout.Button("近傍探索ノイズ除去を実行"))
        {
            viewer.StartNoiseFiltering();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(1f, 0.8f, 0.6f);
        if (GUILayout.Button("モルフォロジー演算を実行 (Morpology)"))
        {
            viewer.StartMorpologyOperation();
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(outlineProp);
        EditorGUILayout.PropertyField(outlineColorProp);

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