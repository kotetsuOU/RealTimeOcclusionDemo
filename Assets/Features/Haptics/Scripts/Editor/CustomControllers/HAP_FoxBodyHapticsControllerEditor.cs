#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(HAP_FoxBodyHapticsController))]
public class HAP_FoxBodyHapticsControllerEditor : Editor
{
    private SerializedProperty autdControllerProp;

    // Bone Transforms
    private SerializedProperty headBoneProp;
    private SerializedProperty leftEarBoneProp;
    private SerializedProperty rightEarBoneProp;
    private SerializedProperty frontLeftFootProp;
    private SerializedProperty frontRightFootProp;
    private SerializedProperty backLeftFootProp;
    private SerializedProperty backRightFootProp;
    private SerializedProperty tailBoneProp;

    // Toggles
    private SerializedProperty enableHeadProp;
    private SerializedProperty enableLeftEarProp;
    private SerializedProperty enableRightEarProp;
    private SerializedProperty enableFrontLeftProp;
    private SerializedProperty enableFrontRightProp;
    private SerializedProperty enableBackLeftProp;
    private SerializedProperty enableBackRightProp;
    private SerializedProperty enableTailProp;

    // Touch Directions
    private SerializedProperty headTargetTouchDirectionProp;
    private SerializedProperty footTargetTouchDirectionProp;

    // Airborne / Contact
    private SerializedProperty disableWhenInAirProp;
    private SerializedProperty airborneHeightThresholdProp;
    private SerializedProperty rootTransformProp;
    
    private SerializedProperty onlyTargetHandContactProp;
    private SerializedProperty handContactThresholdProp;
    private SerializedProperty useIndividualContactThresholdsProp;
    private SerializedProperty headContactThresholdProp;
    private SerializedProperty leftEarContactThresholdProp;
    private SerializedProperty rightEarContactThresholdProp;
    private SerializedProperty frontLeftContactThresholdProp;
    private SerializedProperty frontRightContactThresholdProp;
    private SerializedProperty backLeftContactThresholdProp;
    private SerializedProperty backRightContactThresholdProp;
    private SerializedProperty tailContactThresholdProp;

    // STM Settings
    private SerializedProperty stmModeProp;
    private SerializedProperty sequentialSTMFrequencyProp;
    private SerializedProperty trackModeProp;

    // Gizmos
    private SerializedProperty drawGizmosProp;
    private SerializedProperty activeColorProp;
    private SerializedProperty inactiveColorProp;

    private void OnEnable()
    {
        autdControllerProp = serializedObject.FindProperty("autdController");

        headBoneProp = serializedObject.FindProperty("headBone");
        leftEarBoneProp = serializedObject.FindProperty("leftEarBone");
        rightEarBoneProp = serializedObject.FindProperty("rightEarBone");
        frontLeftFootProp = serializedObject.FindProperty("frontLeftFoot");
        frontRightFootProp = serializedObject.FindProperty("frontRightFoot");
        backLeftFootProp = serializedObject.FindProperty("backLeftFoot");
        backRightFootProp = serializedObject.FindProperty("backRightFoot");
        tailBoneProp = serializedObject.FindProperty("tailBone");

        enableHeadProp = serializedObject.FindProperty("enableHead");
        enableLeftEarProp = serializedObject.FindProperty("enableLeftEar");
        enableRightEarProp = serializedObject.FindProperty("enableRightEar");
        enableFrontLeftProp = serializedObject.FindProperty("enableFrontLeft");
        enableFrontRightProp = serializedObject.FindProperty("enableFrontRight");
        enableBackLeftProp = serializedObject.FindProperty("enableBackLeft");
        enableBackRightProp = serializedObject.FindProperty("enableBackRight");
        enableTailProp = serializedObject.FindProperty("enableTail");

        headTargetTouchDirectionProp = serializedObject.FindProperty("headTargetTouchDirection");
        footTargetTouchDirectionProp = serializedObject.FindProperty("footTargetTouchDirection");

        disableWhenInAirProp = serializedObject.FindProperty("disableWhenInAir");
        airborneHeightThresholdProp = serializedObject.FindProperty("airborneHeightThreshold");
        rootTransformProp = serializedObject.FindProperty("rootTransform");

        onlyTargetHandContactProp = serializedObject.FindProperty("onlyTargetHandContact");
        handContactThresholdProp = serializedObject.FindProperty("handContactThreshold");
        useIndividualContactThresholdsProp = serializedObject.FindProperty("useIndividualContactThresholds");
        headContactThresholdProp = serializedObject.FindProperty("headContactThreshold");
        leftEarContactThresholdProp = serializedObject.FindProperty("leftEarContactThreshold");
        rightEarContactThresholdProp = serializedObject.FindProperty("rightEarContactThreshold");
        frontLeftContactThresholdProp = serializedObject.FindProperty("frontLeftContactThreshold");
        frontRightContactThresholdProp = serializedObject.FindProperty("frontRightContactThreshold");
        backLeftContactThresholdProp = serializedObject.FindProperty("backLeftContactThreshold");
        backRightContactThresholdProp = serializedObject.FindProperty("backRightContactThreshold");
        tailContactThresholdProp = serializedObject.FindProperty("tailContactThreshold");

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
            EditorGUILayout.HelpBox("🔒 各ボーンTransform および RootTransform は PR_BoneDetector により一元同期されています。", MessageType.Info);
        }
        else
        {
            EditorGUILayout.LabelField("Bone Transforms (Manual Fallback)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(headBoneProp);
            EditorGUILayout.PropertyField(leftEarBoneProp);
            EditorGUILayout.PropertyField(rightEarBoneProp);
            EditorGUILayout.PropertyField(frontLeftFootProp);
            EditorGUILayout.PropertyField(frontRightFootProp);
            EditorGUILayout.PropertyField(backLeftFootProp);
            EditorGUILayout.PropertyField(backRightFootProp);
            EditorGUILayout.PropertyField(tailBoneProp);

            if (GUILayout.Button("Auto Detect Bones"))
            {
                ((HAP_FoxBodyHapticsController)target).AutoDetectBones();
            }
        }
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Body Part Toggles (Body Layout)", EditorStyles.boldLabel);
        
        // 便利一括操作ボタン
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Select All (全有効)", EditorStyles.miniButtonLeft))
        {
            SetAllToggles(true);
        }
        if (GUILayout.Button("Deselect All (全無効)", EditorStyles.miniButtonMid))
        {
            SetAllToggles(false);
        }
        if (GUILayout.Button("Feet Only (足のみ)", EditorStyles.miniButtonRight))
        {
            enableHeadProp.boolValue = false;
            enableLeftEarProp.boolValue = false;
            enableRightEarProp.boolValue = false;
            enableFrontLeftProp.boolValue = true;
            enableFrontRightProp.boolValue = true;
            enableBackLeftProp.boolValue = true;
            enableBackRightProp.boolValue = true;
            enableTailProp.boolValue = false;
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(3);

        // キツネ身体配置グリッド
        // 1. Head (中央)
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        enableHeadProp.boolValue = EditorGUILayout.ToggleLeft("Head (頭)", enableHeadProp.boolValue, GUILayout.Width(130));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // 2. Ears (左耳 / 右耳)
        EditorGUILayout.BeginHorizontal();
        enableLeftEarProp.boolValue = EditorGUILayout.ToggleLeft("Left Ear (左耳)", enableLeftEarProp.boolValue);
        enableRightEarProp.boolValue = EditorGUILayout.ToggleLeft("Right Ear (右耳)", enableRightEarProp.boolValue);
        EditorGUILayout.EndHorizontal();

        // 3. Front Feet (前左足 / 前右足)
        EditorGUILayout.BeginHorizontal();
        enableFrontLeftProp.boolValue = EditorGUILayout.ToggleLeft("Front Left (前左足)", enableFrontLeftProp.boolValue);
        enableFrontRightProp.boolValue = EditorGUILayout.ToggleLeft("Front Right (前右足)", enableFrontRightProp.boolValue);
        EditorGUILayout.EndHorizontal();

        // 4. Back Feet (後左足 / 後右足)
        EditorGUILayout.BeginHorizontal();
        enableBackLeftProp.boolValue = EditorGUILayout.ToggleLeft("Back Left (後左足)", enableBackLeftProp.boolValue);
        enableBackRightProp.boolValue = EditorGUILayout.ToggleLeft("Back Right (後右足)", enableBackRightProp.boolValue);
        EditorGUILayout.EndHorizontal();

        // 5. Tail (中央)
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        enableTailProp.boolValue = EditorGUILayout.ToggleLeft("Tail (尻尾)", enableTailProp.boolValue, GUILayout.Width(130));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(headTargetTouchDirectionProp, new GUIContent("Head/Ear Target Touch Direction"));
        EditorGUILayout.PropertyField(footTargetTouchDirectionProp, new GUIContent("Foot/Tail Target Touch Direction"));
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(disableWhenInAirProp);
        if (disableWhenInAirProp.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("ℹ️ 接地判定 (Disable When In Air) は足（前左・前右・後左・後右）のみに適用されます。\n頭・耳・尻尾は空中に浮いていても無効化されず、照射対象として維持されます。", MessageType.Info);
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
            EditorGUILayout.PropertyField(handContactThresholdProp, new GUIContent("Default Contact Threshold (共通)", "全部位共通の手の接触距離しきい値（メートル）。"));
            
            EditorGUILayout.Space(2);
            EditorGUILayout.PropertyField(useIndividualContactThresholdsProp, new GUIContent("Use Individual Thresholds (個別設定)", "部位ごとに異なる手の接触判定距離しきい値を設定します。"));

            if (useIndividualContactThresholdsProp.boolValue)
            {
                EditorGUILayout.HelpBox("部位ごとの手の接触しきい値（メートル）を設定します。\n未設定 (<= 0) の部位は上記の共通しきい値が自動適用されます。", MessageType.None);

                DrawIndividualThresholdField(headContactThresholdProp, "Head (頭)");
                DrawIndividualThresholdField(leftEarContactThresholdProp, "Left Ear (左耳)");
                DrawIndividualThresholdField(rightEarContactThresholdProp, "Right Ear (右耳)");
                DrawIndividualThresholdField(frontLeftContactThresholdProp, "Front Left (前左足)");
                DrawIndividualThresholdField(frontRightContactThresholdProp, "Front Right (前右足)");
                DrawIndividualThresholdField(backLeftContactThresholdProp, "Back Left (後左足)");
                DrawIndividualThresholdField(backRightContactThresholdProp, "Back Right (後右足)");
                DrawIndividualThresholdField(tailContactThresholdProp, "Tail (尻尾)");

                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Copy Common to All", EditorStyles.miniButtonLeft))
                {
                    float common = handContactThresholdProp.floatValue;
                    headContactThresholdProp.floatValue = common;
                    leftEarContactThresholdProp.floatValue = common;
                    rightEarContactThresholdProp.floatValue = common;
                    frontLeftContactThresholdProp.floatValue = common;
                    frontRightContactThresholdProp.floatValue = common;
                    backLeftContactThresholdProp.floatValue = common;
                    backRightContactThresholdProp.floatValue = common;
                    tailContactThresholdProp.floatValue = common;
                }
                if (GUILayout.Button("Reset All to Default", EditorStyles.miniButtonRight))
                {
                    headContactThresholdProp.floatValue = -1f;
                    leftEarContactThresholdProp.floatValue = -1f;
                    rightEarContactThresholdProp.floatValue = -1f;
                    frontLeftContactThresholdProp.floatValue = -1f;
                    frontRightContactThresholdProp.floatValue = -1f;
                    backLeftContactThresholdProp.floatValue = -1f;
                    backRightContactThresholdProp.floatValue = -1f;
                    tailContactThresholdProp.floatValue = -1f;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(stmModeProp);
        
        HapticsSTMMode mode = (HapticsSTMMode)stmModeProp.enumValueIndex;
        
        EditorGUI.indentLevel++;
        
        EditorGUILayout.PropertyField(sequentialSTMFrequencyProp, new GUIContent("STM Frequency (Hz)"));
        
        if (mode == HapticsSTMMode.GainSTM)
        {
            EditorGUILayout.PropertyField(trackModeProp);
        }
        else
        {
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

    private void SetAllToggles(bool value)
    {
        enableHeadProp.boolValue = value;
        enableLeftEarProp.boolValue = value;
        enableRightEarProp.boolValue = value;
        enableFrontLeftProp.boolValue = value;
        enableFrontRightProp.boolValue = value;
        enableBackLeftProp.boolValue = value;
        enableBackRightProp.boolValue = value;
        enableTailProp.boolValue = value;
    }

    private void DrawIndividualThresholdField(SerializedProperty prop, string label)
    {
        EditorGUILayout.BeginHorizontal();
        float currentVal = prop.floatValue;
        bool isCustom = currentVal > 0f;

        float displayVal = isCustom ? currentVal : handContactThresholdProp.floatValue;
        EditorGUI.BeginChangeCheck();
        float newVal = EditorGUILayout.Slider(new GUIContent(label), displayVal, 0.005f, 0.30f);
        if (EditorGUI.EndChangeCheck())
        {
            prop.floatValue = newVal;
        }

        if (isCustom)
        {
            if (GUILayout.Button("Reset", EditorStyles.miniButton, GUILayout.Width(50)))
            {
                prop.floatValue = -1f;
            }
        }
        else
        {
            GUI.color = new Color(0.8f, 0.8f, 0.8f, 0.7f);
            GUILayout.Label("(Default)", EditorStyles.miniLabel, GUILayout.Width(50));
            GUI.color = Color.white;
        }
        EditorGUILayout.EndHorizontal();
    }
}
#endif
