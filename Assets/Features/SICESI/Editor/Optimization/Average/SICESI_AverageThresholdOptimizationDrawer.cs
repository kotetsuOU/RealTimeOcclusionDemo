using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// SICE SI 追加実験タブにおいて、Average 手法のオクルージョン判定閾値 T_ave の
    /// 0.00〜1.00 グリッド最適化スイープセクション全体を統括する Facade Drawer クラス。
    /// </summary>
    public static class SICESI_AverageThresholdOptimizationDrawer
    {
        private static bool _sectionFolded = false;

        /// <summary>
        /// Average 手法 T_ave 最適化セクションを描画します。
        /// </summary>
        public static void Draw(SICESI_StereoEvaluationController controller, bool canExecute)
        {
            if (controller == null) return;

            var runner = controller.AverageThresholdOptimizationRunner;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                string headerTitle = "🎯 Average 手法 T_ave 最適化スイープ (0.00〜1.00 グリッド探索)";
                if (runner != null && runner.lastBestMeanIoU > 0f)
                {
                    headerTitle += $" [最適値: T_ave={runner.lastBestTAve:F2} (IoU: {runner.lastBestMeanIoU:P1})]";
                }

                _sectionFolded = EditorGUILayout.Foldout(_sectionFolded, headerTitle, true, EditorStyles.foldoutHeader);

                if (_sectionFolded)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.HelpBox(
                        "Average 手法におけるオクルージョン判定閾値 T_ave を 0.00〜1.00 まで 0.01 刻み（計101点）でスイープし、\n" +
                        "全配置プリセットに対する平均 IoU が最大となる最適値 T_ave* を導出してパイプラインに自動適用します。\n\n" +
                        "※ 高速化のためスイープ途中の重い画像保存は行わず、IoU 推移 CSV と最適値における画像のみを保存します。",
                        MessageType.Info);

                    EditorGUILayout.Space(4);

                    // 1. 設定項目描画
                    SICESI_AverageThresholdOptimizationSettingsDrawer.DrawSettings();

                    EditorGUILayout.Space(6);

                    // 2. 実行アクション描画
                    SICESI_AverageThresholdOptimizationActionDrawer.DrawAction(controller, canExecute);

                    EditorGUILayout.Space(6);

                    // 3. 直近結果サマリーカード描画
                    if (runner != null && runner.lastBestMeanIoU > 0f)
                    {
                        SICESI_AverageThresholdOptimizationCardDrawer.DrawResultCard(controller, runner);
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }
    }
}
