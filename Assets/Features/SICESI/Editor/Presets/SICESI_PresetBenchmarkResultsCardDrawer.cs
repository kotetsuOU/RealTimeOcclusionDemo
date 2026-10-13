using System.IO;
using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// 再検証（ベンチマーク）完了後の 5 手法全体平均 IoU サマリー結果カードの描画を担当する Drawer クラス。
    /// </summary>
    public static class SICESI_PresetBenchmarkResultsCardDrawer
    {
        /// <summary>
        /// 直近の再検証（ベンチマーク）5手法平均結果カードを描画します。
        /// </summary>
        public static void Draw(SICESI_StereoEvaluationController controller)
        {
            if (controller == null) return;

            var runner = controller.GeneralizationRunner;
            if (runner == null || runner.lastMethodAverages == null || runner.lastMethodAverages.Count == 0) return;

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.BeginHorizontal();
                {
                    GUI.color = new Color(0.2f, 0.85f, 1.0f);
                    EditorGUILayout.LabelField("<b>📊 直近の再検証結果: 5手法の全体平均 IoU (サマリー)</b>", new GUIStyle(EditorStyles.label) { richText = true });
                    GUI.color = Color.white;

                    string avgCsvPath = Path.Combine(controller.outputDirectory, "AdditionalExperiments", "PresetBenchmark", "preset_benchmark_overall_average.csv");
                    if (File.Exists(avgCsvPath) && GUILayout.Button("📂 平均CSVを開く", GUILayout.Width(110)))
                    {
                        EditorUtility.RevealInFinder(avgCsvPath);
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2);

                int idx = 1;
                foreach (var kvp in runner.lastMethodAverages)
                {
                    var acc = kvp.Value;
                    string colorPrefix = idx == 5 ? "<color=#00e676><b>" : (idx >= 3 ? "<color=#29b6f6>" : "<color=#ffffff>");
                    string colorSuffix = (idx >= 3) ? "</color>" : "</color>";
                    if (idx == 5) colorSuffix = "</b></color>";

                    EditorGUILayout.LabelField(
                        $"  {idx}. {acc.methodName,-35} : {colorPrefix}Mean IoU = {acc.AvgMeanIoU:P2}{colorSuffix} (左: {acc.AvgLeftIoU:P2}, 右: {acc.AvgRightIoU:P2}) [N={acc.count}]",
                        new GUIStyle(EditorStyles.miniLabel) { richText = true });
                    idx++;
                }

                EditorGUILayout.Space(2);
                EditorGUILayout.HelpBox("💡 各 Case・各密度を通した全体平均値です。詳細は preset_benchmark_summary.csv および preset_benchmark_overall_average.csv に記録されています。", MessageType.None);
            }
            EditorGUILayout.EndVertical();
        }
    }
}
