using UnityEditor;
using UnityEngine;
using static PCDRendererFeature;

/// <summary>
/// PCDOcclusionPipelineController のカスタム Inspector エディタ。
/// オクルージョン判定モード (Mode 8, 20, 36, 256) のワンクリック切り替え、
/// コアパラメータの可視化、およびデバッグ設定の折りたたみ（Foldout）管理を提供します。
/// </summary>
[CustomEditor(typeof(PCDOcclusionPipelineController))]
[CanEditMultipleObjects]
public class PCDOcclusionPipelineControllerEditor : Editor
{
    // カメラターゲット
    private SerializedProperty _cameraTargetMode;
    private SerializedProperty _cameraNameFilter;

    // オクルージョンコア設定
    private SerializedProperty _kernelType;
    private SerializedProperty _evaluationMode;
    private SerializedProperty _minOccludedSectors;
    private SerializedProperty _maxConsecutiveEmptySectors;
    private SerializedProperty _customPatternLutHex;
    private SerializedProperty _minSearchLevel;

    // アルゴリズムパラメータ
    private SerializedProperty _exponentAlpha;
    private SerializedProperty _densityThreshold_e;
    private SerializedProperty _neighborhoodParam_p_prime;

    // 勾配補正 & 動的LOD
    private SerializedProperty _enableDensityBasedLOD;
    private SerializedProperty _enableGradientCorrection;
    private SerializedProperty _gradientThreshold_g_th;

    // フィルタリング & フェード
    private SerializedProperty _occlusionThreshold;
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

    // デバッグ設定 (PCDOcclusionDebugSettings)
    private SerializedProperty _debugSettings;
    private SerializedProperty _enablePixelTagMap;
    private SerializedProperty _enableOcclusionMap;
    private SerializedProperty _debugPatternId;
    private SerializedProperty _debugSectorId;
    private SerializedProperty _recordOcclusionDebugMap;
    private SerializedProperty _recordPixelTagMap;
    private SerializedProperty _recordIntegratedDepthMap;
    private SerializedProperty _recordNeighborhoodMap;
    private SerializedProperty _recordNeighborCountMap;
    private SerializedProperty _enableBufferManagerLog;

    // Foldout 状態管理
    private static bool _showCameraSettings = false;
    private static bool _showCoreAlgorithm = true;
    private static bool _showAblationStudies = false;
    private static bool _showMorphology = false;
    private static bool _showVirtualContact = false;
    private static bool _showDebugSettings = false; // デバッグ設定はデフォルトで閉じてスッキリさせる

    private void OnEnable()
    {
        _cameraTargetMode = serializedObject.FindProperty("cameraTargetMode");
        _cameraNameFilter = serializedObject.FindProperty("cameraNameFilter");

        _kernelType = serializedObject.FindProperty("kernelType");
        _evaluationMode = serializedObject.FindProperty("evaluationMode");
        _minOccludedSectors = serializedObject.FindProperty("minOccludedSectors");
        _maxConsecutiveEmptySectors = serializedObject.FindProperty("maxConsecutiveEmptySectors");
        _customPatternLutHex = serializedObject.FindProperty("customPatternLutHex");
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

        _enableVirtualDepthIntegration = serializedObject.FindProperty("enableVirtualDepthIntegration");
        _enableTagBasedOptimization = serializedObject.FindProperty("enableTagBasedOptimization");
        _enableTypeAwareDensity = serializedObject.FindProperty("enableTypeAwareDensity");
        _enableSoftOcclusionFade = serializedObject.FindProperty("enableSoftOcclusionFade");
        _holeFillingMethod = serializedObject.FindProperty("holeFillingMethod");
        _gridSize = serializedObject.FindProperty("gridSize");

        _morphKernelHalfSize = serializedObject.FindProperty("morphKernelHalfSize");
        _morphErodeIterations = serializedObject.FindProperty("morphErodeIterations");
        _morphDilateIterations = serializedObject.FindProperty("morphDilateIterations");

        // Debug Settings
        _debugSettings = serializedObject.FindProperty("debugSettings");
        if (_debugSettings != null)
        {
            _enablePixelTagMap = _debugSettings.FindPropertyRelative("enablePixelTagMap");
            _enableOcclusionMap = _debugSettings.FindPropertyRelative("enableOcclusionMap");
            _debugPatternId = _debugSettings.FindPropertyRelative("debugPatternId");
            _debugSectorId = _debugSettings.FindPropertyRelative("debugSectorId");
            _recordOcclusionDebugMap = _debugSettings.FindPropertyRelative("recordOcclusionDebugMap");
            _recordPixelTagMap = _debugSettings.FindPropertyRelative("recordPixelTagMap");
            _recordIntegratedDepthMap = _debugSettings.FindPropertyRelative("recordIntegratedDepthMap");
            _recordNeighborhoodMap = _debugSettings.FindPropertyRelative("recordNeighborhoodMap");
            _recordNeighborCountMap = _debugSettings.FindPropertyRelative("recordNeighborCountMap");
            _enableBufferManagerLog = _debugSettings.FindPropertyRelative("enableBufferManagerLog");
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var controller = (PCDOcclusionPipelineController)target;

        // =====================================================================
        // 1. オクルージョン判定モード (Mode 8 / 20 / 36 / 256) セレクター
        // =====================================================================
        DrawEvaluationModeHeader(controller);

        EditorGUILayout.Space(6);

        // =====================================================================
        // 2. コアアルゴリズム & 探索パラメータ
        // =====================================================================
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
            EditorGUILayout.PropertyField(_occlusionThreshold);
            EditorGUILayout.PropertyField(_occlusionFadeWidth);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        EditorGUILayout.Space(4);

        // =====================================================================
        // 3. カメラターゲット設定
        // =====================================================================
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

        EditorGUILayout.Space(4);

        // =====================================================================
        // 4. アブレーション・提案手法トグル
        // =====================================================================
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

        EditorGUILayout.Space(4);

        // =====================================================================
        // 5. モルフォロジー (ホールフィリング時のみ)
        // =====================================================================
        if (_holeFillingMethod.enumValueIndex != (int)PCD_HoleFillingMethod.None)
        {
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

        // =====================================================================
        // 6. 仮想接触オクルージョン
        // =====================================================================
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

        EditorGUILayout.Space(6);

        // =====================================================================
        // 7. デバッグ設定 (PCDOcclusionDebugSettings) - 折りたたみ可能
        // =====================================================================
        DrawDebugSettingsSection(controller);

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>
    /// モード (8, 20, 36, 256) の専用ヘッダー描画
    /// </summary>
    private void DrawEvaluationModeHeader(PCDOcclusionPipelineController controller)
    {
        EditorGUILayout.LabelField("Occlusion Evaluation Mode", EditorStyles.boldLabel);

        // 4モード + 従来 のクイック切り替えボタン
        EditorGUILayout.BeginHorizontal();
        DrawModeButton("Mode 8\n(Occ < 7)", PCD_OcclusionEvaluationMode.SectorThreshold, new Color(0.85f, 0.9f, 1.0f));
        DrawModeButton("Mode 20\n(R=4, L=1)", PCD_OcclusionEvaluationMode.SectorConsecutiveZeros, new Color(0.85f, 0.9f, 1.0f));
        DrawModeButton("Mode 36\n(36-LUT)", PCD_OcclusionEvaluationMode.RotationLUT36, new Color(0.85f, 1.0f, 0.9f));
        DrawModeButton("Mode 256 ★\n(Opt-LUT)", PCD_OcclusionEvaluationMode.PatternLUT256, new Color(0.35f, 0.95f, 0.55f));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);
        EditorGUILayout.PropertyField(_evaluationMode, new GUIContent("Evaluation Mode (詳細選択)"));

        var curMode = (PCD_OcclusionEvaluationMode)_evaluationMode.enumValueIndex;

        // モード別パラメータカード
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        switch (curMode)
        {
            case PCD_OcclusionEvaluationMode.PatternLUT256:
                EditorGUILayout.LabelField("★ Mode 256 (256パターン独立最適LUT - 推奨・最高精度)", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("平均 IoU: 96.02% (最高値) | 可視パターン数: 59 / 256 パターン\n完全最適化されたルックアップテーブルにより 1 クロックで高速判定します。", MessageType.Info);
                EditorGUILayout.PropertyField(_customPatternLutHex, new GUIContent("Custom 256-LUT HEX"));
                if (string.IsNullOrEmpty(_customPatternLutHex.stringValue))
                {
                    EditorGUILayout.LabelField("適用中 HEX:", PCDOcclusionPipelineController.DEFAULT_HEX_256, EditorStyles.miniLabel);
                }
                if (GUILayout.Button("最適化済みデフォルト解にリセット", EditorStyles.miniButton))
                {
                    _customPatternLutHex.stringValue = PCDOcclusionPipelineController.DEFAULT_HEX_256;
                }
                break;

            case PCD_OcclusionEvaluationMode.RotationLUT36:
                EditorGUILayout.LabelField("Mode 36 (幾何学的回転同値クラス 36-LUT)", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("平均 IoU: 95.98% | 可視クラス: 13 / 36 クラス (105 パターン)\n8方向の回転不変性を保った幾何学的ルックアップテーブル判定です。", MessageType.Info);
                EditorGUILayout.PropertyField(_customPatternLutHex, new GUIContent("Custom 36-LUT HEX"));
                if (string.IsNullOrEmpty(_customPatternLutHex.stringValue))
                {
                    EditorGUILayout.LabelField("適用中 HEX:", PCDOcclusionPipelineController.DEFAULT_HEX_36, EditorStyles.miniLabel);
                }
                if (GUILayout.Button("最適化済み36クラス解にリセット", EditorStyles.miniButton))
                {
                    _customPatternLutHex.stringValue = PCDOcclusionPipelineController.DEFAULT_HEX_36;
                }
                break;

            case PCD_OcclusionEvaluationMode.SectorConsecutiveZeros:
                EditorGUILayout.LabelField("Mode 20 (連続非占有規則: 占有数 R_th + 最大連続非占有 L_th)", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("平均 IoU: 95.93% (最適設定: R_th = 4, L_th = 1)\n遮蔽条件: 占有数 N_occ >= R_th かつ 最大連続非占有セクタ数 L_max <= L_th", MessageType.Info);
                EditorGUILayout.PropertyField(_minOccludedSectors, new GUIContent("R_th (最低占有セクタ数)"));
                EditorGUILayout.PropertyField(_maxConsecutiveEmptySectors, new GUIContent("L_th (許容最大連続非占有)"));
                if (GUILayout.Button("最適設定 (R_th=4, L_th=1) にリセット", EditorStyles.miniButton))
                {
                    _minOccludedSectors.intValue = 4;
                    _maxConsecutiveEmptySectors.intValue = 1;
                }
                break;

            case PCD_OcclusionEvaluationMode.SectorThreshold:
                EditorGUILayout.LabelField("Mode 8 (固定8候補 占有数判定)", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("平均 IoU: 95.92% (最適設定: N_occ < 7 で可視)\n遮蔽条件: 占有セクタ数 N_occ >= R_th", MessageType.Info);
                EditorGUILayout.PropertyField(_minOccludedSectors, new GUIContent("R_th (最低遮蔽セクタ数)"));
                if (GUILayout.Button("最適設定 (R_th=7) にリセット", EditorStyles.miniButton))
                {
                    _minOccludedSectors.intValue = 7;
                }
                break;

            case PCD_OcclusionEvaluationMode.Average:
                EditorGUILayout.LabelField("Legacy Average (従来平均内積方式)", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Bouchiba らの従来手法。全8方向の内積平均値と閾値 (occlusionThreshold) で判定します。", MessageType.None);
                break;
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawModeButton(string label, PCD_OcclusionEvaluationMode mode, Color activeColor)
    {
        bool isCurrent = _evaluationMode.enumValueIndex == (int)mode;
        Color prevColor = GUI.backgroundColor;
        if (isCurrent)
        {
            GUI.backgroundColor = activeColor;
        }

        if (GUILayout.Button(label, GUILayout.Height(34)))
        {
            _evaluationMode.enumValueIndex = (int)mode;
        }
        GUI.backgroundColor = prevColor;
    }

    /// <summary>
    /// デバッグ設定セクション（Foldout で普段は閉じられる）
    /// </summary>
    private void DrawDebugSettingsSection(PCDOcclusionPipelineController controller)
    {
        GUI.backgroundColor = new Color(0.95f, 0.95f, 0.95f);
        _showDebugSettings = EditorGUILayout.BeginFoldoutHeaderGroup(_showDebugSettings, "🛠️ Debug & Record Settings (変数調整・キャプチャ)");
        GUI.backgroundColor = Color.white;

        if (_showDebugSettings && _debugSettings != null)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.indentLevel++;

            // 画面表示デバッグ
            EditorGUILayout.LabelField("Display Debug (常時表示)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_enablePixelTagMap, new GUIContent("PixelTagMap 表示 (白黒由来マップ)"));
            EditorGUILayout.PropertyField(_enableOcclusionMap, new GUIContent("OcclusionMap 表示 (ヒートマップ)"));
            EditorGUILayout.PropertyField(_debugSectorId, new GUIContent("Sector ID (-1:OFF, 0..7:個別, 8:8bit統合, 9:真値)"));
            EditorGUILayout.PropertyField(_debugPatternId, new GUIContent("Pattern ID (-1:OFF, 0..255:特定パターン抽出)"));

            EditorGUILayout.Space(4);

            // 1フレーム記録デバッグ
            EditorGUILayout.LabelField("Record Debug (1フレーム記録)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_recordOcclusionDebugMap, new GUIContent("Record Occlusion Map"));
            EditorGUILayout.PropertyField(_recordPixelTagMap, new GUIContent("Record PixelTag Map"));
            EditorGUILayout.PropertyField(_recordIntegratedDepthMap, new GUIContent("Record Integrated Depth"));
            EditorGUILayout.PropertyField(_recordNeighborhoodMap, new GUIContent("Record Neighborhood Map"));
            EditorGUILayout.PropertyField(_recordNeighborCountMap, new GUIContent("Record SectorMask (RAW/CSV/PNG)"));

            EditorGUILayout.Space(4);

            // ログ
            EditorGUILayout.LabelField("Logger Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_enableBufferManagerLog, new GUIContent("BufferManager ログ出力"));

            EditorGUI.indentLevel--;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("📸 Quick Capture Buttons (PlayMode 専用)", EditorStyles.boldLabel);

            // クイックキャプチャボタン群
            GUI.backgroundColor = new Color(0.4f, 0.85f, 0.95f);
            if (GUILayout.Button("📸 占有セクターマスク (SectorMask RAW/CSV/PNG) を出力", GUILayout.Height(28)))
            {
                controller.TriggerRecordSectorMask();
                EditorUtility.SetDirty(controller);
            }

            GUI.backgroundColor = new Color(0.95f, 0.85f, 0.4f);
            if (GUILayout.Button("📸 オクルージョンマップ (OcclusionMap PNG/CSV) を出力", GUILayout.Height(26)))
            {
                controller.TriggerRecordOcclusionMap();
                EditorUtility.SetDirty(controller);
            }

            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("📸 全デバッグマップ 一括出力", GUILayout.Height(26)))
            {
                controller.TriggerRecordAllDebugMaps();
                EditorUtility.SetDirty(controller);
            }

            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }
}