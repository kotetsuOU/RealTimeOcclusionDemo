using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// SICE SI 配置プリセット (JSON) のファイル保存、一覧スキャン・読み込み、および削除を担当するリポジトリクラス。
    /// </summary>
    public static class SICESI_PresetRepository
    {
        /// <summary>
        /// プリセット保存先のルートフォルダを取得します。
        /// </summary>
        public static string GetPresetsDirectory(string rootOutputDirectory, SICESI_PresetPoseManager.PresetCategory category)
        {
            string categorySubFolder = category == SICESI_PresetPoseManager.PresetCategory.Generalization ? "Generalization" : "Optimization";
            string dir = Path.Combine(rootOutputDirectory, "AdditionalExperiments", "Presets", categorySubFolder);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        /// <summary>
        /// 保存先フォルダ内の全プリセット JSON をスキャンし、カテゴリ別にリストとして取得します。
        /// </summary>
        public static List<SICESI_PresetPoseManager.PresetEntry> LoadPresetEntries(
            string rootOutputDirectory,
            SICESI_PresetPoseManager.PresetCategory? filterCategory = null)
        {
            var list = new List<SICESI_PresetPoseManager.PresetEntry>();

            var categories = filterCategory.HasValue
                ? new SICESI_PresetPoseManager.PresetCategory[] { filterCategory.Value }
                : new SICESI_PresetPoseManager.PresetCategory[] { SICESI_PresetPoseManager.PresetCategory.Generalization, SICESI_PresetPoseManager.PresetCategory.Optimization };

            foreach (var cat in categories)
            {
                string catDir = GetPresetsDirectory(rootOutputDirectory, cat);
                if (!Directory.Exists(catDir)) continue;

                var subDirs = Directory.GetDirectories(catDir);
                foreach (var dir in subDirs)
                {
                    string jsonPath = Path.Combine(dir, "scene_transforms.json");
                    if (!File.Exists(jsonPath)) continue;

                    try
                    {
                        string json = File.ReadAllText(jsonPath);
                        var data = JsonUtility.FromJson<SICESI_SceneMetadataSerializer.SceneTransformsData>(json);
                        if (data == null) continue;

                        var entry = new SICESI_PresetPoseManager.PresetEntry
                        {
                            caseName = !string.IsNullOrEmpty(data.conditionName) ? data.conditionName : Path.GetFileName(dir),
                            jsonPath = jsonPath,
                            timestamp = data.timestamp,
                            category = cat,
                            virtualObjectName = data.virtualObject != null ? data.virtualObject.name : "None",
                            handMeshName = data.handMesh != null ? data.handMesh.name : "None",
                            virtualObjectPosition = data.virtualObject != null ? data.virtualObject.position : Vector3.zero,
                            handMeshPosition = data.handMesh != null ? data.handMesh.position : Vector3.zero
                        };
                        list.Add(entry);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[SICESI_PresetRepository] JSON パース失敗 ({jsonPath}): {ex.Message}");
                    }
                }
            }

            // 自然順ソート
            list.Sort((a, b) =>
            {
                if (int.TryParse(a.caseName.Replace("Case_", "").Replace("Case", ""), out int intA) &&
                    int.TryParse(b.caseName.Replace("Case_", "").Replace("Case", ""), out int intB))
                {
                    return intA.CompareTo(intB);
                }
                return string.Compare(a.caseName, b.caseName, StringComparison.OrdinalIgnoreCase);
            });

            return list;
        }

        /// <summary>
        /// 現在のシーンの配置（手・仮想物体・カメラ）を指定されたカテゴリおよび Case 名でプリセット保存します。
        /// </summary>
        public static bool SaveCurrentSceneAsPreset(
            SICESI_StereoEvaluationController controller,
            string caseName,
            SICESI_PresetPoseManager.PresetCategory category)
        {
            if (controller == null) return false;
            if (string.IsNullOrEmpty(caseName)) caseName = "Case_1";

            // 保存直前に現在アクティブな VO & Hand を確実に同期
            if (controller.datasetConfig != null)
            {
                controller.datasetConfig.SyncActiveObjectsWithController(controller);
            }

            string presetsDir = GetPresetsDirectory(controller.outputDirectory, category);
            string caseDir = Path.Combine(presetsDir, caseName);
            Directory.CreateDirectory(caseDir);

            SICESI_SceneMetadataSerializer.SaveSceneTransformsJson(
                caseDir,
                caseName,
                controller.groundTruthObject,
                controller.virtualObject,
                controller.leftEyeCamera,
                controller.rightEyeCamera,
                controller.sceneCaptureCamera
            );

            string catLabel = category == SICESI_PresetPoseManager.PresetCategory.Generalization ? "検証用" : "最適化用";
            AppLogger.Log(controller, $"[SICESI_PresetRepository] {catLabel}配置プリセットを保存しました: [{caseName}] -> {caseDir}", SICESI_StereoEvaluationController.TagCore);
            return true;
        }

        /// <summary>
        /// 指定したプリセットフォルダを削除します。
        /// </summary>
        public static bool DeletePreset(string jsonPath)
        {
            try
            {
                string caseDir = Path.GetDirectoryName(jsonPath);
                if (Directory.Exists(caseDir))
                {
                    Directory.Delete(caseDir, true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SICESI_PresetRepository] プリセット削除失敗: {ex.Message}");
            }
            return false;
        }
    }
}
