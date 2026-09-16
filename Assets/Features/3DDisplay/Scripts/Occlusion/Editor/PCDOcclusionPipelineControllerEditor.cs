/*
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PCDOcclusionPipelineController))]
[CanEditMultipleObjects]
public class PCDOcclusionPipelineControllerEditor : Editor
{
    private SerializedProperty _cameraTargetMode;
    private SerializedProperty _cameraNameFilter;

    private SerializedProperty _kernelType;
    private SerializedProperty _evaluationMode;
    private SerializedProperty _minOccludedSectors;
    private SerializedProperty _maxConsecutiveEmptySectors;
    private SerializedProperty _minSearchLevel;

    private SerializedProperty _exponentAlpha;
    private SerializedProperty _densityThreshold_e;
    private SerializedProperty _neighborhoodParam_p_prime;

    private SerializedProperty _enableDensityBasedLOD;
    private SerializedProperty _enableGradientCorrection;
    private SerializedProperty _gradientThreshold_g_th;

    private SerializedProperty _occlusionThreshold;
    private SerializedProperty _occlusionFadeWidth;

    private SerializedProperty _enableVirtualContactOcclusion;
    private SerializedProperty _virtualContactRadius;
    private SerializedProperty _virtualContactSpacing;
    private SerializedProperty _virtualContactColor;

    private SerializedProperty _enablePixelTagMap;
    private SerializedProperty _enableOcclusionMap;

    private SerializedProperty _recordOcclusionDebugMap;
    private SerializedProperty _recordPixelTagMap;
    private SerializedProperty _recordIntegratedDepthMap;
    private SerializedProperty _recordNeighborhoodMap;
    private SerializedProperty _recordNeighborCountMap;

    private SerializedProperty _enableVirtualDepthIntegration;

    // 3D Spatial Mirroring Properties
    private SerializedProperty _enable3DMirrorX;
    private SerializedProperty _mirrorOriginX;
    private SerializedProperty _mirrorExternalPointCloud;
    private SerializedProperty _externalPointMirrorOriginX;
    private SerializedProperty _enableFinalOutputUvFlip;
    private SerializedProperty _overrideMeshColor;
    private SerializedProperty _customMeshColor;

    private SerializedProperty _enableTagBasedOptimization;
    private SerializedProperty _enableTypeAwareDensity;
    private SerializedProperty _enableSoftOcclusionFade;
    private SerializedProperty _holeFillingMethod;
    private SerializedProperty _gridSize;

    private SerializedProperty _enableBufferManagerLog;

    private SerializedProperty _morphKernelHalfSize;
    private SerializedProperty _morphErodeIterations;
    private SerializedProperty _morphDilateIterations;

    private void OnEnable()
    {
        _cameraTargetMode = serializedObject.FindProperty("cameraTargetMode");
        _cameraNameFilter = serializedObject.FindProperty("cameraNameFilter");

        _kernelType = serializedObject.FindProperty("kernelType");
        _evaluationMode = serializedObject.FindProperty("evaluationMode");
        _minOccludedSectors = serializedObject.FindProperty("minOccludedSectors");
        _maxConsecutiveEmptySectors = serializedObject.FindProperty("maxConsecutiveEmptySectors");
        _minSearchLevel = serializedObject.FindProperty("minSearchLevel");

        _exponentAlpha = serializedObject.FindProperty("exponentAlpha");
        _densityThreshold_e = serializedObject.FindProperty("densityThreshold_e");
        _neighborhoodParam_p_prime = serializedObject.FindProperty("neighborhoodParam_p_prime");

        _enableDensityBasedLOD = serializedObject.FindProperty("enableDensityBasedLOD");
        _enableGradientCorrection = serializedObject.FindProperty("enableGradientCorrection");
        _gradientThreshold_g_th = serializedObject.FindProperty("gradientThreshold_g_th");

        _occlusionThreshold = serializedObject.FindProperty("occlusionThreshold");
        _occlusionFadeWidth = serializedObject.FindProperty("occlusionFadeWidth");

        _enableVirtualContactOcclusion = serializedObject.FindProperty("enableVirtualContactOcclusion");
        _virtualContactRadius = serializedObject.FindProperty("virtualContactRadius");
        _virtualContactSpacing = serializedObject.FindProperty("virtualContactSpacing");
        _virtualContactColor = serializedObject.FindProperty("virtualContactColor");

        _enablePixelTagMap = serializedObject.FindProperty("enablePixelTagMap");
        _enableOcclusionMap = serializedObject.FindProperty("enableOcclusionMap");

        _recordOcclusionDebugMap = serializedObject.FindProperty("recordOcclusionDebugMap");
        _recordPixelTagMap = serializedObject.FindProperty("recordPixelTagMap");
        _recordIntegratedDepthMap = serializedObject.FindProperty("recordIntegratedDepthMap");
        _recordNeighborhoodMap = serializedObject.FindProperty("recordNeighborhoodMap");
        _recordNeighborCountMap = serializedObject.FindProperty("recordNeighborCountMap");

        _enableVirtualDepthIntegration = serializedObject.FindProperty("enableVirtualDepthIntegration");

        // 3D Spatial Mirroring
        _enable3DMirrorX = serializedObject.FindProperty("enable3DMirrorX");
        _mirrorOriginX = serializedObject.FindProperty("mirrorOriginX");
        _mirrorExternalPointCloud = serializedObject.FindProperty("mirrorExternalPointCloud");
        _externalPointMirrorOriginX = serializedObject.FindProperty("externalPointMirrorOriginX");
        _enableFinalOutputUvFlip = serializedObject.FindProperty("enableFinalOutputUvFlip");
        _overrideMeshColor = serializedObject.FindProperty("overrideMeshColor");
        _customMeshColor = serializedObject.FindProperty("customMeshColor");

        _enableTagBasedOptimization = serializedObject.FindProperty("enableTagBasedOptimization");
        _enableTypeAwareDensity = serializedObject.FindProperty("enableTypeAwareDensity");
        _enableSoftOcclusionFade = serializedObject.FindProperty("enableSoftOcclusionFade");
        _holeFillingMethod = serializedObject.FindProperty("holeFillingMethod");
        _gridSize = serializedObject.FindProperty("gridSize");

        _enableBufferManagerLog = serializedObject.FindProperty("enableBufferManagerLog");

        _morphKernelHalfSize = serializedObject.FindProperty("morphKernelHalfSize");
        _morphErodeIterations = serializedObject.FindProperty("morphErodeIterations");
        _morphDilateIterations = serializedObject.FindProperty("morphDilateIterations");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.Space();

        // 3D Spatial Mirroring Transformation (突出して表示)
        EditorGUILayout.LabelField("3D Spatial Mirroring Transformation", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(_enable3DMirrorX, new GUIContent("Enable 3D Mirror X (Registrar)"));
        if (_enable3DMirrorX.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_mirrorOriginX, new GUIContent("Registrar Mirror Origin X"));
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.PropertyField(_mirrorExternalPointCloud, new GUIContent("Mirror External Point Cloud"));
        if (_mirrorExternalPointCloud.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_externalPointMirrorOriginX, new GUIContent("External Point Mirror Origin X"));
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.Space(2);
        EditorGUILayout.PropertyField(_enableFinalOutputUvFlip, new GUIContent("Enable Final Output UV.u Flip"));
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        // Mesh Registrar Color Override
        EditorGUILayout.LabelField("Mesh Registrar Color Override (Point Cloud Debug)", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(_overrideMeshColor, new GUIContent("Override Mesh Color (Monochrome)"));
        if (_overrideMeshColor.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_customMeshColor, new GUIContent("Custom Mesh Color"));
            EditorGUI.indentLevel--;
        }
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        // Core Settings
        EditorGUILayout.LabelField("Occlusion Core Settings", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(_kernelType);
        EditorGUILayout.PropertyField(_evaluationMode);
        if (_evaluationMode.enumValueIndex != (int)PCDRendererFeature.PCD_OcclusionEvaluationMode.Average)
        {
            EditorGUILayout.PropertyField(_minOccludedSectors);
        }
        if (_evaluationMode.enumValueIndex == (int)PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorConsecutiveZeros)
        {
            EditorGUILayout.PropertyField(_maxConsecutiveEmptySectors);
        }
        EditorGUILayout.PropertyField(_minSearchLevel);
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        // Algorithm Parameters
        EditorGUILayout.LabelField("Algorithm Parameters", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        if (_kernelType.enumValueIndex == (int)PCDRendererFeature.PCD_OcclusionKernel.Exponential)
        {
            EditorGUILayout.PropertyField(_exponentAlpha);
        }
        EditorGUILayout.PropertyField(_densityThreshold_e);
        EditorGUILayout.PropertyField(_neighborhoodParam_p_prime);
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        // Gradient Correction
        EditorGUILayout.LabelField("Gradient Correction & Dynamic LOD", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(_enableDensityBasedLOD);
        EditorGUILayout.PropertyField(_enableGradientCorrection);
        if (_enableGradientCorrection.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_gradientThreshold_g_th);
            EditorGUI.indentLevel--;
        }
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        // Occlusion Filtering
        EditorGUILayout.LabelField("Occlusion Filtering", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(_occlusionThreshold);
        EditorGUILayout.PropertyField(_occlusionFadeWidth);
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        // Virtual Contact
        EditorGUILayout.LabelField("Virtual Contact Occlusion", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(_enableVirtualContactOcclusion);
        if (_enableVirtualContactOcclusion.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_virtualContactRadius);
            EditorGUILayout.PropertyField(_virtualContactSpacing);
            EditorGUILayout.PropertyField(_virtualContactColor);
            EditorGUI.indentLevel--;
        }
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        // Novel Methods
        EditorGUILayout.LabelField("Novel Methods Toggles (Ablation Study)", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(_enableVirtualDepthIntegration);
        EditorGUILayout.PropertyField(_enableTagBasedOptimization);
        EditorGUILayout.PropertyField(_enableTypeAwareDensity);
        EditorGUILayout.PropertyField(_enableSoftOcclusionFade);
        EditorGUILayout.PropertyField(_holeFillingMethod);
        EditorGUILayout.PropertyField(_gridSize);
        EditorGUI.indentLevel--;
        EditorGUILayout.Space();

        // Morphology
        if (_holeFillingMethod.enumValueIndex != (int)PCDRendererFeature.PCD_HoleFillingMethod.None)
        {
            EditorGUILayout.LabelField("Morphology Settings", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_morphKernelHalfSize);
            EditorGUILayout.PropertyField(_morphErodeIterations);
            EditorGUILayout.PropertyField(_morphDilateIterations);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
        }

        // Display Debug
        EditorGUILayout.LabelField("Display & Record Debug Maps", EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(_enablePixelTagMap);
        EditorGUILayout.PropertyField(_enableOcclusionMap);
        EditorGUILayout.PropertyField(_recordOcclusionDebugMap);
        EditorGUILayout.PropertyField(_recordPixelTagMap);
        EditorGUILayout.PropertyField(_recordIntegratedDepthMap);
        EditorGUILayout.PropertyField(_recordNeighborhoodMap);
        EditorGUILayout.PropertyField(_recordNeighborCountMap);
        EditorGUILayout.PropertyField(_enableBufferManagerLog);
        EditorGUI.indentLevel--;

        serializedObject.ApplyModifiedProperties();
    }
}
*/