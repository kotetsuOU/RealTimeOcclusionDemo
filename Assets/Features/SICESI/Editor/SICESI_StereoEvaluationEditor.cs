using UnityEditor;
using UnityEngine;
using System.IO;

namespace SICESI.Editor
{
    [CustomEditor(typeof(SICESI_StereoEvaluationController))]
    public class SICESI_StereoEvaluationEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var controller = (SICESI_StereoEvaluationController)target;

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("【SICE SI 臨時操作パネル】", EditorStyles.boldLabel);

            // カメラ・コンポーネント自動検出ボタン
            if (GUILayout.Button("🔍 シーンからカメラ・点群プロバイダーを自動検出", GUILayout.Height(28)))
            {
                controller.FindCameras();
                controller.FindDummyComponents();
                EditorUtility.SetDirty(controller);
            }

            EditorGUILayout.Space(4);

            // Read/Write 有効化修復ボタン
            GUI.backgroundColor = new Color(0.9f, 0.9f, 0.5f);
            if (GUILayout.Button("🛠️ 手メッシュの Read/Write を強制有効化 (エラー修復)", GUILayout.Height(28)))
            {
                FixHandMeshReadWrite();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(8);

            var collector = controller.GetComponent<SICESI_SectorMaskCollector>();
            var sceneCapturer = controller.GetComponent<SICESI_SceneCapturer>();
            if (sceneCapturer == null)
            {
                sceneCapturer = controller.gameObject.AddComponent<SICESI_SceneCapturer>();
            }

            bool isBusy = controller.isCapturing || (collector != null && collector.isCollecting);
            GUI.enabled = Application.isPlaying && !isBusy;

            // =========================================================================
            // 【配置説明シーン画像保存 (Scene Capture)】
            // =========================================================================
            EditorGUILayout.Space(8);
            GUI.backgroundColor = new Color(0.3f, 0.85f, 0.95f);
            EditorGUILayout.LabelField("【配置説明シーン画像保存 (Scene Capture)】", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("現在のシーン/Case名:", GUILayout.Width(140));
            controller.conditionName = EditorGUILayout.TextField(controller.conditionName);
            EditorGUILayout.EndHorizontal();

            string sceneDir = Path.Combine(controller.ConditionRootDir, "Scene");
            EditorGUILayout.LabelField($"📁 保存先: {sceneDir}", EditorStyles.miniLabel);

            if (controller.sceneCaptureCamera == null)
            {
                EditorGUILayout.HelpBox("⚠️ sceneCaptureCamera が未設定です。インスペクターで撮影用Cameraを指定してください。", MessageType.Warning);
            }

            GUI.backgroundColor = new Color(0.95f, 0.85f, 0.55f);
            if (GUILayout.Button("📸 手メッシュ一時肌色化でシーン画像を撮影 (scene.png + metadata)", GUILayout.Height(36)))
            {
                controller.CaptureSceneImage((success, path) =>
                {
                    if (success)
                    {
                        EditorUtility.DisplayDialog("撮影完了", $"配置説明画像とメタデータを正常に保存しました:\n{path}", "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("撮影失敗", $"撮影に失敗しました:\n{path}", "OK");
                    }
                });
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("---------------- 評価データ収集 / デバッグ撮影 ----------------", EditorStyles.miniLabel);

            // Mesh Depth GT 設定
            controller.useMeshDepthGT = EditorGUILayout.ToggleLeft("🎯 真のGround Truth生成 (Mesh Depth GT: GPU生深度直接比較 / 0.5%誤差ゼロ)", controller.useMeshDepthGT, EditorStyles.boldLabel);
            if (controller.useMeshDepthGT)
            {
                EditorGUILayout.HelpBox("カラー描画や黒マテリアル差し替えを行わず、パイプラインと同一のGPU生深度マップ (ViewPositionMap) を直接比較して完全二値の真GTを生成します。", MessageType.Info);
            }

            // GT撮影ボタン
            GUI.backgroundColor = controller.useMeshDepthGT ? new Color(0.4f, 0.95f, 0.7f) : new Color(0.6f, 0.9f, 0.6f);
            string gtBtnText = controller.useMeshDepthGT 
                ? "🎯 真のGround Truth (Mesh Depth GT) の左右画像を生成・保存" 
                : "📸 Ground Truth (カラーラスタライズ) の左右画像を撮影";

            if (GUILayout.Button(gtBtnText, GUILayout.Height(34)))
            {
                controller.CaptureGroundTruth();
            }

            EditorGUILayout.Space(4);

            // 単発撮影ボタン
            GUI.backgroundColor = new Color(0.7f, 0.85f, 1.0f);
            if (GUILayout.Button($"📸 現在の条件 ({controller.conditionName}) で左右画像を撮影", GUILayout.Height(32)))
            {
                controller.CaptureCurrentCondition();
            }

            EditorGUILayout.Space(6);

            // 密度スイープ一括実行ボタン
            GUI.backgroundColor = new Color(1.0f, 0.7f, 0.4f);
            if (GUILayout.Button("🚀 点群密度スイープを一括自動撮影 (GT + 全密度)", GUILayout.Height(40)))
            {
                if (EditorUtility.DisplayDialog(
                    "点群密度スイープの開始",
                    $"条件名 [{controller.conditionName}] で {controller.sweepDensities.Length} 段階の密度スイープを開始しますか？\n保存先: {controller.ConditionRootDir}",
                    "開始", "キャンセル"))
                {
                    controller.RunDensitySweep();
                }
            }

            EditorGUILayout.Space(4);

            // Bouchiba セクタースイープ一括実行ボタン
            GUI.backgroundColor = new Color(0.95f, 0.65f, 0.95f);
            int perDensityCount = controller.sweepSectors.Length + (controller.includeAverageMode ? 1 : 0);
            int totalCombinations = (controller.sweepDensitiesAcrossSectors && controller.sweepDensities != null && controller.sweepDensities.Length > 0)
                ? (controller.sweepDensities.Length * perDensityCount)
                : perDensityCount;

            string modeDetail = controller.includeAverageMode ? "(Avg + 1〜8セクター)" : "(1〜8セクター)";
            string btnLabel = controller.sweepDensitiesAcrossSectors
                ? $"🚀 Bouchiba スイープ (全{controller.sweepDensities.Length}密度 × {modeDetail}: 計{totalCombinations}組)"
                : $"🚀 Bouchiba スイープ ({modeDetail}: 計{totalCombinations}組)";

            if (GUILayout.Button(btnLabel, GUILayout.Height(40)))
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

            EditorGUILayout.Space(4);

            // 占有数 × 最大連続非占有数 スイープ一括実行ボタン (SICE 2026 Proposed)
            GUI.backgroundColor = new Color(1.0f, 0.55f, 0.7f);
            var validPairs = controller.GetValidConsecutivePairs();
            int pairCount = validPairs.Count;
            int densityMultiplier = controller.sweepDensitiesAcrossConsecutive && controller.sweepDensities != null && controller.sweepDensities.Length > 0
                ? controller.sweepDensities.Length
                : 1;
            int totalConsecutiveCombinations = densityMultiplier * pairCount;

            string skipModeNote = controller.skipRedundantConditions ? $"厳選 {pairCount}組 (重複スキップ)" : $"全 {pairCount}組";
            string consecutiveBtnLabel = densityMultiplier > 1
                ? $"🚀 占有数 × 連続非占有数 スイープ (全{densityMultiplier}密度 × {skipModeNote}: 計{totalConsecutiveCombinations}組)"
                : $"🚀 占有数 × 連続非占有数 スイープ ({skipModeNote}: 計{totalConsecutiveCombinations}組)";

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

            EditorGUILayout.Space(4);

            // 密度 × オクルージョン閾値スイープ一括実行ボタン
            GUI.backgroundColor = new Color(0.4f, 0.85f, 0.95f);
            string fixedModeStr = (controller.fixedEvaluationMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.Average)
                ? "固定: Average"
                : $"固定: R_th={controller.fixedMinOccludedSectors}";
            int densityCount = controller.sweepDensities != null ? controller.sweepDensities.Length : 0;
            int threshCount = controller.sweepOcclusionThresholds != null ? controller.sweepOcclusionThresholds.Length : 0;
            int totalGridCount = densityCount * threshCount;
            string gridBtnLabel = $"🚀 密度 × 閾値スイープ ({fixedModeStr}, {densityCount}密度 × {threshCount}閾値: 計{totalGridCount}組)";

            if (GUILayout.Button(gridBtnLabel, GUILayout.Height(40)))
            {
                string modeDirName = (controller.fixedEvaluationMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.Average)
                    ? "Fixed_Average"
                    : $"Fixed_Sector_{controller.fixedMinOccludedSectors}";
                string savePath = Path.Combine(controller.ConditionRootDir, modeDirName);

                if (EditorUtility.DisplayDialog(
                    "密度 × オクルージョン閾値スイープの開始",
                    $"条件名: [{controller.conditionName}]\n" +
                    $"評価モード: {fixedModeStr}\n" +
                    $"グリッド組み合わせ: {densityCount} 段階の密度 × {threshCount} 段階の閾値 (計 {totalGridCount} パターン)\n\n" +
                    $"保存先: {savePath}\n\n自動一括撮影を開始しますか？",
                    "開始", "キャンセル"))
                {
                    controller.RunDensityOcclusionThresholdSweep();
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("🔬 占有セクターマスク (SectorMask) 実測データ取得", EditorStyles.boldLabel);

            if (collector == null)
            {
                collector = controller.gameObject.AddComponent<SICESI_SectorMaskCollector>();
            }

            // 【主推奨】Point A 生データ直接保存スイープボタン (表示補正完全排除・完全同期 AsyncGPUReadback 方式)
            GUI.backgroundColor = new Color(0.2f, 0.9f, 0.5f);
            string unifiedSectorMaskBtnLabel = $"🪐 【推奨】表示補正前(Point A) 生データ直接保存スイープ (全{densityCount}密度 × 生バッファ+完全同期マスク群)";

            if (GUILayout.Button(unifiedSectorMaskBtnLabel, GUILayout.Height(46)))
            {
                string sweepRootDir = Path.Combine(controller.ConditionRootDir, "Sector8MaskSweep");
                if (EditorUtility.DisplayDialog(
                    "Point A 生データ直接保存スイープの開始",
                    $"条件名: [{controller.conditionName}]\n" +
                    $"密度数: {densityCount} 段階 ({controller.densityUnit})\n" +
                    $"出力先: {sweepRootDir}\n\n" +
                    $"【特長 (Point A 生データ直接保存方式)】\n" +
                    $"・SRDisplay 表示補正 (幾何歪み・ガンマ歪み) を完全排除\n" +
                    $"・AsyncGPUReadback により NeighborCountMap + OriginTypeMap を完全同期取得\n" +
                    $"・1回のリードバックで以下をすべて一括保存:\n" +
                    $"  A_raw_uint32.bin / origin_type_raw_uint32.bin (生バイナリ)\n" +
                    $"  evaluated_mask_pre_correction.png (bit 12: 評価領域 E)\n" +
                    $"  sector_occlusion_mask_direct.png (bit 13: セクター遮蔽)\n" +
                    $"  final_occlusion_mask_direct.png (bit 13 | D: 最終遮蔽)\n" +
                    $"  origin_type_map_direct.png (最前面タグ)\n" +
                    $"  sector_mask.png / sector_0..7_mask.png (8セクター)\n" +
                    $"・画面切り替え不要で高速・左右両目対応\n\n" +
                    $"一括自動保存を開始しますか？",
                    "開始", "キャンセル"))
                {
                    collector.RunSector8MaskSweep();
                }
            }

            EditorGUILayout.Space(6);
            GUI.backgroundColor = new Color(1.0f, 0.75f, 0.2f);
            EditorGUILayout.LabelField("【パイプライン詳細診断 (Stage Diagnosis: Point A / Blit転送 / 無損失Load)】", EditorStyles.boldLabel);
            if (GUILayout.Button("🔬 左目単独 厳密ステージ診断実行 (Point A生判定 + 通常/無損失転送 + 白一色VO)", GUILayout.Height(40)))
            {
                controller.RunLeftEyeStageDiagnosis();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(4);

            // 256占有パターン単独マスク 一括自動取得ボタン (旧36クラスを完全包括・全256枚保存)
            GUI.backgroundColor = new Color(0.35f, 0.75f, 0.95f);
            int totalPatterns = densityCount * 256;
            string pattern256BtnLabel = $"256占有パターン個別マスク保存 (全{densityCount}密度 × 256枚: 計{totalPatterns}枚)";

            if (GUILayout.Button(pattern256BtnLabel, GUILayout.Height(32)))
            {
                string sweepRootDir = Path.Combine(controller.ConditionRootDir, "Pattern256MaskSweep");
                if (EditorUtility.DisplayDialog(
                    "256占有パターン単独二値マスクの一括取得",
                    $"条件名: [{controller.conditionName}]\n" +
                    $"密度数: {densityCount} 段階 ({controller.densityUnit})\n" +
                    $"撮影枚数: 1密度あたり256パターン (計 {totalPatterns} 枚 / 左右で {totalPatterns * 2} 枚)\n" +
                    $"出力先: {sweepRootDir}\n\n" +
                    $"全256パターンのマスクを個別に256枚保存します。\n" +
                    $"※ 高速な「統合セクターマスク画面保存スイープ」の利用を推奨します。",
                    "開始", "キャンセル"))
                {
                    collector.RunPattern256MaskSweep();
                }
            }

            EditorGUILayout.Space(2);

            // 旧RAW同期バッファ取得ボタン (参考用)
            GUI.backgroundColor = new Color(0.7f, 0.7f, 0.7f);
            string rawSectorMaskBtnLabel = $"旧RAWバッファ吸い出しスナップショット (非推奨・逆変換誤差あり)";

            if (GUILayout.Button(rawSectorMaskBtnLabel, GUILayout.Height(26)))
            {
                string sweepRootDir = Path.Combine(controller.ConditionRootDir, "SectorMaskSweep");
                if (EditorUtility.DisplayDialog(
                    "旧RAWバッファ吸い出しスナップショット",
                    $"条件名: [{controller.conditionName}]\n" +
                    $"※ RAWバッファからの逆問題逆算には座標系オフセットやスキップ領域の誤差が含まれるため、画面保存手法への移行を推奨します。\n\n" +
                    $"実行しますか？",
                    "開始", "キャンセル"))
                {
                    collector.RunSectorMaskDensitySweep();
                }
            }

            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

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
                            Debug.Log($"[SICESI] Read/Write を有効化しました: {path}");
                        }
                    }
                }
            }

            EditorUtility.DisplayDialog("修復完了", $"対象手メッシュ {fixedCount} 件の Read/Write を有効化しました。", "OK");
        }
    }
}
