using System;
using System.Collections.Generic;
using UnityEngine;

namespace SICESI
{
    /// <summary>
    /// 実験配置プリセット (JSON) の保存、一覧取得、シーンへの適用、および削除を統合管理する Facade クラス。
    /// 各処理の実装は以下の専門クラスに委譲されています:
    /// - ファイル保存・取得・削除: <see cref="SICESI_PresetRepository"/>
    /// - シーンへの復元適用: <see cref="SICESI_PresetSceneApplier"/>
    /// - カテゴリ自動判定: <see cref="SICESI_PresetCategoryDetector"/>
    /// </summary>
    public static class SICESI_PresetPoseManager
    {
        public enum PresetCategory
        {
            Generalization = 0, // 新規評価・未知検証用 (Test)
            Optimization = 1    // 最適化・ルール導出用 (Train)
        }

        [Serializable]
        public class PresetEntry
        {
            public string caseName;
            public string jsonPath;
            public string timestamp;
            public PresetCategory category;
            public string virtualObjectName;
            public string handMeshName;
            public Vector3 virtualObjectPosition;
            public Vector3 handMeshPosition;
            public bool isVoRegisteredInConfig;
            public bool isHandRegisteredInConfig;

            public string DisplaySummary =>
                $"[{caseName}] VO: {virtualObjectName} ({virtualObjectPosition.x:F2}, {virtualObjectPosition.y:F2}, {virtualObjectPosition.z:F2}) | Hand: {handMeshName}";
        }

        public static string GetPresetsDirectory(string rootOutputDirectory, PresetCategory category)
            => SICESI_PresetRepository.GetPresetsDirectory(rootOutputDirectory, category);

        public static PresetCategory DetectCurrentCategory(SICESI_StereoEvaluationController controller, out string reason)
            => SICESI_PresetCategoryDetector.DetectCurrentCategory(controller, out reason);

        public static List<PresetEntry> LoadPresetEntries(string rootOutputDirectory, PresetCategory? filterCategory = null)
            => SICESI_PresetRepository.LoadPresetEntries(rootOutputDirectory, filterCategory);

        public static bool SaveCurrentSceneAsPreset(SICESI_StereoEvaluationController controller, string caseName, PresetCategory category)
            => SICESI_PresetRepository.SaveCurrentSceneAsPreset(controller, caseName, category);

        public static bool ApplyPresetToScene(SICESI_StereoEvaluationController controller, string jsonPath)
            => SICESI_PresetSceneApplier.ApplyPresetToScene(controller, jsonPath);

        public static bool DeletePreset(string jsonPath)
            => SICESI_PresetRepository.DeletePreset(jsonPath);
    }
}
