#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(HAP_HapticsIllusionFoxFootController))]
public class HAP_HapticsIllusionFoxFootControllerEditor : Editor
{
    private SerializedProperty autdControllerProp;
    private SerializedProperty frontLeftFootProp;
    private SerializedProperty frontRightFootProp;
    private SerializedProperty backLeftFootProp;
    private SerializedProperty backRightFootProp;
    private SerializedProperty tailBoneProp;

    private SerializedProperty enableFrontLeftProp;
    private SerializedProperty enableFrontRightProp;
    private SerializedProperty enableBackLeftProp;
    private SerializedProperty enableBackRightProp;
    private SerializedProperty enableTailProp;

    private SerializedProperty disableWhenInAirProp;
    private SerializedProperty airborneHeightThresholdProp;
    private SerializedProperty rootTransformProp;
    
    private SerializedProperty onlyTargetHandContactProp;
    private SerializedProperty handContactThresholdProp;
    
    private SerializedProperty footTargetTouchDirectionProp;

    // Haptics Illusion Specific Properties
    private SerializedProperty contactDeviceGroupProp;
    private SerializedProperty oppositeDeviceGroupProp;
    private SerializedProperty enableOppositeFocusProp;
    private SerializedProperty contactOffsetProp;
    private SerializedProperty oppositeOffsetProp;
    private SerializedProperty useSTMProp;
    private SerializedProperty stmFrequencyProp;
    private SerializedProperty stmRadiusProp;
    private SerializedProperty stmPointsProp;

    private SerializedProperty drawGizmosProp;
    private SerializedProperty activeColorProp;
    private SerializedProperty inactiveColorProp;

    private void OnEnable()
    {
        autdControllerProp = serializedObject.FindProperty("autdController");
        frontLeftFootProp = serializedObject.FindProperty("frontLeftFoot");
        frontRightFootProp = serializedObject.FindProperty("frontRightFoot");
        backLeftFootProp = serializedObject.FindProperty("backLeftFoot");
        backRightFootProp = serializedObject.FindProperty("backRightFoot");
        tailBoneProp = serializedObject.FindProperty("tailBone");

        enableFrontLeftProp = serializedObject.FindProperty("enableFrontLeft");
        enableFrontRightProp = serializedObject.FindProperty("enableFrontRight");
        enableBackLeftProp = serializedObject.FindProperty("enableBackLeft");
        enableBackRightProp = serializedObject.FindProperty("enableBackRight");
        enableTailProp = serializedObject.FindProperty("enableTail");

        disableWhenInAirProp = serializedObject.FindProperty("disableWhenInAir");
        airborneHeightThresholdProp = serializedObject.FindProperty("airborneHeightThreshold");
        rootTransformProp = serializedObject.FindProperty("rootTransform");

        onlyTargetHandContactProp = serializedObject.FindProperty("onlyTargetHandContact");
        handContactThresholdProp = serializedObject.FindProperty("handContactThreshold");

        footTargetTouchDirectionProp = serializedObject.FindProperty("footTargetTouchDirection");

        // Haptics Illusion Properties
        contactDeviceGroupProp = serializedObject.FindProperty("contactDeviceGroup");
        oppositeDeviceGroupProp = serializedObject.FindProperty("oppositeDeviceGroup");
        enableOppositeFocusProp = serializedObject.FindProperty("enableOppositeFocus");
        contactOffsetProp = serializedObject.FindProperty("contactOffset");
        oppositeOffsetProp = serializedObject.FindProperty("oppositeOffset");
        useSTMProp = serializedObject.FindProperty("useSTM");
        stmFrequencyProp = serializedObject.FindProperty("stmFrequency");
        stmRadiusProp = serializedObject.FindProperty("stmRadius");
        stmPointsProp = serializedObject.FindProperty("stmPoints");

        drawGizmosProp = serializedObject.FindProperty("drawGizmos");
        activeColorProp = serializedObject.FindProperty("activeColor");
        inactiveColorProp = serializedObject.FindProperty("inactiveColor");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox("【Haptics Illusion Mode】\nFoxFootのボーン検出・接触判定を継承し、接点側と反対側から独立した焦点/STMを照射します。各AUTDデバイスの割り当てをチェックボックスで個別に選択・グルーピングできます。", MessageType.Info);
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(autdControllerProp);
        EditorGUILayout.Space();

        // 1. Illusion Settings Section
        EditorGUILayout.LabelField("Illusion Device & Offset Settings", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        
        HAP_AUTDDeviceGroupEditorUtility.DrawGroupMatrix(
            "AUTD Device Group Selection",
            ("Contact (Front)", contactDeviceGroupProp),
            ("Opposite (Back)", oppositeDeviceGroupProp)
        );
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(enableOppositeFocusProp, new GUIContent("Enable Opposite Focus"));
        EditorGUILayout.PropertyField(contactOffsetProp, new GUIContent("Contact Offset"));
        if (enableOppositeFocusProp.boolValue)
        {
            EditorGUILayout.PropertyField(oppositeOffsetProp, new GUIContent("Opposite (Back) Offset"));
        }
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        // 2. STM Settings Section
        EditorGUILayout.LabelField("Illusion STM Parameters", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(useSTMProp, new GUIContent("Use STM (80Hz Circle)"));
        if (useSTMProp.boolValue)
        {
            EditorGUILayout.PropertyField(stmFrequencyProp, new GUIContent("STM Frequency (Hz)"));
            EditorGUILayout.PropertyField(stmRadiusProp, new GUIContent("STM Radius (m)"));
            EditorGUILayout.PropertyField(stmPointsProp, new GUIContent("Points per Cycle"));
        }
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        // 3. Foot Bone Transforms
        EditorGUILayout.LabelField("Foot Bone Transforms", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(frontLeftFootProp);
        EditorGUILayout.PropertyField(frontRightFootProp);
        EditorGUILayout.PropertyField(backLeftFootProp);
        EditorGUILayout.PropertyField(backRightFootProp);
        EditorGUILayout.PropertyField(tailBoneProp);
        if (GUILayout.Button("Auto Detect Bones"))
        {
            ((HAP_HapticsIllusionFoxFootController)target).AutoDetectBones(true);
        }
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        // 4. Foot Toggles
        EditorGUILayout.LabelField("Foot Toggles", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(enableFrontLeftProp);
        EditorGUILayout.PropertyField(enableFrontRightProp);
        EditorGUILayout.PropertyField(enableBackLeftProp);
        EditorGUILayout.PropertyField(enableBackRightProp);
        EditorGUILayout.PropertyField(enableTailProp);
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        // 5. Animation / Hand Contact Settings
        EditorGUILayout.LabelField("Airborne & Hand Contact Settings", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(disableWhenInAirProp);
        if (disableWhenInAirProp.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(airborneHeightThresholdProp);
            EditorGUILayout.PropertyField(rootTransformProp);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.PropertyField(onlyTargetHandContactProp);
        if (onlyTargetHandContactProp.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(handContactThresholdProp);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.PropertyField(footTargetTouchDirectionProp);
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        // 6. Debug Visualization
        EditorGUILayout.LabelField("Debug Visualization", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(drawGizmosProp);
        if (drawGizmosProp.boolValue)
        {
            EditorGUILayout.PropertyField(activeColorProp);
            EditorGUILayout.PropertyField(inactiveColorProp);
        }
        EditorGUI.indentLevel--;

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
