using System.IO;
using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// 現在有効な最適化パラメータ（Mode 8, 20, 36, 256 の詳細・IoU・HEX短縮）
    /// のサマリーカード描画およびパイプライン再適用アクションを担当する Drawer クラス。
    /// </summary>
    public static class SICESI_OptimizationParametersCardDrawer
    {
        /// <summary>
        /// 現在の最適化パラメータカードを描画します。
        /// </summary>
        public static void Draw(
            SICESI_StereoEvaluationController controller,
            SICESI_PythonOptimizationRunner runner)
        {
            if (controller == null) return;

            var activeRes = SICESI_BenchmarkMethodRegistry.ActiveOptimizedParameters ?? (runner != null ? runner.lastResult : null);
            if (activeRes == null)
            {
                // まだ未適用の場合は、既存の JSON があればプレビュー用として読み込みを試みる
                string targetDataset = SICESI_OptimizationSettingsDrawer.GetEffectiveDatasetPath(controller);
                string jsonPath = Path.Combine(targetDataset, "RuleOptimizationResults", "optimized_parameters.json");
                var objective = SICESI_OptimizationSettingsDrawer.Objective;

                if (File.Exists(jsonPath) && SICESI_OptimizationResult.TryLoadFromJson(jsonPath, objective, out var previewRes))
                {
                    activeRes = previewRes;
                }
            }

            if (activeRes == null)
            {
                EditorGUILayout.HelpBox("💡 最適化結果はまだ読み込まれていません。「Python 最適化を実行」または「既存結果を再読み込み」を押してください。", MessageType.None);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.textArea);
            {
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField($"<b>📊 有効な最適化パラメータ ({activeRes.objective})</b>", new GUIStyle(EditorStyles.label) { richText = true });
                    if (GUILayout.Button("🔄 パイプラインに再適用", GUILayout.Width(130)))
                    {
                        var targetMode = SICESI_OptimizationSettingsDrawer.TargetApplyMode;
                        SICESI_OptimizationApplier.ApplyAll(controller.occlusionPipelineController, activeRes, targetMode);
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2);

                // 各モードの詳細表示
                EditorGUILayout.LabelField($"• <b>Mode 8 (占有数閾値)</b>: {activeRes.mode8RuleDesc} (最小占有セクター数 R_th = <b>{activeRes.mode8ThresholdR}</b>, 平均IoU: {activeRes.mode8MeanIoU:P2})", new GUIStyle(EditorStyles.miniLabel) { richText = true });
                EditorGUILayout.LabelField($"• <b>Mode 20 (連続非占有)</b>: {activeRes.mode20RuleDesc} (R_th = <b>{activeRes.mode20ThresholdR}</b>, L_th = <b>{activeRes.mode20MaxConsecutiveEmptyL}</b>, 平均IoU: {activeRes.mode20MeanIoU:P2})", new GUIStyle(EditorStyles.miniLabel) { richText = true });

                string hex36Short = !string.IsNullOrEmpty(activeRes.mode36Hex) ? activeRes.mode36Hex.Substring(0, Mathf.Min(16, activeRes.mode36Hex.Length)) + "..." : "(none)";
                EditorGUILayout.LabelField($"• <b>Mode 36 (36クラスLUT)</b>: 可視 {activeRes.mode36VisibleClasses}/36 クラス (HEX: {hex36Short}, 平均IoU: {activeRes.mode36MeanIoU:P2})", new GUIStyle(EditorStyles.miniLabel) { richText = true });

                string hex256Short = !string.IsNullOrEmpty(activeRes.mode256Hex) ? activeRes.mode256Hex.Substring(0, Mathf.Min(16, activeRes.mode256Hex.Length)) + "..." : "(none)";
                EditorGUILayout.LabelField($"• <b>Mode 256 (256パターンLUT)</b>: 可視 {activeRes.mode256VisiblePatterns}/256 パターン (HEX: {hex256Short}, 平均IoU: {activeRes.mode256MeanIoU:P2})", new GUIStyle(EditorStyles.miniLabel) { richText = true });

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField($"<color=#777777>ソース: {Path.GetFileName(activeRes.sourceFilePath)} | 更新日時: {activeRes.timestamp}</color>", new GUIStyle(EditorStyles.miniLabel) { richText = true });
            }
            EditorGUILayout.EndVertical();
        }
    }
}
