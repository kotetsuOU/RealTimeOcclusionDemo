using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// Python 最適化の設定（Python実行パス、データセットパス、クイック切替、目的関数、詳細オプション）
    /// の入力および状態保持を担当する Drawer クラス。
    /// </summary>
    public static class SICESI_OptimizationSettingsDrawer
    {
        private const string PrefKeyPythonPath = "SICESI_PythonPath";

        private static string _pythonPath = "python";
        private static string _customDatasetPath = "";
        private static bool _advancedFolded = false;
        private static bool _skipDiffMaps = true;
        private static bool _forceRegenerate = false;
        private static bool _autoApply = true;
        private static SICESI_OptimizationResult.ObjectiveType _objective = SICESI_OptimizationResult.ObjectiveType.MeanIoUMaximized;
        private static PCDRendererFeature.PCD_OcclusionEvaluationMode _targetApplyMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.PatternLUT256;

        public static string PythonPath => _pythonPath;
        public static string CustomDatasetPath => _customDatasetPath;
        public static SICESI_OptimizationResult.ObjectiveType Objective => _objective;
        public static bool SkipDiffMaps => _skipDiffMaps;
        public static bool ForceRegenerate => _forceRegenerate;
        public static bool AutoApply => _autoApply;
        public static PCDRendererFeature.PCD_OcclusionEvaluationMode TargetApplyMode => _targetApplyMode;

        static SICESI_OptimizationSettingsDrawer()
        {
            _pythonPath = EditorPrefs.GetString(PrefKeyPythonPath, "python");
        }

        /// <summary>
        /// 有効なデータセットルートパスを取得します（RawTest または指定パス、フォールバック）。
        /// </summary>
        public static string GetEffectiveDatasetPath(SICESI_StereoEvaluationController controller)
        {
            if (controller == null) return _customDatasetPath;

            string rawTestCandidate = (!string.IsNullOrEmpty(controller.outputDirectory) && !string.IsNullOrEmpty(controller.casesParentFolder))
                ? Path.Combine(controller.outputDirectory, controller.casesParentFolder)
                : "";

            string defaultDataset = (!string.IsNullOrEmpty(rawTestCandidate) && Directory.Exists(rawTestCandidate))
                ? rawTestCandidate
                : (!string.IsNullOrEmpty(controller.outputDirectory)
                    ? controller.outputDirectory
                    : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Estimation", "SICESI_Dataset")));

            return !string.IsNullOrEmpty(_customDatasetPath) ? _customDatasetPath : defaultDataset;
        }

        /// <summary>
        /// 解析結果出力先 (Optimization/RuleOptimization) の絶対パスを取得します。
        /// 既存の RuleOptimizationResults が存在する場合はフォールバック互換性を提供します。
        /// </summary>
        public static string GetEffectiveOutputDir(SICESI_StereoEvaluationController controller)
        {
            string basePath = GetEffectiveDatasetPath(controller);
            string newPath = Path.Combine(basePath, "Optimization", "RuleOptimization");
            string legacyPath = Path.Combine(basePath, "RuleOptimizationResults");
            if (!Directory.Exists(newPath) && Directory.Exists(legacyPath))
            {
                return legacyPath;
            }
            return newPath;
        }

        /// <summary>
        /// 設定セクションを描画します。
        /// </summary>
        public static void Draw(SICESI_StereoEvaluationController controller)
        {
            if (controller == null) return;

            // Python パス設定
            EditorGUILayout.BeginHorizontal();
            {
                string newPy = EditorGUILayout.TextField(new GUIContent("Python 実行パス", "python または python.exe の絶対パス"), _pythonPath);
                if (newPy != _pythonPath)
                {
                    _pythonPath = newPy;
                    EditorPrefs.SetString(PrefKeyPythonPath, _pythonPath);
                }

                if (GUILayout.Button("リセット", GUILayout.Width(60)))
                {
                    _pythonPath = "python";
                    EditorPrefs.SetString(PrefKeyPythonPath, _pythonPath);
                }
            }
            EditorGUILayout.EndHorizontal();

            // スマート初期値の解決
            string effectiveDataset = GetEffectiveDatasetPath(controller);
            if (string.IsNullOrEmpty(_customDatasetPath))
            {
                _customDatasetPath = effectiveDataset;
            }

            // 対象データセットパス入力
            EditorGUILayout.BeginHorizontal();
            {
                _customDatasetPath = EditorGUILayout.TextField(new GUIContent("対象データセットパス", "撮影画像群または pattern_counts_256.csv が格納されたルートディレクトリ"), _customDatasetPath);

                if (GUILayout.Button("参照...", GUILayout.Width(55)))
                {
                    string selected = EditorUtility.OpenFolderPanel("データセットフォルダを選択", _customDatasetPath, "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        _customDatasetPath = selected;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // クイック切り替えショートカットボタン行
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.PrefixLabel("クイック切替");

                // 1. RawTest 候補
                if (!string.IsNullOrEmpty(controller.casesParentFolder))
                {
                    string caseDirPath = Path.Combine(controller.outputDirectory, controller.casesParentFolder);
                    if (GUILayout.Button($"📁 {controller.casesParentFolder} (通常)", EditorStyles.miniButton))
                    {
                        _customDatasetPath = caseDirPath;
                    }
                }

                // 2. プリセット最適化フォルダ候補
                string presetOptDir = Path.Combine(controller.outputDirectory, "AdditionalExperiments", "Presets", "Optimization");
                if (GUILayout.Button("🪐 Presets/Optimization", EditorStyles.miniButton))
                {
                    _customDatasetPath = presetOptDir;
                }

                // 3. データセット全体ルート
                if (GUILayout.Button("🏠 SICESI_Dataset (全体ルート)", EditorStyles.miniButton))
                {
                    _customDatasetPath = controller.outputDirectory;
                }
            }
            EditorGUILayout.EndHorizontal();

            // 確定した出力先フォルダの明示表示 & 開くボタン
            string targetOutputDir = GetEffectiveOutputDir(controller);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            {
                GUI.color = new Color(0.3f, 1.0f, 0.4f);
                EditorGUILayout.LabelField("📁 解析結果出力先:", GUILayout.Width(110));
                GUI.color = Color.white;

                EditorGUILayout.SelectableLabel(targetOutputDir, EditorStyles.miniLabel, GUILayout.Height(18));

                if (GUILayout.Button("📂 出力先を開く", GUILayout.Width(100), GUILayout.Height(20)))
                {
                    if (!Directory.Exists(targetOutputDir))
                    {
                        Directory.CreateDirectory(targetOutputDir);
                    }
                    EditorUtility.RevealInFinder(targetOutputDir);
                }
            }
            EditorGUILayout.EndHorizontal();

            // 目的関数
            _objective = (SICESI_OptimizationResult.ObjectiveType)EditorGUILayout.EnumPopup(
                new GUIContent("最適化目的関数", "MeanIoUMaximized (目的関数A: 推奨) または RelativeReductionMaximized (目的関数B)"), _objective);

            // 詳細トグル (折りたたみ)
            _advancedFolded = EditorGUILayout.Foldout(_advancedFolded, "⚙️ 詳細オプション", false);
            if (_advancedFolded)
            {
                EditorGUI.indentLevel++;
                _skipDiffMaps = EditorGUILayout.Toggle(new GUIContent("diff_map 生成をスキップ (高速実行)", "diff_map 画像出力を省略して最適化計算とCSV/JSON出力のみを高速に実行します"), _skipDiffMaps);
                _forceRegenerate = EditorGUILayout.Toggle(new GUIContent("強制再集計 (--force)", "既存キャッシュを無視して撮影画像から強制再集計します"), _forceRegenerate);
                _autoApply = EditorGUILayout.Toggle(new GUIContent("完了時にパイプラインへ自動反映", "最適化完了時に自動で PCDOcclusionPipelineController に数値を上書きします"), _autoApply);
                _targetApplyMode = (PCDRendererFeature.PCD_OcclusionEvaluationMode)EditorGUILayout.EnumPopup(
                    new GUIContent("適用時アクティブモード", "パイプラインに適用する際、評価モードを何に切り替えるか"), _targetApplyMode);
                EditorGUI.indentLevel--;
            }
        }
    }
}
