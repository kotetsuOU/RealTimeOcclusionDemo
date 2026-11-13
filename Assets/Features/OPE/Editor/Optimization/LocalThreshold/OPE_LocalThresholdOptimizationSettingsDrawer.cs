using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OPE.Editor
{
    /// <summary>
    /// 8セクター二値化閾値 T_loc 最適化スイープの設定項目（範囲、刻み幅、対象プリセット、ノイズ設定）を描画・管理する Drawer クラス。
    /// </summary>
    public static class OPE_LocalThresholdOptimizationSettingsDrawer
    {
        public static int TargetPresetCategory { get; set; } = 0; // 0: 最適化用, 1: 検証用, 2: すべて, 3: 選択中のみ
        public static float StartT { get; set; } = 0.0f;
        public static float EndT { get; set; } = 1.0f;
        public static float StepT { get; set; } = 0.01f;
        public static bool AutoApply { get; set; } = true;
        public static bool EnsureNoiseEnabled { get; set; } = true;
        public static float OverrideNoiseMm { get; set; } = 2.0f;

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

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("🎲 評価点群ノイズ設定 (ノイズがないと T_loc=1.00 が自明になるため必須)", EditorStyles.miniBoldLabel);
                EnsureNoiseEnabled = EditorGUILayout.Toggle("ノイズを強制有効化", EnsureNoiseEnabled);
                if (EnsureNoiseEnabled)
                {
                    OverrideNoiseMm = EditorGUILayout.Slider("法線方向ノイズ量 (mm)", OverrideNoiseMm, 0.5f, 10.0f);
                }

                EditorGUILayout.Space(2);
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
        public static List<OPE_PresetPoseManager.PresetEntry> GetTargetPresets(OPE_StereoEvaluationController controller)
        {
            if (controller == null) return new List<OPE_PresetPoseManager.PresetEntry>();

            var all = OPE_PresetPoseManager.LoadPresetEntries(controller.outputDirectory);
            if (TargetPresetCategory == 0) // 最適化用
            {
                return all.FindAll(p => p.category == OPE_PresetPoseManager.PresetCategory.Optimization);
            }
            else if (TargetPresetCategory == 1) // 検証用
            {
                return all.FindAll(p => p.category == OPE_PresetPoseManager.PresetCategory.Generalization);
            }
            else if (TargetPresetCategory == 3) // 選択中のみ
            {
                return all.FindAll(p => OPE_PresetSelectionManager.IsSelected(p.jsonPath));
            }
            return all;
        }
    }
}

