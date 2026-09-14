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

            GUI.enabled = Application.isPlaying && !controller.isCapturing;

            // GT撮影ボタン
            GUI.backgroundColor = new Color(0.6f, 0.9f, 0.6f);
            if (GUILayout.Button("📸 Ground Truth (手メッシュ遮蔽) の左右画像を撮影", GUILayout.Height(32)))
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
                    $"条件名 [{controller.conditionName}] で {controller.sweepDensities.Length} 段階の密度スイープを開始しますか？\n保存先: {controller.outputDirectory}",
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
                    $"条件名 [{controller.conditionName}] で {desc} の自動撮影を開始しますか？\n保存先: {Path.Combine(controller.outputDirectory, "SectorSweep")}",
                    "開始", "キャンセル"))
                {
                    controller.RunSectorSweep();
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
