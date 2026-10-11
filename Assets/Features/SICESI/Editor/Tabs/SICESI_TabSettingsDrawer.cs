using System.IO;
using UnityEditor;
using UnityEngine;
using Core.Logging;

namespace SICESI.Editor
{
    /// <summary>
    /// SICE SI 詳細パラメータ設定およびレガシー機能タブの描画クラス。
    /// </summary>
    public static class SICESI_TabSettingsDrawer
    {
        private static bool _legacyFeaturesFolded = false;

        public static void Draw(
            SICESI_StereoEvaluationController controller,
            SICESI_SectorMaskCollector collector,
            SerializedObject serializedObject,
            bool canExecute)
        {
            // 1. 詳細パラメータ設定
            EditorGUILayout.LabelField("📋 実験パラメータ詳細設定", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("カメラ・オブジェクト参照", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("leftEyeCamera"), new GUIContent("左目カメラ"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("rightEyeCamera"), new GUIContent("右目カメラ"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sceneCaptureCamera"), new GUIContent("俯瞰カメラ"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("groundTruthObject"), new GUIContent("手メッシュ (GT)"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("virtualObject"), new GUIContent("仮想物体 (VO)"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("pointCloudObject"), new GUIContent("点群オブジェクト"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("dummyPointCloudProvider"), new GUIContent("Dummy PC Provider"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("occlusionPipelineController"), new GUIContent("Pipeline Controller"));
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("点群密度スイープ設定", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("densityUnit"), new GUIContent("物理密度単位"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sweepDensities"), new GUIContent("スイープ密度一覧"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("waitFramesAfterDensityChange"), new GUIContent("密度変更後 待機フレーム数"));
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("セクター・オクルージョンスイープ設定", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sweepSectors"), new GUIContent("セクター閾値一覧 (1〜8)"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sweepDensitiesAcrossSectors"), new GUIContent("全密度と組み合わせる"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("includeAverageMode"), new GUIContent("Averageモードも含める"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sweepMaxConsecutiveZeros"), new GUIContent("最大連続非占有一覧 (0〜8)"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("skipRedundantConditions"), new GUIContent("重複条件を自動スキップ"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sweepDensitiesAcrossConsecutive"), new GUIContent("連続非占有×全密度"));
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("オクルージョン判定閾値グリッド設定", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("fixedEvaluationMode"), new GUIContent("固定評価モード"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("fixedMinOccludedSectors"), new GUIContent("固定セクター数 (R_th)"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("sweepOcclusionThresholds"), new GUIContent("閾値一覧 (0.1〜0.9)"), true);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("画像保存・マテリアル詳細設定", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("renderGroundTruthAsBlack"), new GUIContent("GT手メッシュを黒化マスク化"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("renderVirtualObjectAsUnlitWhite"), new GUIContent("仮想物体を完全Unlit白化"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("applySRGBConversion"), new GUIContent("sRGBガンマ補正を適用"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("captureStereoEyesWithScene"), new GUIContent("シーン保存時に左右目線も保存"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("groundTruthSkinColor"), new GUIContent("シーン保存時の手メッシュ肌色"));
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // 2. FBX Read/Write 一括修復ボタン
            GUI.backgroundColor = new Color(1.0f, 0.9f, 0.6f);
            if (GUILayout.Button("🛠️ 手メッシュ FBX の Read/Write を一括有効化 (頂点読み込みエラー修復)", GUILayout.Height(30)))
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
    }
}
