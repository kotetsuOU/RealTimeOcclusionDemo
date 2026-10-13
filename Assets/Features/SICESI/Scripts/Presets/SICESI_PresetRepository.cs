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
        /// <summary>
        /// 保存先フォルダ内の全プリセット JSON をサブフォルダー含めてスキャンし、カテゴリ・フォルダー別にリストとして取得します。
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

                // サブフォルダー階層を含めて再帰検索
                string[] jsonFiles;
                try
                {
                    jsonFiles = Directory.GetFiles(catDir, "scene_transforms.json", SearchOption.AllDirectories);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SICESI_PresetRepository] プリセット走査失敗 ({catDir}): {ex.Message}");
                    continue;
                }

                foreach (var jsonPath in jsonFiles)
                {
                    try
                    {
                        string caseDir = Path.GetDirectoryName(jsonPath);
                        string relPath = Path.GetRelativePath(catDir, caseDir);
                        string parentFolder = Path.GetDirectoryName(relPath);
                        string folderName = string.IsNullOrEmpty(parentFolder) ? "(ルート)" : parentFolder.Replace('\\', '/');

                        string json = File.ReadAllText(jsonPath);
                        var data = JsonUtility.FromJson<SICESI_SceneMetadataSerializer.SceneTransformsData>(json);
                        if (data == null) continue;

                        var entry = new SICESI_PresetPoseManager.PresetEntry
                        {
                            caseName = !string.IsNullOrEmpty(data.conditionName) ? data.conditionName : Path.GetFileName(caseDir),
                            folderName = folderName,
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

            // 自然順ソート (フォルダ名 -> Case名)
            list.Sort((a, b) =>
            {
                if (a.folderName != b.folderName)
                {
                    if (a.folderName == "(ルート)") return -1;
                    if (b.folderName == "(ルート)") return 1;
                    int fCmp = string.Compare(a.folderName, b.folderName, StringComparison.OrdinalIgnoreCase);
                    if (fCmp != 0) return fCmp;
                }

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
        /// 保存先ディレクトリ内に存在する既存サブフォルダー名一覧を取得します（先頭は "(ルート)"）。
        /// </summary>
        public static List<string> GetExistingFolders(string rootOutputDirectory, SICESI_PresetPoseManager.PresetCategory category)
        {
            var folders = new HashSet<string> { "(ルート)" };
            string catDir = GetPresetsDirectory(rootOutputDirectory, category);
            if (Directory.Exists(catDir))
            {
                try
                {
                    string[] jsonFiles = Directory.GetFiles(catDir, "scene_transforms.json", SearchOption.AllDirectories);
                    foreach (var jf in jsonFiles)
                    {
                        string caseDir = Path.GetDirectoryName(jf);
                        string rel = Path.GetRelativePath(catDir, caseDir);
                        string parent = Path.GetDirectoryName(rel);
                        if (!string.IsNullOrEmpty(parent))
                        {
                            folders.Add(parent.Replace('\\', '/'));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SICESI_PresetRepository] フォルダ一覧取得失敗: {ex.Message}");
                }
            }
            var list = new List<string>(folders);
            list.Sort((a, b) =>
            {
                if (a == "(ルート)") return -1;
                if (b == "(ルート)") return 1;
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        /// <summary>
        /// 現在のシーンの配置（手・仮想物体・カメラ）を指定されたカテゴリ、サブフォルダー、および Case 名でプリセット保存します。
        /// </summary>
        public static bool SaveCurrentSceneAsPreset(
            SICESI_StereoEvaluationController controller,
            string caseName,
            SICESI_PresetPoseManager.PresetCategory category,
            string subFolder = "")
        {
            if (controller == null) return false;
            if (string.IsNullOrEmpty(caseName)) caseName = "Case_1";

            // 保存直前に現在アクティブな VO & Hand を確実に同期
            if (controller.datasetConfig != null)
            {
                controller.datasetConfig.SyncActiveObjectsWithController(controller);
            }

            string presetsDir = GetPresetsDirectory(controller.outputDirectory, category);
            string targetDir = presetsDir;
            if (!string.IsNullOrEmpty(subFolder) && subFolder != "(ルート)")
            {
                targetDir = Path.Combine(presetsDir, subFolder);
            }
            string caseDir = Path.Combine(targetDir, caseName);
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
            string folderLabel = (!string.IsNullOrEmpty(subFolder) && subFolder != "(ルート)") ? $"/{subFolder}" : "";
            AppLogger.Log(controller, $"[SICESI_PresetRepository] {catLabel}配置プリセットを保存しました: [{catLabel}{folderLabel}/{caseName}] -> {caseDir}", SICESI_StereoEvaluationController.TagCore);
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
