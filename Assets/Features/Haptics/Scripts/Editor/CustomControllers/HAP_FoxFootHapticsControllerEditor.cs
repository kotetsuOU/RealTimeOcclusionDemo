#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(HAP_FoxFootHapticsController))]
public class HAP_FoxFootHapticsControllerEditor : Editor
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

    private SerializedProperty stmModeProp;
    private SerializedProperty sequentialSTMFrequencyProp;
    private SerializedProperty trackModeProp;

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

        stmModeProp = serializedObject.FindProperty("stmMode");
        sequentialSTMFrequencyProp = serializedObject.FindProperty("sequentialSTMFrequency");
        trackModeProp = serializedObject.FindProperty("trackMode");

        drawGizmosProp = serializedObject.FindProperty("drawGizmos");
        activeColorProp = serializedObject.FindProperty("activeColor");
        inactiveColorProp = serializedObject.FindProperty("inactiveColor");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(autdControllerProp);
        bool isBoneDetectorActive = false;
        var detectorType = System.Type.GetType("Features.Animation.PR_BoneDetector, Assembly-CSharp")
                        ?? System.Type.GetType("PR_BoneDetector, Assembly-CSharp");
        if (detectorType != null && Object.FindFirstObjectByType(detectorType) != null)
        {
            isBoneDetectorActive = true;
        }

        if (isBoneDetectorActive)
        {
            EditorGUILayout.HelpBox("🔒 各足ボーンTransform および RootTransform は PR_BoneDetector により一元同期されています。", MessageType.Info);
        }
        else
        {
            EditorGUILayout.LabelField("Foot Bones (Manual Fallback)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(frontLeftFootProp);
            EditorGUILayout.PropertyField(frontRightFootProp);
            EditorGUILayout.PropertyField(backLeftFootProp);
            EditorGUILayout.PropertyField(backRightFootProp);
            EditorGUILayout.PropertyField(tailBoneProp);
            if (GUILayout.Button("Auto Detect Bones"))
            {
                ((HAP_FoxFootHapticsController)target).AutoDetectBones();
            }
        }
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(enableFrontLeftProp);
        EditorGUILayout.PropertyField(enableFrontRightProp);
        EditorGUILayout.PropertyField(enableBackLeftProp);
        EditorGUILayout.PropertyField(enableBackRightProp);
        EditorGUILayout.PropertyField(enableTailProp);
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(disableWhenInAirProp);
        if (disableWhenInAirProp.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(airborneHeightThresholdProp);
            if (!isBoneDetectorActive)
            {
                EditorGUILayout.PropertyField(rootTransformProp);
            }
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
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(stmModeProp);
        
        // STM Mode に応じた表示の切り替え
        HapticsSTMMode mode = (HapticsSTMMode)stmModeProp.enumValueIndex;
        
        EditorGUI.indentLevel++;
        
        // FociSTM / GainSTM 共通
        EditorGUILayout.PropertyField(sequentialSTMFrequencyProp, new GUIContent("STM Frequency (Hz)"));
        
        if (mode == HapticsSTMMode.GainSTM)
        {
            // GainSTM の場合のみ表示
            EditorGUILayout.PropertyField(trackModeProp);
        }
        else
        {
            // FociSTM の場合は非表示（固定値の案内）
            EditorGUILayout.HelpBox("FociSTM (Hardware STM) forces Track Mode to Sequential and Algorithm to Naive (Hardware single-focus calculation).", MessageType.Info);
        }
        
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(drawGizmosProp);
        if (drawGizmosProp.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(activeColorProp);
            EditorGUILayout.PropertyField(inactiveColorProp);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();
        if (Application.isPlaying)
        {
            if (GUILayout.Button("📋 触覚診断レポートをConsoleに出力 (Log Diagnostics)"))
            {
                ((HAP_BaseObjectHapticsController)target).LogDiagnostics();
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
