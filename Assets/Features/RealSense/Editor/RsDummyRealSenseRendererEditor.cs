using UnityEditor;
using UnityEngine;
using RealSense.DummyPointCloud;

namespace RealSense.Editor
{
    [CustomEditor(typeof(RsDummyRealSenseRenderer))]
    public class RsDummyRealSenseRendererEditor : UnityEditor.Editor
    {
        private SerializedProperty _targetObjectsProp;
        private SerializedProperty _includeChildrenProp;

        private SerializedProperty _densityUnitProp;
        private SerializedProperty _densityValueProp;
        private SerializedProperty _maxPointLimitProp;
        private SerializedProperty _colorModeProp;
        private SerializedProperty _solidColorProp;
        private SerializedProperty _applyColorProp;

        private SerializedProperty _noiseSettingsProp;

        private SerializedProperty _useCameraPerspectiveProp;
        private SerializedProperty _simulatedCameraTransformProp;
        private SerializedProperty _depthWidthProp;
        private SerializedProperty _depthHeightProp;
        private SerializedProperty _updateFPSProp;

        private SerializedProperty _pointCloudColorProp;
        private SerializedProperty _isRangeFilterProp;
        private SerializedProperty _showGizmosProp;

        private void OnEnable()
        {
            _targetObjectsProp = serializedObject.FindProperty("targetObjects");
            _includeChildrenProp = serializedObject.FindProperty("includeChildren");

            _densityUnitProp = serializedObject.FindProperty("densityUnit");
            _densityValueProp = serializedObject.FindProperty("densityValue");
            _maxPointLimitProp = serializedObject.FindProperty("maxPointLimit");
            _colorModeProp = serializedObject.FindProperty("colorMode");
            _solidColorProp = serializedObject.FindProperty("solidColor");
            _applyColorProp = serializedObject.FindProperty("applyColorToMaterialAndRenderer");

            _noiseSettingsProp = serializedObject.FindProperty("noiseSettings");

            _useCameraPerspectiveProp = serializedObject.FindProperty("useCameraPerspective");
            _simulatedCameraTransformProp = serializedObject.FindProperty("simulatedCameraTransform");
            _depthWidthProp = serializedObject.FindProperty("depthWidth");
            _depthHeightProp = serializedObject.FindProperty("depthHeight");
            _updateFPSProp = serializedObject.FindProperty("updateFPS");

            _pointCloudColorProp = serializedObject.FindProperty("pointCloudColor");
            _isRangeFilterProp = serializedObject.FindProperty("isGlobalRangeFilterEnabled");
            _showGizmosProp = serializedObject.FindProperty("showGizmos");
        }

        public override void OnInspectorGUI()
        {
            if (_targetObjectsProp == null)
            {
                OnEnable();
            }

            serializedObject.Update();

            var renderer = (RsDummyRealSenseRenderer)target;

            DrawStatusBanner(renderer);
            EditorGUILayout.Space(6f);

            DrawTargetsSection();
            EditorGUILayout.Space(6f);

            DrawDensityAndColorSection(renderer);
            EditorGUILayout.Space(6f);

            DrawNoiseSection();
            EditorGUILayout.Space(6f);

            DrawCameraSection();
            EditorGUILayout.Space(6f);

            DrawRenderingAndActionsSection(renderer);

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawStatusBanner(RsDummyRealSenseRenderer renderer)
        {
            int count = renderer.GetLastFilteredCount();
            var buffer = renderer.GetFilteredVerticesBuffer();
            string bufferState = buffer != null ? $"Allocated ({buffer.count})" : "Unallocated";
            EditorGUILayout.HelpBox($"Dummy Points: {count}  |  GPU Buffer: {bufferState}", MessageType.Info);
        }

        private void DrawTargetsSection()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("1. Target 3D Objects", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_targetObjectsProp, new GUIContent("Target Objects"), true);
                EditorGUILayout.PropertyField(_includeChildrenProp, new GUIContent("Include Children"));
            }
        }

        private void DrawDensityAndColorSection(RsDummyRealSenseRenderer renderer)
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("2. Point Density & Color", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_densityUnitProp, new GUIContent("Density Unit"));
                EditorGUILayout.PropertyField(_densityValueProp, new GUIContent("Density Value"));
                EditorGUILayout.PropertyField(_maxPointLimitProp, new GUIContent("Max Point Limit"));
                EditorGUILayout.PropertyField(_colorModeProp, new GUIContent("Color Mode"));

                if (_colorModeProp.enumValueIndex == (int)PointColorMode.SolidColor)
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(_solidColorProp, new GUIContent("Solid Color"));
                    EditorGUILayout.PropertyField(_applyColorProp, new GUIContent("Apply to Material & Renderer"));
                    if (EditorGUI.EndChangeCheck())
                    {
                        serializedObject.ApplyModifiedProperties();
                        renderer.UpdateMaterialAndRendererColors();
                    }
                }
            }
        }

        private void DrawNoiseSection()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("3. Noise & Outliers", EditorStyles.boldLabel);
                if (_noiseSettingsProp != null)
                {
                    EditorGUILayout.PropertyField(_noiseSettingsProp, new GUIContent("Noise Settings"), true);
                }
            }
        }

        private void DrawCameraSection()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("4. Camera Perspective & Update", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_useCameraPerspectiveProp, new GUIContent("Use Camera Perspective"));

                if (_useCameraPerspectiveProp.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_simulatedCameraTransformProp, new GUIContent("Camera Transform"));
                    EditorGUILayout.PropertyField(_depthWidthProp, new GUIContent("Depth Width"));
                    EditorGUILayout.PropertyField(_depthHeightProp, new GUIContent("Depth Height"));
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.PropertyField(_updateFPSProp, new GUIContent("Update FPS"));
            }
        }

        private void DrawRenderingAndActionsSection(RsDummyRealSenseRenderer renderer)
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("5. Rendering & Debug", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_pointCloudColorProp, new GUIContent("Point Color"));
                EditorGUILayout.PropertyField(_isRangeFilterProp, new GUIContent("Global Range Filter"));
                RsPointCloudRendererEditor.DrawRangeFilterUI(renderer);
                EditorGUILayout.PropertyField(_showGizmosProp, new GUIContent("Show Scene Gizmos"));

                EditorGUILayout.Space(4f);
                if (GUILayout.Button("Force Re-Sample Now", GUILayout.Height(26f)))
                {
                    renderer.ForceUpdateSampling();
                    SceneView.RepaintAll();
                }
            }
        }
    }
}
