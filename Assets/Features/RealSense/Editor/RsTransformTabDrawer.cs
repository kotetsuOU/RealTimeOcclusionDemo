using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="RsGeneralControllerEditor"/> の Transform タブ描画を担当するヘルパー。
/// </summary>
internal sealed class RsTransformTabDrawer
{
    private static readonly string[] SlotLabels = { "Slot 1", "Slot 2", "Slot 3" };

    public void DrawInspector(RsTransformController controller)
    {
        if (controller == null) return;

        var so = new SerializedObject(controller);
        so.Update();

        var currentSlotIndexProp = so.FindProperty("currentSlotIndex");
        var slot1Prop = so.FindProperty("slot1");
        var slot2Prop = so.FindProperty("slot2");
        var slot3Prop = so.FindProperty("slot3");
        var showCalibrationGuideProp = so.FindProperty("showCalibrationGuide");
        var guideFrameColorProp = so.FindProperty("guideFrameColor");
        var cornerMarkerColorProp = so.FindProperty("cornerMarkerColor");
        var saveFileNameProp = so.FindProperty("saveFileName");

        EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Current Slot", GUILayout.Width(100f));
            int nextSlot = GUILayout.Toolbar(currentSlotIndexProp.intValue, SlotLabels);
            if (nextSlot != currentSlotIndexProp.intValue)
            {
                currentSlotIndexProp.intValue = nextSlot;
            }
        }

        EditorGUILayout.PropertyField(saveFileNameProp);
        EditorGUILayout.Space(2f);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
            if (GUILayout.Button("Save Transforms to JSON", GUILayout.Height(30f)))
            {
                controller.SaveTransformsToJson();
            }

            GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
            if (GUILayout.Button("Load Transforms from JSON", GUILayout.Height(30f)))
            {
                LoadTransformsWithUndo(controller);
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space();

        if (!Application.isPlaying)
        {
            EditorGUILayout.PropertyField(showCalibrationGuideProp);
            if (showCalibrationGuideProp.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("緑色の枠線と赤い球に合わせて、点群の位置を調整してください。", MessageType.Info);

                SerializedProperty currentSlot = GetSlotProperty(currentSlotIndexProp.intValue, slot1Prop, slot2Prop, slot3Prop);
                EditorGUILayout.LabelField($"Slot {currentSlotIndexProp.intValue + 1} Settings", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(currentSlot.FindPropertyRelative("origin"), new GUIContent("Origin (Start Point)"));
                EditorGUILayout.PropertyField(currentSlot.FindPropertyRelative("boxSize"), new GUIContent("Box Size"));

                EditorGUILayout.Space(5f);
                EditorGUILayout.LabelField("Visual Settings", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(guideFrameColorProp);
                EditorGUILayout.PropertyField(cornerMarkerColorProp);
                EditorGUI.indentLevel--;
            }
        }

        so.ApplyModifiedProperties();
    }

    private static SerializedProperty GetSlotProperty(int index, SerializedProperty s1, SerializedProperty s2, SerializedProperty s3)
    {
        switch (index)
        {
            case 1: return s2;
            case 2: return s3;
            default: return s1;
        }
    }

    private static void LoadTransformsWithUndo(RsTransformController controller)
    {
        var globalManager = controller.GetComponent<RsGlobalPointCloudManager>();
        if (globalManager != null)
        {
            foreach (var renderer in globalManager.GetChildRenderers())
            {
                if (renderer != null) Undo.RecordObject(renderer.transform, "Load Child Transforms");
            }
        }

        controller.LoadTransformsFromJson();

        if (globalManager != null)
        {
            foreach (var renderer in globalManager.GetChildRenderers())
            {
                if (renderer != null) EditorUtility.SetDirty(renderer.transform);
            }
        }
        SceneView.RepaintAll();
    }
}

