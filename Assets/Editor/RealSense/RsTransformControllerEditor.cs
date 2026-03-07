using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RsTransformController))]
public class RsTransformControllerEditor : Editor
{
    private SerializedProperty currentSlotIndexProp;
    private SerializedProperty slot1Prop;
    private SerializedProperty slot2Prop;
    private SerializedProperty slot3Prop;
    private SerializedProperty showCalibrationGuideProp;
    private SerializedProperty guideFrameColorProp;
    private SerializedProperty cornerMarkerColorProp;

    private void OnEnable()
    {
        currentSlotIndexProp = serializedObject.FindProperty("currentSlotIndex");
        slot1Prop = serializedObject.FindProperty("slot1");
        slot2Prop = serializedObject.FindProperty("slot2");
        slot3Prop = serializedObject.FindProperty("slot3");
        showCalibrationGuideProp = serializedObject.FindProperty("showCalibrationGuide");
        guideFrameColorProp = serializedObject.FindProperty("guideFrameColor");
        cornerMarkerColorProp = serializedObject.FindProperty("cornerMarkerColor");
    }

    private SerializedProperty GetCurrentSlotProp()
    {
        switch (currentSlotIndexProp.intValue)
        {
            case 0: return slot1Prop;
            case 1: return slot2Prop;
            case 2: return slot3Prop;
            default: return slot1Prop;
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Current Slot", GUILayout.Width(100));
        int newSlotIndex = GUILayout.Toolbar(currentSlotIndexProp.intValue, new string[] { "Slot 1", "Slot 2", "Slot 3" });
        if (newSlotIndex != currentSlotIndexProp.intValue)
        {
            currentSlotIndexProp.intValue = newSlotIndex;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        if (!UnityEngine.Application.isPlaying)
        {
            EditorGUILayout.LabelField("Calibration Guide", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(showCalibrationGuideProp);

            if (showCalibrationGuideProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("緑色の枠線と赤い球に合わせて、点群の位置を調整してください。", MessageType.Info);

                SerializedProperty currentSlot = GetCurrentSlotProp();
                int slotNum = currentSlotIndexProp.intValue + 1;
                EditorGUILayout.LabelField($"Slot {slotNum} Settings", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(currentSlot.FindPropertyRelative("origin"), new GUIContent("Origin (Start Point)"));
                EditorGUILayout.PropertyField(currentSlot.FindPropertyRelative("boxSize"), new GUIContent("Box Size"));

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Visual Settings", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(guideFrameColorProp);
                EditorGUILayout.PropertyField(cornerMarkerColorProp);

                EditorGUI.indentLevel--;
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}