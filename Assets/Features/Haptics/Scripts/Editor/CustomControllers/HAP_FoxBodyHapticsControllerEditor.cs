#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Features.Haptics.Editor;

/// <summary>
/// <see cref="HAP_FoxBodyHapticsController"/> 用カスタム Inspector。
/// ボーン・身体トグル・しきい値セクションの細部描画は <see cref="HAP_FoxBodyInspectorSections"/> へ委譲します。
/// </summary>
[CustomEditor(typeof(HAP_FoxBodyHapticsController))]
public class HAP_FoxBodyHapticsControllerEditor : Editor
{
    private SerializedProperty autdControllerProp;

    private SerializedProperty headBoneProp;
    private SerializedProperty leftEarBoneProp;
    private SerializedProperty rightEarBoneProp;
    private SerializedProperty frontLeftFootProp;
    private SerializedProperty frontRightFootProp;
    private SerializedProperty backLeftFootProp;
    private SerializedProperty backRightFootProp;
    private SerializedProperty tailBoneProp;

    private SerializedProperty enableHeadProp;
    private SerializedProperty enableLeftEarProp;
    private SerializedProperty enableRightEarProp;
    private SerializedProperty enableFrontLeftProp;
    private SerializedProperty enableFrontRightProp;
    private SerializedProperty enableBackLeftProp;
    private SerializedProperty enableBackRightProp;
    private SerializedProperty enableTailProp;

    private SerializedProperty headTargetTouchDirectionProp;
    private SerializedProperty footTargetTouchDirectionProp;

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

    private SerializedProperty stmModeProp;
    private SerializedProperty sequentialSTMFrequencyProp;
    private SerializedProperty trackModeProp;

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
        bool isBoneDetectorActive = IsBoneDetectorActive();
        DrawBoneSection(isBoneDetectorActive);
        EditorGUILayout.Space();

        HAP_FoxBodyInspectorSections.DrawBodyPartLayoutSection(
            enableHeadProp, enableLeftEarProp, enableRightEarProp,
            enableFrontLeftProp, enableFrontRightProp, enableBackLeftProp, enableBackRightProp, enableTailProp);
        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(headTargetTouchDirectionProp, new GUIContent("Head/Ear Target Touch Direction"));
        EditorGUILayout.PropertyField(footTargetTouchDirectionProp, new GUIContent("Foot/Tail Target Touch Direction"));
        EditorGUILayout.Space();

        DrawAirborneAndContactSections(isBoneDetectorActive);
        EditorGUILayout.Space();

        DrawStmAndGizmoSections();

        if (Application.isPlaying)
        {
            EditorGUILayout.Space();
            if (GUILayout.Button("📋 触覚診断レポートをConsoleに出力 (Log Diagnostics)"))
            {
                ((HAP_BaseObjectHapticsController)target).LogDiagnostics();
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static bool IsBoneDetectorActive()
    {
        var detectorType = System.Type.GetType("Features.Animation.PR_BoneDetector, Assembly-CSharp")
                        ?? System.Type.GetType("PR_BoneDetector, Assembly-CSharp");
        return detectorType != null && Object.FindFirstObjectByType(detectorType) != null;
    }

    private void DrawBoneSection(bool isBoneDetectorActive)
    {
        if (isBoneDetectorActive)
        {
            EditorGUILayout.HelpBox("🔒 各ボーンTransform および RootTransform は PR_BoneDetector により一元同期されています。", MessageType.Info);
            return;
        }

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

    private void DrawAirborneAndContactSections(bool isBoneDetectorActive)
    {
        EditorGUILayout.PropertyField(disableWhenInAirProp);
        if (disableWhenInAirProp.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "ℹ️ 接地判定 (Disable When In Air) は足（前左・前右・後左・後右）のみに適用されます。\n頭・耳・尻尾は空中に浮いていても無効化されず、照射対象として維持されます。",
                MessageType.Info);
            EditorGUILayout.PropertyField(airborneHeightThresholdProp);
            if (!isBoneDetectorActive) EditorGUILayout.PropertyField(rootTransformProp);
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
                HAP_FoxBodyInspectorSections.DrawIndividualThresholdsSection(
                    handContactThresholdProp,
                    headContactThresholdProp, leftEarContactThresholdProp, rightEarContactThresholdProp,
                    frontLeftContactThresholdProp, frontRightContactThresholdProp, backLeftContactThresholdProp, backRightContactThresholdProp, tailContactThresholdProp);
            }
            EditorGUI.indentLevel--;
        }
    }

    private void DrawStmAndGizmoSections()
    {
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
    }
}
#endif
