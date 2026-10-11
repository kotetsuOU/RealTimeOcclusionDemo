using System.IO;
using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// SICE SI 各種パラメータスイープ実験タブ（連続非占有、セクター、密度、閾値グリッド）の描画クラス。
    /// </summary>
    public static class SICESI_TabSweepsDrawer
    {
        public static void Draw(SICESI_StereoEvaluationController controller, bool canExecute)
        {
            GUI.enabled = canExecute;

            // 1. 提案手法: 連続非占有スイープ
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("🌟 SICE 2026 提案手法: 連続非占有セクタースイープ", EditorStyles.boldLabel);

                var pairs = controller.GetValidConsecutivePairs();
                int densityMultiplier = controller.sweepDensitiesAcrossConsecutive && controller.sweepDensities != null ? controller.sweepDensities.Length : 1;
                int totalConditions = pairs.Count * densityMultiplier;
                string btnLabel = $"🚀 連続非占有スイープ実行 ({pairs.Count}条件 × {densityMultiplier}密度: 計{totalConditions}組)";

                GUI.backgroundColor = new Color(0.4f, 0.9f, 0.6f);
                if (GUILayout.Button(btnLabel, GUILayout.Height(36)))
                {
                    string savePath = Path.Combine(controller.ConditionRootDir, "ConsecutiveSectorSweep");
                    if (EditorUtility.DisplayDialog(
                        "連続非占有セクタースイープの開始",
                        $"条件名: [{controller.conditionName}]\n" +
                        $"幾何学的有効条件数: {pairs.Count} パターン\n" +
                        $"全密度組み合わせ: {totalConditions} 組\n\n" +
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

            // 2. 従来手法: セクター閾値スイープ
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("📊 比較手法: セクター閾値 R_th スイープ (1〜8)", EditorStyles.boldLabel);

                int secCount = controller.sweepSectors != null ? controller.sweepSectors.Length : 0;
                int densityMultiplier = controller.sweepDensitiesAcrossSectors && controller.sweepDensities != null ? controller.sweepDensities.Length : 1;
                int totalConditions = secCount * densityMultiplier;
                string btnLabel = $"🚀 セクタースイープ実行 ({secCount}セクター × {densityMultiplier}密度: 計{totalConditions}組)";

                GUI.backgroundColor = new Color(0.6f, 0.8f, 1.0f);
                if (GUILayout.Button(btnLabel, GUILayout.Height(36)))
                {
                    string savePath = Path.Combine(controller.ConditionRootDir, "SectorSweep");
                    if (EditorUtility.DisplayDialog(
                        "セクター閾値スイープの開始",
                        $"条件名: [{controller.conditionName}]\n" +
                        $"セクター数: {secCount} 段階\n" +
                        $"全密度組み合わせ: {totalConditions} 組\n" +
                        $"Averageモード含む: {(controller.includeAverageMode ? "はい" : "いいえ")}\n\n" +
                        $"保存先: {savePath}\n\n自動一括撮影を開始しますか？",
                        "開始", "キャンセル"))
                    {
                        controller.RunSectorSweep();
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            // 3. 点群密度単独スイープ
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("📈 点群密度 単独スイープ", EditorStyles.boldLabel);

                int dCount = controller.sweepDensities != null ? controller.sweepDensities.Length : 0;
                string btnLabel = $"🚀 密度スイープ実行 ({dCount}段階: {controller.densityUnit})";

                GUI.backgroundColor = new Color(0.7f, 0.9f, 0.8f);
                if (GUILayout.Button(btnLabel, GUILayout.Height(32)))
                {
                    string savePath = Path.Combine(controller.ConditionRootDir, "DensitySweep");
                    if (EditorUtility.DisplayDialog(
                        "点群密度スイープの開始",
                        $"条件名: [{controller.conditionName}]\n" +
                        $"密度数: {dCount} 段階 ({controller.densityUnit})\n" +
                        $"保存先: {savePath}\n\n自動一括撮影を開始しますか？",
                        "開始", "キャンセル"))
                    {
                        controller.RunDensitySweep();
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
    }
}
