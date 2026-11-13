using UnityEditor;
using UnityEngine;

namespace OPE.Editor
{
    /// <summary>
    /// SICE SI 追加実験タブにおいて、8セクター二値化閾値 T_loc の
    /// 0.00〜1.00 グリッド最適化スイープ（深度ピラミッドGT比較）セクション全体を統括する Facade Drawer クラス。
    /// </summary>
    public static class OPE_LocalThresholdOptimizationDrawer
    {
        private static bool _sectionFolded = false;

        /// <summary>
        /// T_loc 最適化セクションを描画します。
        /// </summary>
        public static void Draw(OPE_StereoEvaluationController controller, bool canExecute)
        {
            if (controller == null) return;

            var runner = controller.LocalThresholdOptimizationRunner;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                string headerTitle = "🎯 8セクター二値化閾値 T_loc 最適化スイープ (深度ピラミッドGT探索)";
                if (runner != null && runner.lastBestMeanIoU > 0f)
                {
                    headerTitle += $" [最適値: T_loc={runner.lastBestTLoc:F2} (8Dir-IoU: {runner.lastBestMeanIoU:P1})]";
                }

                _sectionFolded = EditorGUILayout.Foldout(_sectionFolded, headerTitle, true, EditorStyles.foldoutHeader);

                if (_sectionFolded)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.HelpBox(
                        "手メッシュから構築された「全ピクセル埋まった理想深度ピラミッド」において各セクター内に手前点が存在するかを8方向正解(GT)とし、\n" +
                        "位置ノイズありの評価用点群で T_loc (0.00〜1.00) をスイープしながら 8方向白黒マスクの IoU 一致度を最大化する最適値 T_loc* を探索します。\n\n" +
                        "※ ノイズによる誤判定 (FP) を排除しつつ本来の手前点 (TP) を残す最適な局所評価閾値が求まります。\n" +
                        "※ 高速化のためスイープ途中の画像保存は行わず、IoU 推移 CSV と最適値における画像のみを保存します。",
                        MessageType.Info);

                    EditorGUILayout.Space(4);

                    // 1. 設定項目描画
                    OPE_LocalThresholdOptimizationSettingsDrawer.DrawSettings();

                    EditorGUILayout.Space(6);

                    // 2. 実行アクション描画
                    OPE_LocalThresholdOptimizationActionDrawer.DrawAction(controller, canExecute);

                    EditorGUILayout.Space(6);

                    // 3. 直近結果サマリーカード描画
                    if (runner != null && runner.lastBestMeanIoU > 0f)
                    {
                        OPE_LocalThresholdOptimizationCardDrawer.DrawResultCard(controller, runner);
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }
    }
}
