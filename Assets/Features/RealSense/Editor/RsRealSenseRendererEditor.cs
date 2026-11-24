using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RsRealSenseRenderer))]
public class RsRealSenseRendererEditor : Editor
{
    private bool _isVerticesSaved = false;
    private bool _showDependencies = false;

    private SerializedProperty _deviceConfigProp;
    private SerializedProperty _modeProp;
    private SerializedProperty _processModeProp;
    private SerializedProperty _recordDurationProp;
    private SerializedProperty _maxRetriesProp;
    private SerializedProperty _retryIntervalProp;

    private SerializedProperty _processingProfileProp;
    private SerializedProperty _processIntervalProp;

    private SerializedProperty _pointCloudColorProp;
    private SerializedProperty _maxPlaneDistanceProp;
    private SerializedProperty _isRangeFilterProp;

    private SerializedProperty _deviceControllerProp;
    private SerializedProperty _filterShaderProp;
    private SerializedProperty _transformerShaderProp;
    private SerializedProperty _exportFileNameProp;

    private void OnEnable()
    {
        _deviceConfigProp = serializedObject.FindProperty("DeviceConfiguration");
        _modeProp = _deviceConfigProp.FindPropertyRelative("mode");
        _processModeProp = serializedObject.FindProperty("processMode");
        _recordDurationProp = serializedObject.FindProperty("recordDurationInFrames");
        _maxRetriesProp = serializedObject.FindProperty("maxRetries");
        _retryIntervalProp = serializedObject.FindProperty("retryIntervalSeconds");

        _processingProfileProp = serializedObject.FindProperty("processingProfile");
        _processIntervalProp = serializedObject.FindProperty("_processIntervalFrames");

        _pointCloudColorProp = serializedObject.FindProperty("pointCloudColor");
        _maxPlaneDistanceProp = serializedObject.FindProperty("maxPlaneDistance");
        _isRangeFilterProp = serializedObject.FindProperty("isGlobalRangeFilterEnabled");

        _deviceControllerProp = serializedObject.FindProperty("rsDeviceController");
        _filterShaderProp = serializedObject.FindProperty("pointCloudFilterShader");
        _transformerShaderProp = serializedObject.FindProperty("pointCloudTransformerShader");
        _exportFileNameProp = serializedObject.FindProperty("exportFileName");
    }

    public override void OnInspectorGUI()
    {
        if (_deviceConfigProp == null)
        {
            OnEnable();
        }

        serializedObject.Update();

        var renderer = (RsRealSenseRenderer)target;
        bool isStreaming = renderer.isActiveAndEnabled && renderer.ActiveProfile != null;

        DrawStatusBanner(renderer, isStreaming);
        EditorGUILayout.Space(6f);

        DrawDeviceSection(isStreaming);
        EditorGUILayout.Space(6f);

        DrawPipelineSection();
        EditorGUILayout.Space(6f);

        DrawPointCloudSection(renderer);
        EditorGUILayout.Space(6f);

        DrawDependenciesFoldout();
        EditorGUILayout.Space(6f);

        _isVerticesSaved = RsPointCloudExportTool.DrawExportUI(renderer, _exportFileNameProp, _isVerticesSaved);
        RsPointCloudSceneGizmo.DrawPCAModeInfo();

        serializedObject.ApplyModifiedProperties();
    }

    private void OnSceneGUI()
    {
        var renderer = (RsRealSenseRenderer)target;
        RsPointCloudSceneGizmo.DrawPCAEstimationGizmo(renderer);
    }

    private static void DrawStatusBanner(RsRealSenseRenderer renderer, bool isStreaming)
    {
        string status = isStreaming ? "Streaming Active" : "Idle / Not Streaming";
        string detail = isStreaming ? $"Filtered Points: {renderer.GetLastFilteredCount()}" : "Play モードでストリーミングを開始します";
        MessageType type = isStreaming ? MessageType.Info : MessageType.None;
        EditorGUILayout.HelpBox($"{status}  |  {detail}", type);
    }

    private void DrawDeviceSection(bool isStreaming)
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("1. Device Settings", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(isStreaming);
            _modeProp.enumValueIndex = GUILayout.Toolbar(_modeProp.enumValueIndex, _modeProp.enumDisplayNames);
            EditorGUILayout.Space(4f);

            var mode = (RsConfiguration.Mode)_modeProp.enumValueIndex;
            switch (mode)
            {
                case RsConfiguration.Mode.Live:
                    EditorGUILayout.PropertyField(_deviceConfigProp.FindPropertyRelative("RequestedSerialNumber"), new GUIContent("Serial Number"));
                    EditorGUILayout.PropertyField(_deviceConfigProp.FindPropertyRelative("Profiles"), true);
                    break;

                case RsConfiguration.Mode.Playback:
                    EditorGUILayout.PropertyField(_deviceConfigProp.FindPropertyRelative("RequestedSerialNumber"), new GUIContent("Serial Number"));
                    DrawPathWarningIfAbsolute(_deviceConfigProp.FindPropertyRelative("PlaybackFile"), "playback");
                    EditorGUILayout.PropertyField(_deviceConfigProp.FindPropertyRelative("PlaybackFile"));
                    break;

                case RsConfiguration.Mode.Record:
                    EditorGUILayout.PropertyField(_deviceConfigProp.FindPropertyRelative("Profiles"), true);
                    DrawPathWarningIfAbsolute(_deviceConfigProp.FindPropertyRelative("RecordPath"), "record");
                    EditorGUILayout.PropertyField(_deviceConfigProp.FindPropertyRelative("RecordPath"));
                    EditorGUILayout.PropertyField(_recordDurationProp, new GUIContent("Record Frames (0 = ∞)"));
                    break;
            }

            EditorGUILayout.PropertyField(_processModeProp, new GUIContent("Process Mode"));
            EditorGUILayout.PropertyField(_maxRetriesProp, new GUIContent("Max Retries"));
            EditorGUILayout.PropertyField(_retryIntervalProp, new GUIContent("Retry Interval (s)"));
            EditorGUI.EndDisabledGroup();
        }
    }

    private void DrawPipelineSection()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("2. Processing Pipeline", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(_processingProfileProp, new GUIContent("Processing Profile"));
                if (_processingProfileProp.objectReferenceValue != null && GUILayout.Button("Select", GUILayout.Width(54f)))
                {
                    Selection.activeObject = _processingProfileProp.objectReferenceValue;
                    EditorGUIUtility.PingObject(_processingProfileProp.objectReferenceValue);
                }
            }

            EditorGUILayout.PropertyField(_processIntervalProp, new GUIContent("Process Interval (Frames)"));
        }
    }

    private void DrawPointCloudSection(RsRealSenseRenderer renderer)
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("3. Point Cloud & Rendering", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_pointCloudColorProp, new GUIContent("Point Color"));
            EditorGUILayout.PropertyField(_maxPlaneDistanceProp, new GUIContent("Max Plane Distance"));
            EditorGUILayout.PropertyField(_isRangeFilterProp, new GUIContent("Global Range Filter"));
            RsPointCloudRendererEditor.DrawRangeFilterUI(renderer);
        }
    }

    private void DrawDependenciesFoldout()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            _showDependencies = EditorGUILayout.Foldout(_showDependencies, "4. Dependencies (Controller & Shaders)", true, EditorStyles.foldoutHeader);
            if (_showDependencies)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_deviceControllerProp, new GUIContent("Device Controller"));
                EditorGUILayout.PropertyField(_filterShaderProp, new GUIContent("Filter ComputeShader"));
                EditorGUILayout.PropertyField(_transformerShaderProp, new GUIContent("Transformer ComputeShader"));
                EditorGUI.indentLevel--;
            }
        }
    }

    private static void DrawPathWarningIfAbsolute(SerializedProperty pathProp, string label)
    {
        if (pathProp != null && !string.IsNullOrEmpty(pathProp.stringValue) && Path.IsPathRooted(pathProp.stringValue))
        {
            EditorGUILayout.HelpBox(
                $"The {label} file path is an absolute path. Placing it inside Assets/StreamingAssets is recommended.",
                MessageType.Warning);
        }
    }
}
