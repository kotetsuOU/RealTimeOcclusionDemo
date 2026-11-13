using UnityEditor;
using UnityEngine;
using static PCDRendererFeature;

/// <summary>
/// <see cref="PCDOcclusionPipelineController"/> のカスタム Inspector エディタ。
/// ヘッダー UI (<see cref="PCDOcclusionModeHeaderDrawer"/>) および
/// デバッグ設定セクション (<see cref="PCDOcclusionDebugSectionDrawer"/>) を統合し、
/// コアパラメータ・アブレーション・モルフォロジー・仮想接触の各 Foldout セクションを描画します。
/// </summary>
[CustomEditor(typeof(PCDOcclusionPipelineController))]
[CanEditMultipleObjects]
public class PCDOcclusionPipelineControllerEditor : Editor
{
    private readonly PCDOcclusionModeHeaderDrawer _modeHeaderDrawer = new PCDOcclusionModeHeaderDrawer();
    private readonly PCDOcclusionDebugSectionDrawer _debugSectionDrawer = new PCDOcclusionDebugSectionDrawer();

    // カメラターゲット
    private SerializedProperty _cameraTargetMode;
    private SerializedProperty _cameraNameFilter;

    // コア探索 & アルゴリズム
    private SerializedProperty _kernelType;
    private SerializedProperty _minSearchLevel;
    private SerializedProperty _exponentAlpha;
    private SerializedProperty _densityThreshold_e;
    private SerializedProperty _neighborhoodParam_p_prime;

    // 勾配補正 & 動的LOD
    private SerializedProperty _enableDensityBasedLOD;
    private SerializedProperty _enableGradientCorrection;
    private SerializedProperty _gradientThreshold_g_th;

    // フィルタリング & フェード
    private SerializedProperty _occlusionThreshold_T_ave;
    private SerializedProperty _occlusionThreshold_T_loc;
    private SerializedProperty _useSectorSpecificThreshold;
    private SerializedProperty _sectorThreshold_T_loc;
    private SerializedProperty _occlusionFadeWidth;

    // 仮想接触
    private SerializedProperty _enableVirtualContactOcclusion;
    private SerializedProperty _virtualContactRadius;
    private SerializedProperty _virtualContactSpacing;
    private SerializedProperty _virtualContactColor;

    // 提案手法トグル
    private SerializedProperty _enableVirtualDepthIntegration;
    private SerializedProperty _enableTagBasedOptimization;
    private SerializedProperty _enableTypeAwareDensity;
    private SerializedProperty _enableSoftOcclusionFade;
    private SerializedProperty _holeFillingMethod;
    private SerializedProperty _gridSize;

    // モルフォロジー
    private SerializedProperty _morphKernelHalfSize;
    private SerializedProperty _morphErodeIterations;
    private SerializedProperty _morphDilateIterations;

    // Foldout 状態管理
    private static bool _showCameraSettings = false;
    private static bool _showCoreAlgorithm = true;
    private static bool _showAblationStudies = false;
    private static bool _showMorphology = false;
    private static bool _showVirtualContact = false;

    private static readonly string[] SectorNames = new string[]
    {
        "Sec 0: 北 (N)", "Sec 1: 北東 (NE)", "Sec 2: 東 (E)", "Sec 3: 南東 (SE)",
        "Sec 4: 南 (S)", "Sec 5: 南西 (SW)", "Sec 6: 西 (W)", "Sec 7: 北西 (NW)"
    };

    private void OnEnable()
    {
        _modeHeaderDrawer.Bind(serializedObject);
        _debugSectionDrawer.Bind(serializedObject);

        _cameraTargetMode = serializedObject.FindProperty("cameraTargetMode");
        _cameraNameFilter = serializedObject.FindProperty("cameraNameFilter");

        _kernelType = serializedObject.FindProperty("kernelType");
        _minSearchLevel = serializedObject.FindProperty("minSearchLevel");
        _exponentAlpha = serializedObject.FindProperty("exponentAlpha");
        _densityThreshold_e = serializedObject.FindProperty("densityThreshold_e");
        _neighborhoodParam_p_prime = serializedObject.FindProperty("neighborhoodParam_p_prime");

        _enableDensityBasedLOD = serializedObject.FindProperty("enableDensityBasedLOD");
        _enableGradientCorrection = serializedObject.FindProperty("enableGradientCorrection");
        _gradientThreshold_g_th = serializedObject.FindProperty("gradientThreshold_g_th");

        _occlusionThreshold_T_ave = serializedObject.FindProperty("occlusionThreshold_T_ave");
        _occlusionThreshold_T_loc = serializedObject.FindProperty("occlusionThreshold_T_loc");
        _useSectorSpecificThreshold = serializedObject.FindProperty("useSectorSpecificThreshold");
        _sectorThreshold_T_loc = serializedObject.FindProperty("sectorThreshold_T_loc");
        _occlusionFadeWidth = serializedObject.FindProperty("occlusionFadeWidth");

        _enableVirtualContactOcclusion = serializedObject.FindProperty("enableVirtualContactOcclusion");
        _virtualContactRadius = serializedObject.FindProperty("virtualContactRadius");
        _virtualContactSpacing = serializedObject.FindProperty("virtualContactSpacing");
        _virtualContactColor = serializedObject.FindProperty("virtualContactColor");

        _enableVirtualDepthIntegration = serializedObject.FindProperty("enableVirtualDepthIntegration");
        _enableTagBasedOptimization = serializedObject.FindProperty("enableTagBasedOptimization");
        _enableTypeAwareDensity = serializedObject.FindProperty("enableTypeAwareDensity");
        _enableSoftOcclusionFade = serializedObject.FindProperty("enableSoftOcclusionFade");
        _holeFillingMethod = serializedObject.FindProperty("holeFillingMethod");
        _gridSize = serializedObject.FindProperty("gridSize");

        _morphKernelHalfSize = serializedObject.FindProperty("morphKernelHalfSize");
        _morphErodeIterations = serializedObject.FindProperty("morphErodeIterations");
        _morphDilateIterations = serializedObject.FindProperty("morphDilateIterations");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var controller = (PCDOcclusionPipelineController)target;

        _modeHeaderDrawer.Draw(serializedObject, controller);
        EditorGUILayout.Space(6);

        DrawCoreAlgorithmSection();
        EditorGUILayout.Space(4);

        DrawCameraTargetSection();
        EditorGUILayout.Space(4);

        DrawAblationSection();
        EditorGUILayout.Space(4);

        DrawMorphologySection();
        DrawVirtualContactSection();

        EditorGUILayout.Space(6);
        _debugSectionDrawer.Draw(serializedObject, controller);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawCoreAlgorithmSection()
    {
        _showCoreAlgorithm = EditorGUILayout.BeginFoldoutHeaderGroup(_showCoreAlgorithm, "Core Pipeline & Search Parameters");
        if (_showCoreAlgorithm)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_kernelType);
            if (_kernelType.enumValueIndex == (int)PCD_OcclusionKernel.Exponential)
            {
                EditorGUILayout.PropertyField(_exponentAlpha);
            }

            EditorGUILayout.PropertyField(_minSearchLevel);
            EditorGUILayout.PropertyField(_enableDensityBasedLOD);
            EditorGUILayout.PropertyField(_densityThreshold_e);
            EditorGUILayout.PropertyField(_neighborhoodParam_p_prime);

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(_enableGradientCorrection);
            if (_enableGradientCorrection.boolValue)
            {
                EditorGUILayout.PropertyField(_gradientThreshold_g_th);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("オクルージョン判定閾値 (Thresholds)", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_occlusionThreshold_T_ave, new GUIContent("閾値 T_ave (Average手法)"));
            EditorGUILayout.PropertyField(_useSectorSpecificThreshold, new GUIContent("8セクター個別 T_loc を使用"));
            if (_useSectorSpecificThreshold.boolValue)
            {
                EditorGUI.indentLevel++;
                if (_sectorThreshold_T_loc.arraySize < 8) _sectorThreshold_T_loc.arraySize = 8;
                for (int s = 0; s < 8; s++)
                {
                    EditorGUILayout.Slider(_sectorThreshold_T_loc.GetArrayElementAtIndex(s), 0f, 1f, new GUIContent(SectorNames[s]));
                }
                EditorGUI.indentLevel--;
            }
            else
            {
                EditorGUILayout.PropertyField(_occlusionThreshold_T_loc, new GUIContent("閾値 T_loc (セクター占有・共通)"));
            }
            EditorGUILayout.PropertyField(_occlusionFadeWidth);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    private void DrawCameraTargetSection()
    {
        _showCameraSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showCameraSettings, "Camera Target & Culling Settings");
        if (_showCameraSettings)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_cameraTargetMode);
            if (_cameraTargetMode.enumValueIndex == (int)PCD_CameraTargetMode.CustomFilter)
            {
                EditorGUILayout.PropertyField(_cameraNameFilter);
            }
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    private void DrawAblationSection()
    {
        _showAblationStudies = EditorGUILayout.BeginFoldoutHeaderGroup(_showAblationStudies, "Ablation Study & Optimization Methods");
        if (_showAblationStudies)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_enableVirtualDepthIntegration);
            EditorGUILayout.PropertyField(_enableTagBasedOptimization);
            EditorGUILayout.PropertyField(_enableTypeAwareDensity);
            EditorGUILayout.PropertyField(_enableSoftOcclusionFade);
            EditorGUILayout.PropertyField(_holeFillingMethod);
            EditorGUILayout.PropertyField(_gridSize);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    private void DrawMorphologySection()
    {
        if (_holeFillingMethod.enumValueIndex == (int)PCD_HoleFillingMethod.None) return;

        _showMorphology = EditorGUILayout.BeginFoldoutHeaderGroup(_showMorphology, "Morphology Post-Process Settings");
        if (_showMorphology)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_morphKernelHalfSize);
            EditorGUILayout.PropertyField(_morphErodeIterations);
            EditorGUILayout.PropertyField(_morphDilateIterations);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
        EditorGUILayout.Space(4);
    }

    private void DrawVirtualContactSection()
    {
        _showVirtualContact = EditorGUILayout.BeginFoldoutHeaderGroup(_showVirtualContact, "Virtual Contact Occlusion (HCD Haptics)");
        if (_showVirtualContact)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_enableVirtualContactOcclusion);
            if (_enableVirtualContactOcclusion.boolValue)
            {
                EditorGUILayout.PropertyField(_virtualContactRadius);
                EditorGUILayout.PropertyField(_virtualContactSpacing);
                EditorGUILayout.PropertyField(_virtualContactColor);
            }
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }
}