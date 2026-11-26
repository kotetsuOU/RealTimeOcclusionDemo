#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Debug;
using Features.Haptics.Editor;

/// <summary>
/// <see cref="HAP_AUTDCalibration"/> 用カスタム Inspector。
/// PlayMode 状態の保存・復元は <see cref="HAP_CalibrationStatePersistence"/> へ委譲します。
/// </summary>
[CustomEditor(typeof(HAP_AUTDCalibration))]
public class HAP_AUTDCalibrationEditor : Editor
{
    private HAP_AUTDCalibration script = null!;

    private void OnEnable()
    {
        script = (HAP_AUTDCalibration)target;

        // Playモード終了時のみ復元する（Playモード突入時の誤復元を防ぐ）
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            HAP_CalibrationStatePersistence.RestoreState(script, serializedObject);
        }
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
        {
            HAP_CalibrationStatePersistence.SaveState(script);
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUI.BeginChangeCheck();
        DrawControllerReferences();
        EditorGUILayout.Space();

        GUI.color = script.enableCalibration ? new Color(1f, 0.6f, 0.6f) : Color.white;
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.PropertyField(serializedObject.FindProperty("enableCalibration"));
        EditorGUILayout.EndVertical();
        GUI.color = Color.white;
        EditorGUILayout.Space();

        DrawTargetDevicesList();
        EditorGUILayout.Space();
        DrawFocusSettings();

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            if (Application.isPlaying) HAP_CalibrationStatePersistence.SaveState(script);
        }

        EditorGUILayout.Space(10);
        DrawCalibrationTools();
    }

    private void DrawControllerReferences()
    {
        var autdProp = serializedObject.FindProperty("autdController") ?? serializedObject.FindProperty("hapticsController");
        if (autdProp != null) EditorGUILayout.PropertyField(autdProp);

        var transformLoaderProp = serializedObject.FindProperty("transformLoader");
        if (transformLoaderProp != null) EditorGUILayout.PropertyField(transformLoaderProp);
    }

    private void DrawTargetDevicesList()
    {
        EditorGUILayout.LabelField("Target Devices (Check to Enable)", EditorStyles.boldLabel);

        var devices = FindObjectsByType<AUTD3Device>(FindObjectsSortMode.None);
        int devCount = devices.Length;
        if (devCount == 0)
        {
            EditorGUILayout.HelpBox("No AUTD3Device found in the scene.", MessageType.Warning);
            return;
        }

        if (script.targetDevices.Count != devCount)
        {
            while (script.targetDevices.Count < devCount) script.targetDevices.Add(true);
            if (script.targetDevices.Count > devCount) script.targetDevices.RemoveRange(devCount, script.targetDevices.Count - devCount);
            EditorUtility.SetDirty(script);
        }

        EditorGUI.indentLevel++;
        for (int i = 0; i < script.targetDevices.Count; i++)
        {
            script.targetDevices[i] = EditorGUILayout.Toggle($"Device [{i}]", script.targetDevices[i]);
        }
        EditorGUI.indentLevel--;
    }

    private void DrawFocusSettings()
    {
        EditorGUILayout.PropertyField(serializedObject.FindProperty("useMultiFocus"));

        if (script.useMultiFocus)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("multiFocusPositions"), true);
        }
        else
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("singleFocusTarget"));
            if (script.singleFocusTarget == null)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("singleFocusPosition"));
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("truePositionTarget"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("focusAmplitude"));
    }

    private void DrawCalibrationTools()
    {
        EditorGUILayout.LabelField("Calibration Tools", EditorStyles.boldLabel);

        if (GUILayout.Button("Apply Transform Offset (Legacy)", GUILayout.Height(24)))
        {
            ExecuteWithTransformLoader(() =>
            {
                Undo.RecordObject(script.transformLoader, "Apply AUTD Offset");
                Undo.RecordObject(script.transform, "Reset Calibration Transform");
                script.ApplyOffset();
                EditorUtility.SetDirty(script.transformLoader);
                AppLogger.Log(script, HAP_LogTriggers.TagCalibration, "[Calibration] Applied legacy offset to AUTD TransformLoader.");
            });
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Calculate & Add Offset\n(FocusTarget -> TruePosition)", GUILayout.Height(36)))
        {
            ExecuteWithTransformLoader(() =>
            {
                Undo.RecordObject(script.transformLoader, "Calculate & Add AUTD Offset");
                script.ApplyOffsetByDifference();
                EditorUtility.SetDirty(script.transformLoader);
            });
        }

        EditorGUILayout.Space();
        DrawBakeSection();

        if (script.transformLoader != null)
        {
            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            Vector3 newOffset = EditorGUILayout.Vector3Field("Current Offset", script.transformLoader.offset);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(script.transformLoader, "Edit AUTD Offset");
                script.transformLoader.offset = newOffset;
                EditorUtility.SetDirty(script.transformLoader);
                if (Application.isPlaying) HAP_CalibrationStatePersistence.SaveState(script);
            }
        }
    }

    private void DrawBakeSection()
    {
        GUI.enabled = !Application.isPlaying;
        GUI.backgroundColor = new Color(1f, 0.8f, 0.5f);
        if (GUILayout.Button("Bake Offset to Devices\n(Reset Offset & Move Devices)", GUILayout.Height(36)))
        {
            ExecuteWithTransformLoader(() =>
            {
                var allDevices = FindObjectsByType<AUTD3Device>(FindObjectsSortMode.None).OrderBy(d => d.ID).ToArray();
                for (int i = 0; i < allDevices.Length; i++)
                {
                    if (i < script.targetDevices.Count && script.targetDevices[i])
                    {
                        Undo.RecordObject(allDevices[i].transform, "Bake AUTD Offset to Device");
                    }
                }
                Undo.RecordObject(script.transformLoader, "Bake AUTD Offset (Reset)");
                script.BakeOffsetToDevices();
                EditorUtility.SetDirty(script.transformLoader);
                for (int i = 0; i < allDevices.Length; i++)
                {
                    if (i < script.targetDevices.Count && script.targetDevices[i])
                    {
                        EditorUtility.SetDirty(allDevices[i]);
                    }
                }
            });
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;
    }

    private void ExecuteWithTransformLoader(System.Action action)
    {
        if (script.transformLoader == null)
        {
            AppLogger.LogWarning(script, HAP_LogTriggers.TagCalibration, "[Calibration] AUTD TransformLoader is not assigned.");
            return;
        }

        action();
        if (Application.isPlaying) HAP_CalibrationStatePersistence.SaveState(script);
    }
}
#endif