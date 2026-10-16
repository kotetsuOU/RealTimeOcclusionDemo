using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// SICE SI 追加実験における「Python 最適化のバックグラウンド実行および PCDOcclusionPipelineController への自動パラメータ置換」
    /// セクションを描画する Facade クラス。
    /// 設定 (<see cref="SICESI_OptimizationSettingsDrawer"/>)、
    /// 実行アクション (<see cref="SICESI_OptimizationActionDrawer"/>)、
    /// パラメータ結果表示 (<see cref="SICESI_OptimizationParametersCardDrawer"/>) を統合して描画します。
    /// </summary>
    public static class SICESI_OptimizationSectionDrawer
    {
        private static bool _sectionFolded = true;

        /// <summary>
        /// 最適化 & 自動置換セクションを描画します。
        /// </summary>
        public static void Draw(SICESI_StereoEvaluationController controller, bool canExecute)
        {
            if (controller == null) return;

            var runner = controller.PythonOptimizationRunner;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                // ヘッダー Foldout
                string foldoutTitle = "🪐 Python 最適化の実行 & パイプライン自動置換 (256パターンLUT / 36クラス / 20候補 / 8候補 最適化)";
                _sectionFolded = EditorGUILayout.Foldout(_sectionFolded, foldoutTitle, true, EditorStyles.foldoutHeader);

                if (_sectionFolded)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.HelpBox(
                        "撮影済みマスクデータセット (Sector8MaskSweep) から真値集計 (pattern_VO.csv) と数理最適化 (MILP / 局所探索) をバックグラウンド実行し、\n" +
                        "算出された 256パターンLUT 等の最適パラメータ (Mode 8, 20, 36, 256) を PCDOcclusionPipelineController およびベンチマーク設定へ自動置換します。",
                        MessageType.Info);

                    EditorGUILayout.Space(4);

                    // 1. 実行設定セクション (委譲)
                    SICESI_OptimizationSettingsDrawer.Draw(controller);

                    EditorGUILayout.Space(6);

                    // 2. 実行アクションセクション (進捗バーまたは実行ボタン) (委譲)
                    SICESI_OptimizationActionDrawer.Draw(controller, runner, canExecute);

                    EditorGUILayout.Space(6);

                    // 3. 現在の最適化結果パラメータカード (委譲)
                    SICESI_OptimizationParametersCardDrawer.Draw(controller, runner);
                }
            }
            EditorGUILayout.EndVertical();
        }
    }
}
