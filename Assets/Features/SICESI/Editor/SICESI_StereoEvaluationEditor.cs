using System.IO;
using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// SICE SI 評価実験コントローラー専用のカスタム Inspector エディター (Facade)。
    /// タブ切り替え式により、各機能の描画責務は独立した Tab Drawer クラス
    /// (SICESI_TabSICESI2026Drawer, SICESI_TabAdditionalExperimentsDrawer,
    ///  SICESI_TabSweepsDrawer, SICESI_TabSettingsDrawer) に委譲されています。
    /// </summary>
    [CustomEditor(typeof(SICESI_StereoEvaluationController))]
    public class SICESI_StereoEvaluationEditor : UnityEditor.Editor
    {
        private const string SelectedTabPrefKey = "SICESI_SelectedTabIndex";

        private int _selectedTab = 0;
        private static readonly string[] TabTitles = new string[]
        {
            "🪐 SICESI 2026 実験",
            "🔬 追加実験 (Generalization)",
            "🚀 実験スイープ (Sweeps)",
            "⚙️ 詳細・レガシー設定 (Settings)"
        };

        // SerializedProperties
        private SerializedProperty _conditionNameProp;
        private SerializedProperty _outputDirectoryProp;
        private SerializedProperty _casesParentFolderProp;
        private SerializedProperty _datasetConfigProp;

        private void OnEnable()
        {
            _selectedTab = EditorPrefs.GetInt(SelectedTabPrefKey, 0);

            _conditionNameProp = serializedObject.FindProperty("conditionName");
            _outputDirectoryProp = serializedObject.FindProperty("outputDirectory");
            _casesParentFolderProp = serializedObject.FindProperty("casesParentFolder");
            _datasetConfigProp = serializedObject.FindProperty("datasetConfig");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var controller = (SICESI_StereoEvaluationController)target;
            var collector = controller.GetComponent<SICESI_SectorMaskCollector>();

            // 1. トップ概要バー（条件名 & 保存先パス表示）
            DrawTopConditionHeader(controller);

            // 2. タブ切り替えツールバー
            EditorGUILayout.Space(6);
            int newTab = GUILayout.Toolbar(_selectedTab, TabTitles, GUILayout.Height(30));
            if (newTab != _selectedTab)
            {
                _selectedTab = newTab;
                EditorPrefs.SetInt(SelectedTabPrefKey, _selectedTab);
            }
            EditorGUILayout.Space(8);

            bool isBusy = controller.isCapturing || (collector != null && collector.isCollecting);
            bool canExecute = Application.isPlaying && !isBusy;

            // 3. 各タブの内容描画 (独立 Drawer に委譲)
            switch (_selectedTab)
            {
                case 0:
                    SICESI_TabSICESI2026Drawer.Draw(controller, collector, canExecute);
                    break;
                case 1:
                    SICESI_TabAdditionalExperimentsDrawer.Draw(controller, _datasetConfigProp, canExecute);
                    break;
                case 2:
                    SICESI_TabSweepsDrawer.Draw(controller, canExecute);
                    break;
                case 3:
                    SICESI_TabSettingsDrawer.Draw(controller, collector, serializedObject, canExecute);
                    break;
            }

            // 4. フッター（ステータス・実行状態メッセージ）
            DrawFooterStatus(controller, collector);

            serializedObject.ApplyModifiedProperties();
        }

        #region トップヘッダー

        private void DrawTopConditionHeader(SICESI_StereoEvaluationController controller)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("🏷️ 現在の実験条件 & 保存先", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_conditionNameProp, new GUIContent("条件名 (Case名)"));
                EditorGUILayout.PropertyField(_casesParentFolderProp, new GUIContent("Case親フォルダ名"));
                EditorGUILayout.PropertyField(_outputDirectoryProp, new GUIContent("ルート出力ディレクトリ"));

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("実出力先:", controller.ConditionRootDir, EditorStyles.wordWrappedMiniLabel);

                if (GUILayout.Button("📂 出力先フォルダをエクスプローラーで開く", GUILayout.Height(22)))
                {
                    if (Directory.Exists(controller.ConditionRootDir))
                    {
                        EditorUtility.RevealInFinder(controller.ConditionRootDir);
                    }
                    else if (Directory.Exists(controller.outputDirectory))
                    {
                        EditorUtility.RevealInFinder(controller.outputDirectory);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("情報", $"フォルダがまだ存在しません:\n{controller.ConditionRootDir}", "OK");
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region フッター

        private void DrawFooterStatus(SICESI_StereoEvaluationController controller, SICESI_SectorMaskCollector collector)
        {
            EditorGUILayout.Space(8);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("※ 撮影・スイープ機能は Unity の Play モード中に実行してください。", MessageType.Info);
            }

            if (controller.isCapturing)
            {
                EditorGUILayout.HelpBox($"進行中: {controller.statusMessage}", MessageType.Warning);
                Repaint();
            }
            else if (collector != null && collector.isCollecting)
            {
                EditorGUILayout.HelpBox($"SectorMask 収集進行中: {collector.statusMessage}", MessageType.Warning);
                Repaint();
            }
        }

        #endregion
    }
}
