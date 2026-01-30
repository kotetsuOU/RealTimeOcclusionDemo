using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RsTransformController))]
public class RsTransformControllerEditor : Editor
{
    private SerializedProperty currentSlotIndexProp;
    private SerializedProperty configFileNameSlot1Prop;
    private SerializedProperty configFileNameSlot2Prop;
    private SerializedProperty configFileNameSlot3Prop;
    private SerializedProperty loadOnStartProp;
    private SerializedProperty showCalibrationGuideProp;
    private SerializedProperty guideFrameColorProp;
    private SerializedProperty cornerMarkerColorProp;
    private SerializedProperty calibrationOriginProp;
    private SerializedProperty calibrationBoxSizeProp;

    private void OnEnable()
    {
        currentSlotIndexProp = serializedObject.FindProperty("currentSlotIndex");
        configFileNameSlot1Prop = serializedObject.FindProperty("configFileNameSlot1");
        configFileNameSlot2Prop = serializedObject.FindProperty("configFileNameSlot2");
        configFileNameSlot3Prop = serializedObject.FindProperty("configFileNameSlot3");
        loadOnStartProp = serializedObject.FindProperty("loadOnStart");
        showCalibrationGuideProp = serializedObject.FindProperty("showCalibrationGuide");
        guideFrameColorProp = serializedObject.FindProperty("guideFrameColor");
        cornerMarkerColorProp = serializedObject.FindProperty("cornerMarkerColor");
        calibrationOriginProp = serializedObject.FindProperty("calibrationOrigin");
        calibrationBoxSizeProp = serializedObject.FindProperty("calibrationBoxSize");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        RsTransformController controller = (RsTransformController)target;

        EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Current Slot", GUILayout.Width(100));
        int newSlotIndex = GUILayout.Toolbar(currentSlotIndexProp.intValue, new string[] { "Slot 1", "Slot 2", "Slot 3" });
        if (newSlotIndex != currentSlotIndexProp.intValue)
        {
            currentSlotIndexProp.intValue = newSlotIndex;
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(5);
        
        EditorGUILayout.LabelField("Slot File Names", EditorStyles.miniBoldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(configFileNameSlot1Prop, new GUIContent("Slot 1"));
        EditorGUILayout.PropertyField(configFileNameSlot2Prop, new GUIContent("Slot 2"));
        EditorGUILayout.PropertyField(configFileNameSlot3Prop, new GUIContent("Slot 3"));
        EditorGUI.indentLevel--;
        
        EditorGUILayout.Space(5);
        EditorGUILayout.PropertyField(loadOnStartProp);

        EditorGUILayout.Space();

        if (!UnityEngine.Application.isPlaying)
        {
            EditorGUILayout.LabelField("Calibration Guide", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(showCalibrationGuideProp);

            if (showCalibrationGuideProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("緑色の枠線と赤い球に合わせて、点群の位置を調整してください。", MessageType.Info);

                EditorGUILayout.PropertyField(calibrationOriginProp, new GUIContent("Origin (Start Point)"));
                EditorGUILayout.PropertyField(calibrationBoxSizeProp, new GUIContent("Box Size"));

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Visual Settings", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(guideFrameColorProp);
                EditorGUILayout.PropertyField(cornerMarkerColorProp);

                EditorGUI.indentLevel--;
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Transform I/O Operations", EditorStyles.boldLabel);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        string currentFileName = controller.CurrentConfigFileName;
        int slotNum = controller.currentSlotIndex + 1;
        bool isValidName = !string.IsNullOrEmpty(currentFileName);
        bool hasFile = controller.HasConfigFile(controller.currentSlotIndex);
        
        EditorGUILayout.LabelField($"Current: Slot {slotNum} ({currentFileName})", EditorStyles.miniLabel);
        
        if (!isValidName)
        {
            EditorGUILayout.HelpBox($"Slot {slotNum} の設定ファイル名が指定されていません。", MessageType.Warning);
        }
        else if (!hasFile)
        {
            EditorGUILayout.HelpBox($"Slot {slotNum} の設定ファイルはまだ存在しません。", MessageType.Info);
        }

        EditorGUI.BeginDisabledGroup(!isValidName);
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.7f, 0.8f, 1f);
        if (GUILayout.Button($"Save to Slot {slotNum}"))
        {
            if (EditorUtility.DisplayDialog("Save Transforms",
                $"Save current transforms to Slot {slotNum} ('{currentFileName}')?\n既存のファイルは上書きされます。", "Yes", "No"))
            {
                controller.SaveTransformConfig();
            }
        }

        GUI.backgroundColor = new Color(1f, 0.8f, 0.7f);
        EditorGUI.BeginDisabledGroup(!hasFile);
        if (GUILayout.Button($"Load from Slot {slotNum}"))
        {
            if (EditorUtility.DisplayDialog("Load Transforms",
                $"Load transforms from Slot {slotNum} ('{currentFileName}')?\n現在の位置は上書きされます。", "Yes", "No"))
            {
                controller.LoadTransformConfig();
            }
        }
        EditorGUI.EndDisabledGroup();

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();
        
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Quick Switch & Load", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        for (int i = 0; i < 3; i++)
        {
            bool slotHasFile = controller.HasConfigFile(i);
            string slotLabel = slotHasFile ? $"Switch to Slot {i + 1}" : $"Slot {i + 1} (empty)";
            EditorGUI.BeginDisabledGroup(!slotHasFile || i == controller.currentSlotIndex);
            if (GUILayout.Button(slotLabel))
            {
                controller.SwitchToSlot(i);
                currentSlotIndexProp.intValue = i;
            }
            EditorGUI.EndDisabledGroup();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        serializedObject.ApplyModifiedProperties();
    }
}