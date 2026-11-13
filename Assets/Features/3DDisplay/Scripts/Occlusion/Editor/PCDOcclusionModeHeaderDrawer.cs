using UnityEditor;
using UnityEngine;
using static PCDRendererFeature;

/// <summary>
/// <see cref="PCDOcclusionPipelineController"/> Inspector 上部の
/// オクルージョン評価モード（Mode 8 / 20 / 36 / 256）切り替えカードおよび密度適応型 LUT 設定 UI を描画する Editor ヘルパー。
/// </summary>
internal sealed class PCDOcclusionModeHeaderDrawer
{
    private SerializedProperty _evaluationMode;
    private SerializedProperty _minOccludedSectorsMode8;
    private SerializedProperty _minOccludedSectorsMode20;
    private SerializedProperty _maxConsecutiveEmptySectors;
    private SerializedProperty _customPatternLutHex;
    private SerializedProperty _useDensityAdaptiveLut;
    private SerializedProperty _densityLutEntries;

    public void Bind(SerializedObject serializedObject)
    {
        _evaluationMode = serializedObject.FindProperty("evaluationMode");
        _minOccludedSectorsMode8 = serializedObject.FindProperty("minOccludedSectorsMode8");
        _minOccludedSectorsMode20 = serializedObject.FindProperty("minOccludedSectorsMode20");
        _maxConsecutiveEmptySectors = serializedObject.FindProperty("maxConsecutiveEmptySectors");
        _customPatternLutHex = serializedObject.FindProperty("customPatternLutHex");
        _useDensityAdaptiveLut = serializedObject.FindProperty("useDensityAdaptiveLut");
        _densityLutEntries = serializedObject.FindProperty("densityLutEntries");
    }

    public void Draw(SerializedObject serializedObject, PCDOcclusionPipelineController controller)
    {
        EditorGUILayout.LabelField("Occlusion Evaluation Mode", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        DrawModeButton("Mode 8\n(Occ < 7)", PCD_OcclusionEvaluationMode.SectorThreshold, new Color(0.85f, 0.9f, 1.0f));
        DrawModeButton("Mode 20\n(R=4, L=1)", PCD_OcclusionEvaluationMode.SectorConsecutiveZeros, new Color(0.85f, 0.9f, 1.0f));
        DrawModeButton("Mode 36\n(36-LUT)", PCD_OcclusionEvaluationMode.RotationLUT36, new Color(0.85f, 1.0f, 0.9f));
        DrawModeButton("Mode 256 ★\n(Opt-LUT)", PCD_OcclusionEvaluationMode.PatternLUT256, new Color(0.35f, 0.95f, 0.55f));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);
        EditorGUILayout.PropertyField(_evaluationMode, new GUIContent("Evaluation Mode (詳細選択)"));

        var curMode = (PCD_OcclusionEvaluationMode)_evaluationMode.enumValueIndex;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        switch (curMode)
        {
            case PCD_OcclusionEvaluationMode.PatternLUT256:
                DrawLutModeDetails(
                    serializedObject,
                    controller,
                    "★ Mode 256 (256パターン独立最適LUT - 推奨・最高精度)",
                    "平均 IoU: 96.02% (最高値) | 可視パターン数: 59 / 256 パターン\n完全最適化されたルックアップテーブルにより 1 クロックで高速判定します。",
                    "Custom 256-LUT HEX",
                    PCDOcclusionPipelineController.DEFAULT_HEX_256,
                    "最適化済みデフォルト解にリセット");
                break;

            case PCD_OcclusionEvaluationMode.RotationLUT36:
                DrawLutModeDetails(
                    serializedObject,
                    controller,
                    "Mode 36 (幾何学的回転同値クラス 36-LUT)",
                    "平均 IoU: 95.98% | 可視クラス: 13 / 36 クラス (105 パターン)\n8方向の回転不変性を保った幾何学的ルックアップテーブル判定です。",
                    "Custom 36-LUT HEX",
                    PCDOcclusionPipelineController.DEFAULT_HEX_36,
                    "最適化済み36クラス解にリセット");
                break;

            case PCD_OcclusionEvaluationMode.SectorConsecutiveZeros:
                EditorGUILayout.LabelField("Mode 20 (連続非占有規則: 占有数 R_th + 最大連続非占有 L_th)", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("平均 IoU: 95.93% (最適設定: R_th = 4, L_th = 1)\n遮蔽条件: 占有数 N_occ >= R_th かつ 最大連続非占有セクタ数 L_max <= L_th", MessageType.Info);
                EditorGUILayout.PropertyField(_minOccludedSectorsMode20, new GUIContent("R_th (最低占有セクタ数)"));
                EditorGUILayout.PropertyField(_maxConsecutiveEmptySectors, new GUIContent("L_th (許容最大連続非占有)"));
                if (GUILayout.Button("最適設定 (R_th=4, L_th=1) にリセット", EditorStyles.miniButton))
                {
                    _minOccludedSectorsMode20.intValue = 4;
                    _maxConsecutiveEmptySectors.intValue = 1;
                }
                break;

            case PCD_OcclusionEvaluationMode.SectorThreshold:
                EditorGUILayout.LabelField("Mode 8 (固定8候補 占有数判定)", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("平均 IoU: 95.92% (最適設定: N_occ < 7 で可視)\n遮蔽条件: 占有セクタ数 N_occ >= R_th", MessageType.Info);
                EditorGUILayout.PropertyField(_minOccludedSectorsMode8, new GUIContent("R_th (最低遮蔽セクタ数)"));
                if (GUILayout.Button("最適設定 (R_th=7) にリセット", EditorStyles.miniButton))
                {
                    _minOccludedSectorsMode8.intValue = 7;
                }
                break;

            case PCD_OcclusionEvaluationMode.Average:
                EditorGUILayout.LabelField("Legacy Average (従来平均内積方式)", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Bouchiba らの従来手法。全8方向の内積平均値と閾値 (occlusionThreshold) で判定します。", MessageType.None);
                break;
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawLutModeDetails(
        SerializedObject serializedObject,
        PCDOcclusionPipelineController controller,
        string title,
        string helpText,
        string fieldLabel,
        string defaultHex,
        string resetLabel)
    {
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(helpText, MessageType.Info);
        EditorGUILayout.PropertyField(_customPatternLutHex, new GUIContent(fieldLabel));
        if (string.IsNullOrEmpty(_customPatternLutHex.stringValue))
        {
            EditorGUILayout.LabelField("適用中 HEX:", defaultHex, EditorStyles.miniLabel);
        }
        if (GUILayout.Button(resetLabel, EditorStyles.miniButton))
        {
            _customPatternLutHex.stringValue = defaultHex;
        }
        DrawDensityAdaptiveLutSection(serializedObject, controller);
    }

    private void DrawDensityAdaptiveLutSection(SerializedObject serializedObject, PCDOcclusionPipelineController controller)
    {
        if (_useDensityAdaptiveLut == null || _densityLutEntries == null) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.PropertyField(
            _useDensityAdaptiveLut,
            new GUIContent(
                "Density-Adaptive LUT (密度別最適LUT)",
                "CalculateDensity のスクリーン空間密度に応じて最大8段階の最適 LUT をピクセル単位で切り替えます。"));

        if (!_useDensityAdaptiveLut.boolValue) return;

        EditorGUI.indentLevel++;
        EditorGUILayout.HelpBox(
            "Max Screen Density 以下のグリッドセルに対して各エントリの LUT HEX が適用されます（最大 8 段階・昇順推奨）。空の HEX または未指定範囲にはベース LUT が使用されます。",
            MessageType.None);
        if (_densityLutEntries.arraySize > PCDOcclusionPipelineController.MAX_DENSITY_LUT_BINS)
        {
            EditorGUILayout.HelpBox(
                $"エントリ数が上限 ({PCDOcclusionPipelineController.MAX_DENSITY_LUT_BINS}) を超えています。先頭 {PCDOcclusionPipelineController.MAX_DENSITY_LUT_BINS} 件のみが GPU へ転送されます。",
                MessageType.Warning);
        }

        EditorGUILayout.PropertyField(_densityLutEntries, new GUIContent("Density LUT Entries"), true);
        if (controller != null && GUILayout.Button("密度別デフォルト解にリセット", EditorStyles.miniButton))
        {
            Undo.RecordObject(controller, "Reset Density LUT Entries");
            controller.ResetDefaultDensityLutEntries();
            EditorUtility.SetDirty(controller);
            serializedObject.Update();
        }
        EditorGUI.indentLevel--;
    }

    private void DrawModeButton(string label, PCD_OcclusionEvaluationMode mode, Color activeColor)
    {
        bool isCurrent = _evaluationMode.enumValueIndex == (int)mode;
        Color prevColor = GUI.backgroundColor;
        if (isCurrent) GUI.backgroundColor = activeColor;
        if (GUILayout.Button(label, GUILayout.Height(34)))
        {
            _evaluationMode.enumValueIndex = (int)mode;
        }
        GUI.backgroundColor = prevColor;
    }
}

