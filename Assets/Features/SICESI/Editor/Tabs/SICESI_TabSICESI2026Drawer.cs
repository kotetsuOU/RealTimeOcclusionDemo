using System.IO;
using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// SICE SI 2026 メイン実験タブ（8セクター二値マスク収集、バッチ再実験、スナップショット）の描画クラス。
    /// </summary>
    public static class SICESI_TabSICESI2026Drawer
    {
        public static void Draw(SICESI_StereoEvaluationController controller, SICESI_SectorMaskCollector collector, bool canExecute)
        {
            int densityCount = controller.sweepDensities != null ? controller.sweepDensities.Length : 0;
            int totalSectorMasks = densityCount * 8;

            // 1. 【本命】8セクター二値マスク画面保存スイープボタン
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

            // 2. 【再実験】過去Case一括自動再実験 (Batch Re-sweep)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("📂 過去Case一括自動再実験 (Batch Re-Sweep)", EditorStyles.boldLabel);

                string targetScanDir = Path.Combine(controller.outputDirectory, controller.casesParentFolder);
                EditorGUILayout.LabelField($"走査対象フォルダ: {targetScanDir}", EditorStyles.wordWrappedMiniLabel);

                GUI.enabled = canExecute && !controller.BatchReSweepRunner.isRunning;
                GUI.backgroundColor = new Color(0.3f, 0.75f, 0.95f); // 鮮やかなスカイブルー

                string batchBtnText = "📂 フォルダ内の全Caseを自動復元・一括再実験実行\n(scene_transforms.json から各VO・手を自動切替)";
                if (GUILayout.Button(batchBtnText, GUILayout.Height(42)))
                {
                    if (EditorUtility.DisplayDialog(
                        "過去Case一括自動再実験の開始",
                        $"対象親フォルダ:\n{targetScanDir}\n\n" +
                        $"配下の全 Case (1, 2, 3...) の scene_transforms.json を自動走査し、\n" +
                        $"各仮想オブジェクト (Fox1, happy, elephal等) と手メッシュを自動検索・切り替えて\n" +
                        $"Transform を復元しながら 8セクター二値マスクスイープを全自動で連続実行します。\n\n" +
                        $"一括再実験を開始しますか？",
                        "開始", "キャンセル"))
                    {
                        controller.RunBatchReSweep(targetScanDir);
                    }
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;

                EditorGUILayout.HelpBox("💡 各 Case フォルダの scene_transforms.json に記録されたオブジェクト名からシーン内オブジェクトを自動検出・アクティブ切り替えして全自動で再実験します。", MessageType.None);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // 3. スナップショット・日常撮影
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
                    : "📸 シーン保存カメラで現在の様子を撮影 (scene_overview.png)";
                if (GUILayout.Button(sceneBtnLabel, GUILayout.Height(28)))
                {
                    controller.CaptureSceneOverview();
                }
            }

            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            EditorGUILayout.Space(8);

            // 4. クイックセットアップツール
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
    }
}
