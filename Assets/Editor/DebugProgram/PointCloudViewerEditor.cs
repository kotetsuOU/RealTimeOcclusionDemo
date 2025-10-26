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
    private SerializedProperty complementationDensityThresholdProp;
    private SerializedProperty complementationPointsPerAxisProp;
    private SerializedProperty complementationPointColorProp;
    private SerializedProperty complementationRandomPlacementProp;

    private SerializedProperty pointCloudFilterShaderProp;
    private SerializedProperty morpologyOperationShaderProp;
    private SerializedProperty densityFilterShaderProp;
    private SerializedProperty densityComplementationShaderProp;

    private SerializedProperty useGpuNoiseFilterProp;
    private SerializedProperty useGpuDensityFilterProp;
    private SerializedProperty useGpuDensityComplementationProp;

    private bool showDataFiles = true;
    private bool showRenderingSettings = true;
    private bool showNeighborSearch = true;
    private bool showMorpologyOperation = true;
    private bool showDensityComplementation = true;
    private bool showGpuAcceleration = true;
    private bool showOutlineSettings = true;

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
            complementationDensityThresholdProp = settingsObject.FindProperty("complementationDensityThreshold");
            complementationPointsPerAxisProp = settingsObject.FindProperty("complementationPointsPerAxis");
            complementationPointColorProp = settingsObject.FindProperty("complementationPointColor");
            complementationRandomPlacementProp = settingsObject.FindProperty("complementationRandomPlacement");

            pointCloudFilterShaderProp = settingsObject.FindProperty("pointCloudFilterShader");
            morpologyOperationShaderProp = settingsObject.FindProperty("morpologyOperationShader");
            densityFilterShaderProp = settingsObject.FindProperty("densityFilterShader");
            densityComplementationShaderProp = settingsObject.FindProperty("densityComplementationShader");

            useGpuNoiseFilterProp = settingsObject.FindProperty("useGpuNoiseFilter");
            useGpuDensityFilterProp = settingsObject.FindProperty("useGpuDensityFilter");
            useGpuDensityComplementationProp = settingsObject.FindProperty("useGpuDensityComplementation");
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

        showDataFiles = EditorGUILayout.Foldout(showDataFiles, "Data Files", true, EditorStyles.foldoutHeader);

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("すべてON")) SetAllFileUsage(true);
        GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
        if (GUILayout.Button("すべてOFF")) SetAllFileUsage(false);
        EditorGUILayout.EndHorizontal();

        GUI.backgroundColor = new Color(0.8f, 0.8f, 0.6f);
        if (GUILayout.Button("点群を再構築")) viewer.RebuildPointCloud();
        GUI.backgroundColor = Color.white;

        if (showDataFiles)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(fileSettingsProp, true);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.Space();

        showRenderingSettings = EditorGUILayout.Foldout(showRenderingSettings, "Rendering Settings", true, EditorStyles.foldoutHeader);
        if (showRenderingSettings)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(pointSizeProp);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.Space();

        showNeighborSearch = EditorGUILayout.Foldout(showNeighborSearch, "Neighbor Search & Filtering", true, EditorStyles.foldoutHeader);

        GUI.backgroundColor = new Color(0.7f, 0.9f, 0.7f);
        if (GUILayout.Button("Voxelごとの点群数をCSV出力"))
        {
            viewer.ExportVoxelCountsToCSV();
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.Space();

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
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        if (showNeighborSearch)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(voxelSizeProp);
            EditorGUILayout.PropertyField(searchRadiusProp);
            EditorGUILayout.PropertyField(neighborColorProp);
            EditorGUILayout.PropertyField(neighborThresholdProp);
            EditorGUILayout.PropertyField(voxelDensityThresholdProp);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.Space();

        showMorpologyOperation = EditorGUILayout.Foldout(showMorpologyOperation, "Morpology Operation", true, EditorStyles.foldoutHeader);

        GUI.backgroundColor = new Color(1f, 0.8f, 0.6f);
        if (GUILayout.Button("モルフォロジー演算を実行 (Morpology)"))
        {
            viewer.StartMorpologyOperation();
        }
        GUI.backgroundColor = Color.white;

        if (showMorpologyOperation)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(erosionIterationsProp);
            EditorGUILayout.PropertyField(dilationIterationsProp);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.Space();

        showDensityComplementation = EditorGUILayout.Foldout(showDensityComplementation, "Density Complementation", true, EditorStyles.foldoutHeader);

        GUI.backgroundColor = new Color(1f, 0.7f, 1f);
        if (GUILayout.Button("密度補完を実行"))
        {
            viewer.StartDensityComplementation();
        }
        GUI.backgroundColor = Color.white;

        if (showDensityComplementation)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(complementationDensityThresholdProp);
            EditorGUILayout.PropertyField(complementationPointsPerAxisProp);
            EditorGUILayout.PropertyField(complementationPointColorProp);
            EditorGUILayout.PropertyField(complementationRandomPlacementProp);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.Space();

        showGpuAcceleration = EditorGUILayout.Foldout(showGpuAcceleration, "GPU Acceleration", true, EditorStyles.foldoutHeader);

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("すべて GPU ON")) SetAllGpuUsage(true);
        GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
        if (GUILayout.Button("すべて GPU OFF (CPU)")) SetAllGpuUsage(false);
        EditorGUILayout.EndHorizontal();
        GUI.backgroundColor = Color.white;

        if (showGpuAcceleration)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(useGpuNoiseFilterProp, new GUIContent("近傍探索ノイズ除去 (GPU)"));
            EditorGUILayout.PropertyField(useGpuDensityFilterProp, new GUIContent("ボクセル密度フィルタ (GPU)"));
            EditorGUILayout.PropertyField(useGpuDensityComplementationProp, new GUIContent("密度補完 (GPU)"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Compute Shader Assets", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(pointCloudFilterShaderProp);
            EditorGUILayout.PropertyField(morpologyOperationShaderProp);
            EditorGUILayout.PropertyField(densityFilterShaderProp);
            EditorGUILayout.PropertyField(densityComplementationShaderProp);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.Space();

        showOutlineSettings = EditorGUILayout.Foldout(showOutlineSettings, "Outline Settings", true, EditorStyles.foldoutHeader);
        if (showOutlineSettings)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(outlineProp);
            EditorGUILayout.PropertyField(outlineColorProp);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.Space();

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

    private void SetAllGpuUsage(bool value)
    {
        useGpuNoiseFilterProp.boolValue = value;
        useGpuDensityFilterProp.boolValue = value;
        useGpuDensityComplementationProp.boolValue = value;
    }
}