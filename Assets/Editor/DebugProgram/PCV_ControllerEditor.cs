using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(PCV_Controller))]
public class PCV_ControllerEditor : Editor
{
    private PCV_Controller controller;
    private SerializedObject settingsObject;
    private PCV_Settings settingsComponent;

    // Profile Management
    private string profileName = "DefaultProfile";
    private bool showProfileSettings = false;

    // Properties
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

    private SerializedProperty neighborNoiseFilterShaderProp;
    private SerializedProperty morpologyOperationShaderProp;
    private SerializedProperty densityFilterShaderProp;
    private SerializedProperty densityComplementationShaderProp;
    private SerializedProperty voxelGridBuilderShaderProp;

    private SerializedProperty useGpuNoiseFilterProp;
    private SerializedProperty useGpuDensityFilterProp;
    private SerializedProperty useGpuDensityComplementationProp;

    // Foldouts
    private bool showDataFiles = false;
    private bool showNeighborSearch = false;
    private bool showMorpologyOperation = false;
    private bool showDensityComplementation = false;
    private bool showGpuAcceleration = false;
    private bool showRenderingSettings = false;
    private bool showOutlineSettings = false;

    void OnEnable()
    {
        controller = (PCV_Controller)target;
        settingsComponent = controller.GetComponent<PCV_Settings>();

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

            neighborNoiseFilterShaderProp = settingsObject.FindProperty("neighborNoiseFilterShader");
            morpologyOperationShaderProp = settingsObject.FindProperty("morpologyOperationShader");
            densityFilterShaderProp = settingsObject.FindProperty("densityFilterShader");
            densityComplementationShaderProp = settingsObject.FindProperty("densityComplementationShader");
            voxelGridBuilderShaderProp = settingsObject.FindProperty("voxelGridBuilderShader");

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

        EditorGUILayout.Space();
        showProfileSettings = EditorGUILayout.Foldout(showProfileSettings, "Profile Management (JSON)", true, EditorStyles.foldoutHeader);
        if (showProfileSettings)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Manage Settings Preset", EditorStyles.miniBoldLabel);
            profileName = EditorGUILayout.TextField("Profile Name", profileName);

            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(0.7f, 0.8f, 1f);
            if (GUILayout.Button("Save Profile"))
            {
                if (EditorUtility.DisplayDialog("Save Profile",
                    $"Save current settings to '{profileName}.json'?", "Save", "Cancel"))
                {
                    PCV_ConfigIO.SaveConfig(settingsComponent, profileName);
                }
            }

            GUI.backgroundColor = new Color(1f, 0.8f, 0.7f);
            if (GUILayout.Button("Load Profile"))
            {
                if (EditorUtility.DisplayDialog("Load Profile",
                    $"Load settings from '{profileName}.json'?\nCurrent settings will be overwritten.", "Load", "Cancel"))
                {
                    PCV_ConfigIO.LoadConfig(settingsComponent, profileName);
                }
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.Space();

        showDataFiles = EditorGUILayout.Foldout(showDataFiles, "Data Files", true, EditorStyles.foldoutHeader);

        if (showDataFiles)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Manual Calibration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("1. Viewerを回転させて [Apply Rotation] を押す\n2. Viewerを移動させて [Apply Position] を押す\n※実行後、ViewerのTransformはリセットされます。", MessageType.Info);

            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(1f, 0.7f, 0.4f); // オレンジ系
            if (GUILayout.Button("Apply Rotation Only"))
            {
                if (EditorUtility.DisplayDialog("Apply Rotation",
                    "現在のViewerの【回転】のみをターゲットに反映します。\n反映後、Viewerの回転はリセットされます。\nよろしいですか？", "Yes", "Cancel"))
                {
                    controller.ApplyRotationCorrection();
                }
            }

            GUI.backgroundColor = new Color(0.4f, 1f, 0.6f); // 緑系
            if (GUILayout.Button("Apply Position Only"))
            {
                if (EditorUtility.DisplayDialog("Apply Position",
                    "現在のViewerの【位置】のみをターゲットに反映します。\n反映後、Viewerの位置はリセットされます。\nよろしいですか？", "Yes", "Cancel"))
                {
                    controller.ApplyPositionCorrection();
                }
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("すべてON")) SetAllFileUsage(true);
        GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
        if (GUILayout.Button("すべてOFF")) SetAllFileUsage(false);
        EditorGUILayout.EndHorizontal();

        GUI.backgroundColor = new Color(0.8f, 0.8f, 0.6f);
        if (GUILayout.Button("点群を再構築")) controller.RebuildPointCloud();
        GUI.backgroundColor = Color.white;

        if (showDataFiles)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(fileSettingsProp, true);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.Space();

        showNeighborSearch = EditorGUILayout.Foldout(showNeighborSearch, "Neighbor Search & Filtering", true, EditorStyles.foldoutHeader);

        GUI.backgroundColor = new Color(0.7f, 0.9f, 0.7f);
        if (GUILayout.Button("Voxelごとの点群数をCSV出力"))
        {
            controller.ExportVoxelCountsToCSV();
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.8f, 1f, 0.8f);
        if (GUILayout.Button("ボクセル密度フィルタリングを実行"))
        {
            controller.StartVoxelDensityFiltering();
        }
        GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
        if (GUILayout.Button("近傍探索フィルタを実行"))
        {
            controller.StartNeighborFiltering();
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
            controller.StartMorpologyOperation();
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
            controller.StartDensityComplementation();
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

        showRenderingSettings = EditorGUILayout.Foldout(showRenderingSettings, "Rendering Settings", true, EditorStyles.foldoutHeader);
        if (showRenderingSettings)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(pointSizeProp);
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
            EditorGUILayout.PropertyField(useGpuNoiseFilterProp, new GUIContent("近傍探索フィルタ (GPU)"));
            EditorGUILayout.PropertyField(useGpuDensityFilterProp, new GUIContent("ボクセル密度フィルタ (GPU)"));
            EditorGUILayout.PropertyField(useGpuDensityComplementationProp, new GUIContent("密度補完 (GPU)"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Compute Shader Assets", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(neighborNoiseFilterShaderProp);
            EditorGUILayout.PropertyField(morpologyOperationShaderProp);
            EditorGUILayout.PropertyField(densityFilterShaderProp);
            EditorGUILayout.PropertyField(densityComplementationShaderProp);
            EditorGUILayout.PropertyField(voxelGridBuilderShaderProp);
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