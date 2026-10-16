using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// Average 手法 T_ave 最適化スイープの設定項目（範囲、刻み幅、対象プリセット）を描画・管理する Drawer クラス。
    /// </summary>
    public static class SICESI_AverageThresholdOptimizationSettingsDrawer
    {
        public static int TargetPresetCategory { get; set; } = 0; // 0: 最適化用, 1: 検証用, 2: すべて, 3: 選択中のみ
        public static float StartT { get; set; } = 0.0f;
        public static float EndT { get; set; } = 1.0f;
        public static float StepT { get; set; } = 0.01f;
        public static bool AutoApply { get; set; } = true;

        private static readonly string[] CategoryOptions = new string[]
        {
            "🪐 最適化用プリセット",
            "🔬 検証用プリセット",
            "📁 すべてのプリセット",
            "☑️ 選択中のみ"
        };

        /// <summary>
        /// スイープパラメータ設定 UI を描画します。
        /// </summary>
        public static void DrawSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField("⚙️ スイープ設定", EditorStyles.boldLabel);

                TargetPresetCategory = EditorGUILayout.Popup("対象プリセット群", TargetPresetCategory, CategoryOptions);

                EditorGUILayout.BeginHorizontal();
                {
                    StartT = EditorGUILayout.FloatField("開始値", StartT);
                    EndT = EditorGUILayout.FloatField("終了値", EndT);
                    StepT = EditorGUILayout.FloatField("刻み幅", StepT);
                }
                EditorGUILayout.EndHorizontal();

                StartT = Mathf.Clamp(StartT, 0f, 1f);
                EndT = Mathf.Clamp(EndT, StartT, 1f);
                StepT = Mathf.Max(0.001f, StepT);

                int totalSteps = GetTotalSteps();
                EditorGUILayout.LabelField($"  探索ステップ数: {totalSteps} 点 ({StartT:F2} .. {EndT:F2})", EditorStyles.miniLabel);

                AutoApply = EditorGUILayout.Toggle("最適値をパイプラインに自動適用", AutoApply);
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 現在の設定に基づく探索ステップ総数を取得します。
        /// </summary>
        public static int GetTotalSteps()
        {
            return Mathf.RoundToInt((EndT - StartT) / StepT) + 1;
        }

        /// <summary>
        /// 選択されたカテゴリに応じたプリセットリストを取得します。
        /// </summary>
        public static List<SICESI_PresetPoseManager.PresetEntry> GetTargetPresets(SICESI_StereoEvaluationController controller)
        {
            if (controller == null) return new List<SICESI_PresetPoseManager.PresetEntry>();

            var all = SICESI_PresetPoseManager.LoadPresetEntries(controller.outputDirectory);
            if (TargetPresetCategory == 0) // 最適化用
            {
                return all.FindAll(p => p.category == SICESI_PresetPoseManager.PresetCategory.Optimization);
            }
            else if (TargetPresetCategory == 1) // 検証用
            {
                return all.FindAll(p => p.category == SICESI_PresetPoseManager.PresetCategory.Generalization);
            }
            else if (TargetPresetCategory == 3) // 選択中のみ
            {
                return all.FindAll(p => SICESI_PresetSelectionManager.IsSelected(p.jsonPath));
            }
            return all;
        }
    }
}
