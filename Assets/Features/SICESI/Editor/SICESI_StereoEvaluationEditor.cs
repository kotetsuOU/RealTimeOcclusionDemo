using System.IO;
using UnityEditor;
using UnityEngine;
using Core.Logging;

namespace SICESI.Editor
{
    /// <summary>
    /// SICE SI 評価実験コントローラー専用のカスタム Inspector エディター。
    /// タブ切り替え式により、日常的に使う最重要機能（8セクター二値マスク収集、GT撮影）と
    /// 実験スイープ、詳細パラメータ・レガシー機能を明確に分離して高い作業効率を提供します。
    /// </summary>
    [CustomEditor(typeof(SICESI_StereoEvaluationController))]
    public class SICESI_StereoEvaluationEditor : UnityEditor.Editor
    {
        private const string SelectedTabPrefKey = "SICESI_SelectedTabIndex";

        private int _selectedTab = 0;
        private static readonly string[] TabTitles = new string[]
        {
            "🪐 メイン実行 (Main)",
            "🚀 実験スイープ (Sweeps)",
            "⚙️ 詳細・レガシー設定 (Settings)"
        };

        // SerializedProperties
        private SerializedProperty _conditionNameProp;
        private SerializedProperty _outputDirectoryProp;
        private SerializedProperty _casesParentFolderProp;

        private SerializedProperty _leftEyeCameraProp;
        private SerializedProperty _rightEyeCameraProp;
        private SerializedProperty _sceneCaptureCameraProp;

        private SerializedProperty _groundTruthObjectProp;
        private SerializedProperty _virtualObjectProp;
        private SerializedProperty _pointCloudObjectProp;
        private SerializedProperty _dummyPointCloudProviderProp;
        private SerializedProperty _occlusionPipelineControllerProp;

        private SerializedProperty _densityUnitProp;
        private SerializedProperty _sweepDensitiesProp;
        private SerializedProperty _waitFramesProp;

        private SerializedProperty _sweepSectorsProp;
        private SerializedProperty _sweepDensitiesAcrossSectorsProp;
        private SerializedProperty _includeAverageModeProp;

        private SerializedProperty _sweepMaxConsecutiveZerosProp;
        private SerializedProperty _skipRedundantConditionsProp;
        private SerializedProperty _sweepDensitiesAcrossConsecutiveProp;

        private SerializedProperty _fixedEvaluationModeProp;
        private SerializedProperty _fixedMinOccludedSectorsProp;
        private SerializedProperty _sweepOcclusionThresholdsProp;

        private SerializedProperty _renderGroundTruthAsBlackProp;
        private SerializedProperty _renderVirtualObjectAsUnlitWhiteProp;
        private SerializedProperty _applySRGBConversionProp;
        private SerializedProperty _captureStereoEyesWithSceneProp;
        private SerializedProperty _groundTruthSkinColorProp;

        private bool _legacyFeaturesFolded = false;

        private void OnEnable()
        {
            _selectedTab = EditorPrefs.GetInt(SelectedTabPrefKey, 0);

            _conditionNameProp = serializedObject.FindProperty("conditionName");
            _outputDirectoryProp = serializedObject.FindProperty("outputDirectory");
            _casesParentFolderProp = serializedObject.FindProperty("casesParentFolder");

            _leftEyeCameraProp = serializedObject.FindProperty("leftEyeCamera");
            _rightEyeCameraProp = serializedObject.FindProperty("rightEyeCamera");
            _sceneCaptureCameraProp = serializedObject.FindProperty("sceneCaptureCamera");

            _groundTruthObjectProp = serializedObject.FindProperty("groundTruthObject");
            _virtualObjectProp = serializedObject.FindProperty("virtualObject");
            _pointCloudObjectProp = serializedObject.FindProperty("pointCloudObject");
            _dummyPointCloudProviderProp = serializedObject.FindProperty("dummyPointCloudProvider");
            _occlusionPipelineControllerProp = serializedObject.FindProperty("occlusionPipelineController");

            _densityUnitProp = serializedObject.FindProperty("densityUnit");
            _sweepDensitiesProp = serializedObject.FindProperty("sweepDensities");
            _waitFramesProp = serializedObject.FindProperty("waitFramesAfterDensityChange");

            _sweepSectorsProp = serializedObject.FindProperty("sweepSectors");
            _sweepDensitiesAcrossSectorsProp = serializedObject.FindProperty("sweepDensitiesAcrossSectors");
            _includeAverageModeProp = serializedObject.FindProperty("includeAverageMode");

            _sweepMaxConsecutiveZerosProp = serializedObject.FindProperty("sweepMaxConsecutiveZeros");
            _skipRedundantConditionsProp = serializedObject.FindProperty("skipRedundantConditions");
            _sweepDensitiesAcrossConsecutiveProp = serializedObject.FindProperty("sweepDensitiesAcrossConsecutive");

            _fixedEvaluationModeProp = serializedObject.FindProperty("fixedEvaluationMode");
            _fixedMinOccludedSectorsProp = serializedObject.FindProperty("fixedMinOccludedSectors");
            _sweepOcclusionThresholdsProp = serializedObject.FindProperty("sweepOcclusionThresholds");

            _renderGroundTruthAsBlackProp = serializedObject.FindProperty("renderGroundTruthAsBlack");
            _renderVirtualObjectAsUnlitWhiteProp = serializedObject.FindProperty("renderVirtualObjectAsUnlitWhite");
            _applySRGBConversionProp = serializedObject.FindProperty("applySRGBConversion");
            _captureStereoEyesWithSceneProp = serializedObject.FindProperty("captureStereoEyesWithScene");
            _groundTruthSkinColorProp = serializedObject.FindProperty("groundTruthSkinColor");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var controller = (SICESI_StereoEvaluationController)target;
            var collector = controller.GetComponent<SICESI_SectorMaskCollector>();

            // 1. トップ概要バー（条件名 & 保存先パス表示）
            DrawTopConditionHeader(controller);

            // 2. タブ切り替えツールバー
            EditorGUILayout.Space(6);
            int newTab = GUILayout.Toolbar(_selectedTab, TabTitles, GUILayout.Height(30));
            if (newTab != _selectedTab)
            {
                _selectedTab = newTab;
                EditorPrefs.SetInt(SelectedTabPrefKey, _selectedTab);
            }
            EditorGUILayout.Space(8);

            bool isBusy = controller.isCapturing || (collector != null && collector.isCollecting);
            bool canExecute = Application.isPlaying && !isBusy;

            // 3. 各タブの内容描画
            switch (_selectedTab)
            {
                case 0:
                    DrawMainTab(controller, collector, canExecute);
                    break;
                case 1:
                    DrawSweepsTab(controller, canExecute);
                    break;
                case 2:
                    DrawSettingsAndLegacyTab(controller, collector, canExecute);
                    break;
            }

            // 4. フッター（ステータス・実行状態メッセージ）
            DrawFooterStatus(controller, collector);

            serializedObject.ApplyModifiedProperties();
        }

        #region トップヘッダー

        private void DrawTopConditionHeader(SICESI_StereoEvaluationController controller)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("🎯 実験条件・ケース名:", EditorStyles.boldLabel, GUILayout.Width(130));
                    EditorGUILayout.PropertyField(_conditionNameProp, GUIContent.none);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("📁 出力先:", EditorStyles.miniLabel, GUILayout.Width(60));
                    EditorGUILayout.SelectableLabel(controller.ConditionRootDir, EditorStyles.miniLabel, GUILayout.Height(18));
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region タブ 0: メイン実行 (Main)

        private void DrawMainTab(SICESI_StereoEvaluationController controller, SICESI_SectorMaskCollector collector, bool canExecute)
        {
            int densityCount = controller.sweepDensities != null ? controller.sweepDensities.Length : 0;
            int totalSectorMasks = densityCount * 8;

            // 1. 【最重要・主推奨】8セクター二値マスク画面保存スイープボタン
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("【本命】256パターンLUT最適化用 実測データ収集", EditorStyles.boldLabel);

                GUI.enabled = canExecute;
                GUI.backgroundColor = new Color(0.2f, 0.9f, 0.5f); // 鮮やかなエメラルドグリーン

                string btnText = $"🪐 【推奨】8セクター二値マスク画面保存スイープ\n(全{densityCount}密度 × 8枚二値マスク + GPU真値)";
                if (GUILayout.Button(btnText, GUILayout.Height(50)))
                {
                    if (collector == null)
                    {
                        collector = controller.gameObject.AddComponent<SICESI_SectorMaskCollector>();
                    }

                    string sweepRootDir = Path.Combine(controller.ConditionRootDir, "Sector8MaskSweep");
                    if (EditorUtility.DisplayDialog(
                        "8セクター二値マスク画面保存スイープの開始",
                        $"条件名: [{controller.conditionName}]\n" +
                        $"密度数: {densityCount} 段階 ({controller.densityUnit})\n" +
                        $"撮影枚数: 1密度あたり二値マスク8枚＋GPU真値マスク1枚 (計 {totalSectorMasks} 枚 / 左右で {totalSectorMasks * 2} 枚)\n" +
                        $"出力先: {sweepRootDir}\n\n" +
                        $"【特長】\n" +
                        $"・0 or 255 の二値画像として各セクターマスクを個別に直接画面保存\n" +
                        $"・sRGB ガンマ変換歪みを 100% 排除し、99.998% の完全一致を保証\n" +
                        $"・GPU実遮蔽真値マスク (gpu_occluded_mask) も同一フレームで直接保存\n" +
                        $"・Python 側 optimize_pattern_rules.py で 256 パターン LUT 最適解を即時導出可能\n\n" +
                        $"一括自動撮影を開始しますか？",
                        "開始", "キャンセル"))
                    {
                        collector.RunSector8MaskSweep();
                    }
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;

                EditorGUILayout.HelpBox("💡 ガンマ歪みなく各セクターの真値二値マスクを出力します。Python 側の LUT 最適化スクリプトへの入力用として推奨されます。", MessageType.None);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // 2. スナップショット・日常撮影
            EditorGUILayout.LabelField("📸 スナップショット・個別撮影", EditorStyles.boldLabel);

            GUI.enabled = canExecute;

            // GT 撮影
            GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
            if (GUILayout.Button("📸 Ground Truth (手メッシュ遮蔽) の左右画像を撮影", GUILayout.Height(32)))
            {
                controller.CaptureGroundTruth();
            }

            // 現在条件の単発撮影
            GUI.backgroundColor = new Color(0.7f, 0.85f, 1.0f);
            if (GUILayout.Button($"📸 現在の条件 ({controller.conditionName}) で左右画像を撮影", GUILayout.Height(30)))
            {
                controller.CaptureCurrentCondition();
            }

            // シーン全体撮影
            if (controller.sceneCaptureCamera != null || (controller.captureStereoEyesWithScene && (controller.leftEyeCamera != null || controller.rightEyeCamera != null)))
            {
                GUI.backgroundColor = new Color(0.85f, 0.85f, 1.0f);
                string sceneBtnLabel = controller.captureStereoEyesWithScene && (controller.leftEyeCamera != null || controller.rightEyeCamera != null)
                    ? "📸 シーン保存 (Sceneカメラ + 左右両眼視点)"
                    : "📸 シーン保存カメラで現在の様子を撮影 (scene.png)";
                if (GUILayout.Button(sceneBtnLabel, GUILayout.Height(28)))
                {
                    controller.CaptureSceneOverview();
                }
            }

            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            EditorGUILayout.Space(8);

            // 3. クイックセットアップツール
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("🔧 クイックセットアップ", EditorStyles.boldLabel);
                if (GUILayout.Button("🔍 シーンからカメラ・点群プロバイダーを自動検出", GUILayout.Height(26)))
                {
                    controller.FindCameras();
                    controller.FindDummyComponents();
                    EditorUtility.SetDirty(controller);
                }
            }
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region タブ 1: 実験スイープ (Sweeps)

        private void DrawSweepsTab(SICESI_StereoEvaluationController controller, bool canExecute)
        {
            GUI.enabled = canExecute;

            // 1. 提案手法: 連続非占有スイープ
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("✨ SICE 2026 提案手法: 連続非占有セクター許容規則", EditorStyles.boldLabel);

                var validPairs = controller.GetValidConsecutivePairs();
                int pairCount = validPairs.Count;
                int densityMultiplier = controller.sweepDensitiesAcrossConsecutive && controller.sweepDensities != null && controller.sweepDensities.Length > 0
                    ? controller.sweepDensities.Length
                    : 1;
                int totalConsecutiveCombinations = densityMultiplier * pairCount;

                string skipModeNote = controller.skipRedundantConditions ? $"厳選 {pairCount}組 (幾何学的重複スキップ)" : $"全 {pairCount}組";
                string consecutiveBtnLabel = densityMultiplier > 1
                    ? $"🚀 占有数 × 連続非占有数 スイープ\n(全{densityMultiplier}密度 × {skipModeNote}: 計{totalConsecutiveCombinations}組)"
                    : $"🚀 占有数 × 連続非占有数 スイープ\n({skipModeNote}: 計{totalConsecutiveCombinations}組)";

                GUI.backgroundColor = new Color(1.0f, 0.55f, 0.7f);
                if (GUILayout.Button(consecutiveBtnLabel, GUILayout.Height(42)))
                {
                    string savePath = Path.Combine(controller.ConditionRootDir, "ConsecutiveSweep");
                    string desc = densityMultiplier > 1
                        ? $"{densityMultiplier} 段階の密度 × {skipModeNote} (計 {totalConsecutiveCombinations} パターン)"
                        : $"{skipModeNote} (計 {totalConsecutiveCombinations} パターン)";

                    if (EditorUtility.DisplayDialog(
                        "占有数 × 最大連続非占有数 スイープの開始",
                        $"条件名: [{controller.conditionName}]\n" +
                        $"評価モード: SectorConsecutiveZeros (提案手法)\n" +
                        $"探索条件: {desc}\n" +
                        $"重複スキップ: {(controller.skipRedundantConditions ? $"ON (72組 -> {pairCount}組)" : "OFF (全72組)")}\n\n" +
                        $"保存先: {savePath}\n\n自動一括撮影を開始しますか？",
                        "開始", "キャンセル"))
                    {
                        controller.RunConsecutiveSectorSweep();
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            // 2. 比較手法: 点群密度スイープ
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("📊 密度スイープ (固定パラメータで点群密度のみ変化)", EditorStyles.boldLabel);

                GUI.backgroundColor = new Color(1.0f, 0.7f, 0.4f);
                if (GUILayout.Button($"🚀 点群密度スイープを一括自動撮影 (GT + 全{controller.sweepDensities?.Length ?? 0}密度)", GUILayout.Height(36)))
                {
                    if (EditorUtility.DisplayDialog(
                        "点群密度スイープの開始",
                        $"条件名 [{controller.conditionName}] で {controller.sweepDensities.Length} 段階の密度スイープを開始しますか？\n保存先: {controller.ConditionRootDir}",
                        "開始", "キャンセル"))
                    {
                        controller.RunDensitySweep();
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            // 3. 比較手法: Bouchiba セクタースイープ
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("📐 Bouchiba 手法: セクター数スイープ (1〜8セクター)", EditorStyles.boldLabel);

                int perDensityCount = (controller.sweepSectors?.Length ?? 0) + (controller.includeAverageMode ? 1 : 0);
                int totalCombinations = (controller.sweepDensitiesAcrossSectors && controller.sweepDensities != null && controller.sweepDensities.Length > 0)
                    ? (controller.sweepDensities.Length * perDensityCount)
                    : perDensityCount;

                string modeDetail = controller.includeAverageMode ? "(Avg + 1〜8セクター)" : "(1〜8セクター)";
                string btnLabel = controller.sweepDensitiesAcrossSectors
                    ? $"🚀 Bouchiba スイープ (全{controller.sweepDensities?.Length ?? 0}密度 × {modeDetail}: 計{totalCombinations}組)"
                    : $"🚀 Bouchiba スイープ ({modeDetail}: 計{totalCombinations}組)";

                GUI.backgroundColor = new Color(0.95f, 0.65f, 0.95f);
                if (GUILayout.Button(btnLabel, GUILayout.Height(36)))
                {
                    string desc = controller.sweepDensitiesAcrossSectors
                        ? $"{controller.sweepDensities.Length} 段階の密度 × {modeDetail} (計 {totalCombinations} パターン)"
                        : $"{modeDetail} (計 {totalCombinations} パターン)";

                    if (EditorUtility.DisplayDialog(
                        "Bouchiba スイープの開始",
                        $"条件名 [{controller.conditionName}] で {desc} の自動撮影を開始しますか？\n保存先: {controller.ConditionRootDir}",
                        "開始", "キャンセル"))
                    {
                        controller.RunSectorSweep();
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            // 4. 比較手法: 密度 × 閾値 2D グリッドスイープ
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("🎯 密度 × オクルージョン閾値 2D グリッドスイープ", EditorStyles.boldLabel);

                string fixedModeStr = (controller.fixedEvaluationMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.Average)
                    ? "固定: Average"
                    : $"固定: R_th={controller.fixedMinOccludedSectors}";
                int dCount = controller.sweepDensities != null ? controller.sweepDensities.Length : 0;
                int tCount = controller.sweepOcclusionThresholds != null ? controller.sweepOcclusionThresholds.Length : 0;
                int totalGridCount = dCount * tCount;
                string gridBtnLabel = $"🚀 密度 × 閾値スイープ ({fixedModeStr}, {dCount}密度 × {tCount}閾値: 計{totalGridCount}組)";

                GUI.backgroundColor = new Color(0.4f, 0.85f, 0.95f);
                if (GUILayout.Button(gridBtnLabel, GUILayout.Height(36)))
                {
                    string modeDirName = (controller.fixedEvaluationMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.Average)
                        ? "Fixed_Average"
                        : $"Fixed_Sector_{controller.fixedMinOccludedSectors}";
                    string savePath = Path.Combine(controller.ConditionRootDir, modeDirName);

                    if (EditorUtility.DisplayDialog(
                        "密度 × オクルージョン閾値スイープの開始",
                        $"条件名: [{controller.conditionName}]\n" +
                        $"評価モード: {fixedModeStr}\n" +
                        $"グリッド組み合わせ: {dCount} 段階の密度 × {tCount} 段階の閾値 (計 {totalGridCount} パターン)\n\n" +
                        $"保存先: {savePath}\n\n自動一括撮影を開始しますか？",
                        "開始", "キャンセル"))
                    {
                        controller.RunDensityOcclusionThresholdSweep();
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndVertical();

            GUI.enabled = true;
        }

        #endregion

        #region タブ 2: 詳細・レガシー設定 (Settings & Legacy)

        private void DrawSettingsAndLegacyTab(SICESI_StereoEvaluationController controller, SICESI_SectorMaskCollector collector, bool canExecute)
        {
            // 1. 詳細パラメータ設定
            EditorGUILayout.LabelField("📋 実験パラメータ詳細設定", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("カメラ・オブジェクト参照", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_leftEyeCameraProp, new GUIContent("左目カメラ"));
                EditorGUILayout.PropertyField(_rightEyeCameraProp, new GUIContent("右目カメラ"));
                EditorGUILayout.PropertyField(_sceneCaptureCameraProp, new GUIContent("俯瞰カメラ"));
                EditorGUILayout.PropertyField(_groundTruthObjectProp, new GUIContent("手メッシュ (GT)"));
                EditorGUILayout.PropertyField(_virtualObjectProp, new GUIContent("仮想物体 (VO)"));
                EditorGUILayout.PropertyField(_pointCloudObjectProp, new GUIContent("点群オブジェクト"));
                EditorGUILayout.PropertyField(_dummyPointCloudProviderProp, new GUIContent("Dummy PC Provider"));
                EditorGUILayout.PropertyField(_occlusionPipelineControllerProp, new GUIContent("Pipeline Controller"));
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("点群密度スイープ設定", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_densityUnitProp, new GUIContent("密度単位"));
                EditorGUILayout.PropertyField(_sweepDensitiesProp, new GUIContent("スイープ密度一覧"), true);
                EditorGUILayout.PropertyField(_waitFramesProp, new GUIContent("密度切替後待機フレーム"));
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("セクタースイープ設定", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_sweepSectorsProp, new GUIContent("セクター閾値一覧"), true);
                EditorGUILayout.PropertyField(_sweepDensitiesAcrossSectorsProp, new GUIContent("密度も同時にスイープ"));
                EditorGUILayout.PropertyField(_includeAverageModeProp, new GUIContent("Averageモードも比較撮影"));
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("連続非占有 (提案手法) 設定", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_sweepMaxConsecutiveZerosProp, new GUIContent("連続非占有許容数一覧"), true);
                EditorGUILayout.PropertyField(_skipRedundantConditionsProp, new GUIContent("幾何学的重複をスキップ"));
                EditorGUILayout.PropertyField(_sweepDensitiesAcrossConsecutiveProp, new GUIContent("密度も同時にスイープ"));
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("出力・撮影オプション", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_outputDirectoryProp, new GUIContent("出力ルートディレクトリ"));
                EditorGUILayout.PropertyField(_casesParentFolderProp, new GUIContent("親フォルダ名"));
                EditorGUILayout.PropertyField(_applySRGBConversionProp, new GUIContent("sRGB変換を適用"));
                EditorGUILayout.PropertyField(_renderGroundTruthAsBlackProp, new GUIContent("GT手メッシュを黒描画"));
                EditorGUILayout.PropertyField(_renderVirtualObjectAsUnlitWhiteProp, new GUIContent("仮想物体をUnlit白描画"));
                EditorGUILayout.PropertyField(_captureStereoEyesWithSceneProp, new GUIContent("シーン撮影時に左右眼も保存"));
                EditorGUILayout.PropertyField(_groundTruthSkinColorProp, new GUIContent("シーン撮影時の手メッシュ肌色"));
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // 2. メンテナンスツール
            EditorGUILayout.LabelField("🛠️ メンテナンスツール", EditorStyles.boldLabel);
            GUI.backgroundColor = new Color(0.9f, 0.9f, 0.5f);
            if (GUILayout.Button("🛠️ 手メッシュの Read/Write を強制有効化 (エラー修復)", GUILayout.Height(28)))
            {
                FixHandMeshReadWrite();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(8);

            // 3. レガシー・非推奨機能 (折りたたみ)
            _legacyFeaturesFolded = EditorGUILayout.Foldout(_legacyFeaturesFolded, "⚠️ レガシー・非推奨機能 (旧スイープ・大量枚数保存)", true, EditorStyles.foldoutHeader);
            if (_legacyFeaturesFolded)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    int dCount = controller.sweepDensities != null ? controller.sweepDensities.Length : 0;
                    int totalPatterns = dCount * 256;

                    GUI.enabled = canExecute;

                    // 256パターン全枚数保存 (重い)
                    GUI.backgroundColor = new Color(0.7f, 0.8f, 0.9f);
                    string pattern256BtnLabel = $"256占有パターン個別マスク保存 (全{dCount}密度 × 256枚: 計{totalPatterns}枚)";
                    if (GUILayout.Button(pattern256BtnLabel, GUILayout.Height(30)))
                    {
                        if (collector == null) collector = controller.gameObject.AddComponent<SICESI_SectorMaskCollector>();
                        string sweepRootDir = Path.Combine(controller.ConditionRootDir, "Pattern256MaskSweep");
                        if (EditorUtility.DisplayDialog(
                            "256占有パターン単独二値マスクの一括取得",
                            $"条件名: [{controller.conditionName}]\n" +
                            $"撮影枚数: 計 {totalPatterns * 2} 枚\n\n" +
                            $"全256パターンのマスクを個別に256枚保存します。\n" +
                            $"※ 高速・軽量な「8セクター二値マスク画面保存スイープ」の利用を強く推奨します。",
                            "開始", "キャンセル"))
                        {
                            collector.RunPattern256MaskSweep();
                        }
                    }

                    EditorGUILayout.Space(4);

                    // 旧RAW同期バッファ取得
                    GUI.backgroundColor = new Color(0.7f, 0.7f, 0.7f);
                    if (GUILayout.Button("旧RAWバッファ吸い出しスナップショット (非推奨・逆変換誤差あり)", GUILayout.Height(26)))
                    {
                        if (collector == null) collector = controller.gameObject.AddComponent<SICESI_SectorMaskCollector>();
                        if (EditorUtility.DisplayDialog(
                            "旧RAWバッファ吸い出しスナップショット",
                            $"※ RAWバッファからの逆問題逆算には座標系オフセットやスキップ領域の誤差が含まれるため、画面保存手法への移行を推奨します。\n\n実行しますか？",
                            "開始", "キャンセル"))
                        {
                            collector.RunSectorMaskDensitySweep();
                        }
                    }

                    GUI.backgroundColor = Color.white;
                    GUI.enabled = true;
                }
                EditorGUILayout.EndVertical();
            }
        }

        #endregion

        #region フッター

        private void DrawFooterStatus(SICESI_StereoEvaluationController controller, SICESI_SectorMaskCollector collector)
        {
            EditorGUILayout.Space(8);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("※ 撮影機能は Unity の Play モード中に実行してください。", MessageType.Info);
            }

            if (controller.isCapturing)
            {
                EditorGUILayout.HelpBox($"進行中: {controller.statusMessage}", MessageType.Warning);
                Repaint();
            }
            else if (collector != null && collector.isCollecting)
            {
                EditorGUILayout.HelpBox($"SectorMask 収集進行中: {collector.statusMessage}", MessageType.Warning);
                Repaint();
            }
        }

        #endregion

        #region ヘルパー

        private static void FixHandMeshReadWrite()
        {
            string[] searchKeywords = new string[] { "LowPolyHandArm_Left", "LowPolyHandArm_Right", "LowPoly_Rigged_Hand" };
            int fixedCount = 0;

            foreach (var kw in searchKeywords)
            {
                string[] guids = AssetDatabase.FindAssets(kw);
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(".fbx") || path.EndsWith(".obj"))
                    {
                        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                        if (importer != null && !importer.isReadable)
                        {
                            importer.isReadable = true;
                            importer.SaveAndReimport();
                            fixedCount++;
                            AppLogger.Log(SICESI_StereoEvaluationController.TagCore, $"[SICESI] Read/Write を有効化しました: {path}");
                        }
                    }
                }
            }

            EditorUtility.DisplayDialog("修復完了", $"対象手メッシュ {fixedCount} 件の Read/Write を有効化しました。", "OK");
        }

        #endregion
    }
}
