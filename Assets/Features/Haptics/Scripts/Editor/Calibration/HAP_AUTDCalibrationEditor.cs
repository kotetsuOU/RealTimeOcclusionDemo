using UnityEngine;
using UnityEditor;
using System.Linq;

[CustomEditor(typeof(HAP_AUTDCalibration))]
public class HAP_AUTDCalibrationEditor : Editor
{
    private HAP_AUTDCalibration script = null!;
    private const string PREFS_KEY = "HAP_AUTDCalibration_State";

    private void OnEnable()
    {
        script = (HAP_AUTDCalibration)target;

        // Playモード終了時のみ復元する（Playモード突入時の誤復元を防ぐ）
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            RestoreState();
        }
    }

    private void OnDisable()
    {
        // Playモード中なら終了時に備えて状態を保存する
        if (Application.isPlaying)
        {
            SaveState();
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUI.BeginChangeCheck();

        // ------------------
        // Controller Ref
        // ------------------
        var autdProp = serializedObject.FindProperty("autdController") ?? serializedObject.FindProperty("hapticsController");
        if (autdProp != null) EditorGUILayout.PropertyField(autdProp);
        
        var transformLoaderProp = serializedObject.FindProperty("transformLoader");
        if (transformLoaderProp != null) EditorGUILayout.PropertyField(transformLoaderProp);

        EditorGUILayout.Space();

        // ------------------
        // Calibration Toggle
        // ------------------
        GUI.color = script.enableCalibration ? new Color(1f, 0.6f, 0.6f) : Color.white;
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.PropertyField(serializedObject.FindProperty("enableCalibration"));
        EditorGUILayout.EndVertical();
        GUI.color = Color.white;

        EditorGUILayout.Space();

        // ------------------
        // Target Devices List
        // ------------------
        EditorGUILayout.LabelField("Target Devices (Check to Enable)", EditorStyles.boldLabel);
        
        // シーン内のAUTD3Deviceの数を取得してリストを自動調整
        var devices = FindObjectsByType<AUTD3Device>(FindObjectsSortMode.None);
        int devCount = devices.Length;

        if (devCount > 0)
        {
            if (script.targetDevices.Count != devCount)
            {
                // リストのサイズをデバイス数に合わせる
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
        else
        {
            EditorGUILayout.HelpBox("No AUTD3Device found in the scene.", MessageType.Warning);
        }

        EditorGUILayout.Space();

        // ------------------
        // Focus Settings
        // ------------------
        EditorGUILayout.PropertyField(serializedObject.FindProperty("useMultiFocus"));
        
        if (script.useMultiFocus)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("multiFocusPositions"), true);
        }
        else
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("singleFocusTarget"));
            
            // Transformが指定されていない場合のみ、手入力のVector3フィールドを表示する
            if (script.singleFocusTarget == null)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("singleFocusPosition"));
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("truePositionTarget"));

        EditorGUILayout.PropertyField(serializedObject.FindProperty("focusAmplitude"));

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            // Playモード中に変更されたら即座に保存
            if (Application.isPlaying)
            {
                SaveState();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        // ------------------
        // Offset Tools
        // ------------------
        EditorGUILayout.LabelField("Calibration Tools", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Apply Transform Offset (Legacy)", GUILayout.Height(24)))
        {
            if (script.transformLoader != null)
            {
                Undo.RecordObject(script.transformLoader, "Apply AUTD Offset");
                Undo.RecordObject(script.transform, "Reset Calibration Transform");
                
                script.ApplyOffset();
                
                EditorUtility.SetDirty(script.transformLoader);
                if (Application.isPlaying) SaveState();
                Debug.Log("[Calibration] Applied legacy offset to AUTD TransformLoader.");
            }
            else
            {
                Debug.LogWarning("[Calibration] AUTD TransformLoader is not assigned.");
            }
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Calculate & Add Offset\n(FocusTarget -> TruePosition)", GUILayout.Height(36)))
        {
            if (script.transformLoader != null)
            {
                Undo.RecordObject(script.transformLoader, "Calculate & Add AUTD Offset");
                
                script.ApplyOffsetByDifference();
                
                EditorUtility.SetDirty(script.transformLoader);
                if (Application.isPlaying) SaveState();
            }
            else
            {
                Debug.LogWarning("[Calibration] AUTD TransformLoader is not assigned.");
            }
        }

        EditorGUILayout.Space();

        // ------------------
        // Bake Tools
        // ------------------
        GUI.enabled = !Application.isPlaying;
        GUI.backgroundColor = new Color(1f, 0.8f, 0.5f);
        if (GUILayout.Button("Bake Offset to Devices\n(Reset Offset & Move Devices)", GUILayout.Height(36)))
        {
            if (script.transformLoader != null)
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
                if (Application.isPlaying) SaveState();
            }
            else
            {
                Debug.LogWarning("[Calibration] AUTD TransformLoader is not assigned.");
            }
        }
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;

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
                if (Application.isPlaying) SaveState();
            }
        }
    }

    [System.Serializable]
    private class CalibrationState
    {
        public bool enableCalibration;
        public bool[] targetDevices;
        public bool useMultiFocus;
        public Vector3 singleFocusPosition;
        public Vector3[] multiFocusPositions;
        public float focusAmplitude;
        public Vector3 autdOffset;
    }

    private void SaveState()
    {
        if (script == null) return;

        var state = new CalibrationState
        {
            enableCalibration = script.enableCalibration,
            targetDevices = script.targetDevices.ToArray(),
            useMultiFocus = script.useMultiFocus,
            singleFocusPosition = script.singleFocusPosition,
            multiFocusPositions = script.multiFocusPositions.ToArray(),
            focusAmplitude = script.focusAmplitude,
            autdOffset = script.transformLoader != null ? script.transformLoader.offset : Vector3.zero
        };

        string json = JsonUtility.ToJson(state);
        EditorPrefs.SetString(PREFS_KEY, json);
    }

    private void RestoreState()
    {
        if (script == null || !EditorPrefs.HasKey(PREFS_KEY)) return;

        string json = EditorPrefs.GetString(PREFS_KEY);
        try
        {
            var state = JsonUtility.FromJson<CalibrationState>(json);
            if (state != null)
            {
                Undo.RecordObject(script, "Restore Calibration State");
                
                script.enableCalibration = state.enableCalibration;
                if (state.targetDevices != null) script.targetDevices = state.targetDevices.ToList();
                script.useMultiFocus = state.useMultiFocus;
                script.singleFocusPosition = state.singleFocusPosition;
                if (state.multiFocusPositions != null) script.multiFocusPositions = state.multiFocusPositions.ToList();
                script.focusAmplitude = state.focusAmplitude;

                if (script.transformLoader != null)
                {
                    Undo.RecordObject(script.transformLoader, "Restore AUTD Offset");
                    script.transformLoader.offset = state.autdOffset;
                    EditorUtility.SetDirty(script.transformLoader);
                }

                EditorUtility.SetDirty(script);
                serializedObject.Update();
            }
            
            // 復元が終わったらキーを削除し、再選択時などに古い値が復元（自然生成）されるバグを防ぐ
            EditorPrefs.DeleteKey(PREFS_KEY);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to restore calibration state: {e.Message}");
        }
    }
}
