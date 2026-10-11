using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// SICE SI 追加実験タブ（汎化性能ベンチマーク・配置プリセット JSON の保存と一覧管理、および一括IoU計測）の統合 Drawer (Facade)。
    /// 実際の描画責務は独立した Drawer クラスに委譲されています:
    /// - プリセット保存: <see cref="SICESI_PresetSaveSectionDrawer"/>
    /// - プリセット一覧・実行: <see cref="SICESI_PresetListSectionDrawer"/>
    /// </summary>
    public static class SICESI_TabAdditionalExperimentsDrawer
    {
        private static bool _datasetConfigFolded = true;

        /// <summary>
        /// 追加実験タブの全体描画を行います。
        /// </summary>
        public static void Draw(SICESI_StereoEvaluationController controller, SerializedProperty datasetConfigProp, bool canExecute)
        {
            if (controller == null) return;

            // 概要ヘッダー
            EditorGUILayout.LabelField("🔬 追加実験: 未知モデル・手の新規配置に対する汎化性能ベンチマーク", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "「最適化用 (既知3種)」と「検証用 (未知新規モデル)」を明確に区別して配置プリセット (JSON) を保存・管理し、\n" +
                "5手法 (Average, Mode 8, 20, 36, 256) を一括適用して最終出力 Ground Truth (GT) とのピクセル単位 IoU を自動計測・CSV 出力します。", MessageType.Info);

            EditorGUILayout.Space(6);

            // 1. データセット設定 (登録リスト Foldout)
            DrawDatasetConfigSection(datasetConfigProp, controller);

            EditorGUILayout.Space(8);

            // 2. 現在の配置を JSON プリセットとして保存するセクション (委譲)
            SICESI_PresetSaveSectionDrawer.Draw(controller);

            EditorGUILayout.Space(8);

            // 3. 保存済み JSON プリセット一覧 & 一括ベンチマーク実行 (委譲)
            SICESI_PresetListSectionDrawer.Draw(controller, canExecute);
        }

        #region 1. データセット設定

        private static void DrawDatasetConfigSection(SerializedProperty datasetConfigProp, SICESI_StereoEvaluationController controller)
        {
            if (datasetConfigProp == null || controller == null) return;

            var config = controller.datasetConfig;
            int optVoCount = config != null && config.optimizationVirtualObjects != null ? config.optimizationVirtualObjects.Count : 0;
            int evalVoCount = config != null && config.evaluationVirtualObjects != null ? config.evaluationVirtualObjects.Count : 0;
            int optHandCount = config != null && config.optimizationHandMeshes != null ? config.optimizationHandMeshes.Count : 0;
            int evalHandCount = config != null && config.evaluationHandMeshes != null ? config.evaluationHandMeshes.Count : 0;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                string foldoutTitle = $"📋 仮想オブジェクト & 手メッシュの登録リスト (最適化: VO {optVoCount}/Hand {optHandCount} | 検証: VO {evalVoCount}/Hand {evalHandCount})";
                _datasetConfigFolded = EditorGUILayout.Foldout(_datasetConfigFolded, foldoutTitle, true, EditorStyles.foldoutHeader);

                if (_datasetConfigFolded)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("【🪐 最適化・ルール導出用 (Optimization / Train)】", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(
                        datasetConfigProp.FindPropertyRelative("optimizationVirtualObjects"),
                        new GUIContent("最適化用 仮想オブジェクト (例: Fox1, happy, elephal 3種)"), true);
                    EditorGUILayout.PropertyField(
                        datasetConfigProp.FindPropertyRelative("optimizationHandMeshes"),
                        new GUIContent("最適化用 手メッシュ / GT リスト"), true);

                    EditorGUILayout.Space(6);
                    EditorGUILayout.LabelField("【🔬 新規評価・未知検証用 (Generalization / Test)】", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(
                        datasetConfigProp.FindPropertyRelative("evaluationVirtualObjects"),
                        new GUIContent("新規検証用 仮想オブジェクト (未知モデル群)"), true);
                    EditorGUILayout.PropertyField(
                        datasetConfigProp.FindPropertyRelative("evaluationHandMeshes"),
                        new GUIContent("新規検証用 手メッシュ / 新規GTリスト"), true);
                }
            }
            EditorGUILayout.EndVertical();
        }

        #endregion
    }
}
