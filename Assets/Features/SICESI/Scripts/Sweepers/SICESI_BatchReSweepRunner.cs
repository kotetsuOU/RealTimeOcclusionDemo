using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// 過去に取得した実験ディレクトリ（例: RawTest）内の全 Case フォルダを走査し、
    /// 各 scene_transforms.json から仮想オブジェクトや手メッシュを自動検索・切り替えて
    /// Transform を復元しながら一括して自動再実験（8セクター二値マスクスイープ等）を実行するバッチランナー。
    /// </summary>
    public class SICESI_BatchReSweepRunner
    {
        private readonly SICESI_StereoEvaluationController _controller;

        public bool isRunning { get; private set; } = false;
        public string statusMessage { get; private set; } = "Ready";

        public SICESI_BatchReSweepRunner(SICESI_StereoEvaluationController controller)
        {
            _controller = controller;
        }

        /// <summary>
        /// 指定した親フォルダ内の全 Case を自動走査し、再実験を連続実行するコルーチン。
        /// </summary>
        public IEnumerator RunBatchReSweepRoutine(string parentDirectory)
        {
            if (isRunning)
            {
                AppLogger.LogWarning(_controller, "[SICESI_BatchReSweep] 既にバッチ再実験が実行中です。", SICESI_StereoEvaluationController.TagCore);
                yield break;
            }

            if (string.IsNullOrEmpty(parentDirectory) || !Directory.Exists(parentDirectory))
            {
                AppLogger.LogError(_controller, $"[SICESI_BatchReSweep] 対象親ディレクトリが存在しません: {parentDirectory}", SICESI_StereoEvaluationController.TagCore);
                yield break;
            }

            // 配下の Case ディレクトリを検索し、自然順ソート (1, 2, 3...)
            var caseDirs = GetSortedCaseDirectories(parentDirectory);
            if (caseDirs.Count == 0)
            {
                AppLogger.LogWarning(_controller, $"[SICESI_BatchReSweep] scene_transforms.json を含む Case ディレクトリが見つかりませんでした: {parentDirectory}", SICESI_StereoEvaluationController.TagCore);
                yield break;
            }

            isRunning = true;
            _controller.isCapturing = true;

            var collector = _controller.GetComponent<SICESI_SectorMaskCollector>();
            if (collector == null)
            {
                collector = _controller.gameObject.AddComponent<SICESI_SectorMaskCollector>();
            }

            AppLogger.Log(_controller, $"=== [SICESI_BatchReSweep] 全 {caseDirs.Count} 件の Case 一括再実験を開始します ===", SICESI_StereoEvaluationController.TagCore);

            for (int i = 0; i < caseDirs.Count; i++)
            {
                string caseDir = caseDirs[i];
                string caseName = Path.GetFileName(caseDir);
                string jsonPath = Path.Combine(caseDir, "scene_transforms.json");

                statusMessage = $"Processing Case {i + 1}/{caseDirs.Count} [{caseName}]...";
                _controller.statusMessage = statusMessage;
                AppLogger.Log(_controller, $"--- [{i + 1}/{caseDirs.Count}] Case '{caseName}' 再実験開始: {jsonPath} ---", SICESI_StereoEvaluationController.TagCore);

                // 1. JSON ロード & オブジェクトの自動解決・切り替え
                bool loadSuccess = LoadAndResolveCaseScene(jsonPath, caseName);
                if (!loadSuccess)
                {
                    AppLogger.LogWarning(_controller, $"Case '{caseName}' のシーン設定復元に失敗したためスキップします。", SICESI_StereoEvaluationController.TagCore);
                    continue;
                }

                // 2. 物理・描画反映待機
                for (int f = 0; f < 5; f++) yield return null;

                // 3. 8セクター二値マスクスイープを実行
                collector.RunSector8MaskSweep();

                // 4. スイープ完了まで待機
                while (collector.isCollecting)
                {
                    yield return new WaitForSeconds(0.2f);
                }

                yield return new WaitForSeconds(0.5f);
                AppLogger.Log(_controller, $"--- [{i + 1}/{caseDirs.Count}] Case '{caseName}' 再実験完了 ---", SICESI_StereoEvaluationController.TagCore);
            }

            isRunning = false;
            _controller.isCapturing = false;
            statusMessage = "All Batch Re-Sweep Cases Completed!";
            _controller.statusMessage = statusMessage;
            AppLogger.Log(_controller, $"=== [SICESI_BatchReSweep] 全 {caseDirs.Count} 件の一括再実験が正常に完了しました ===", SICESI_StereoEvaluationController.TagCore);
        }

        /// <summary>
        /// 選択された最適化用配置プリセット (JSON) 群の 8枚二値マスク収集＆最適化ルーチン。
        /// 処理は <see cref="SICESI_OptimizationPresetRunner"/> に委譲されています。
        /// </summary>
        public IEnumerator RunOptimizationPresetsRoutine(
            List<SICESI_PresetPoseManager.PresetEntry> presets,
            bool autoRunPython = true)
        {
            return _controller.OptimizationPresetRunner.RunOptimizationPresetsRoutine(presets, autoRunPython);
        }

        /// <summary>
        /// scene_transforms.json からオブジェクト名を取得し、シーン内の GameObject を解決して Transform を適用します。
        /// </summary>
        private bool LoadAndResolveCaseScene(string jsonPath, string defaultCaseName)
        {
            try
            {
                if (!File.Exists(jsonPath)) return false;
                string json = File.ReadAllText(jsonPath);
                var data = JsonUtility.FromJson<SICESI_SceneMetadataSerializer.SceneTransformsData>(json);
                if (data == null) return false;

                // 1. 条件名の更新
                _controller.conditionName = !string.IsNullOrEmpty(data.conditionName) ? data.conditionName : defaultCaseName;

                // 2. 仮想オブジェクトの自動検索・切り替え
                if (data.virtualObject != null)
                {
                    GameObject targetVo = ResolveGameObject(data.virtualObject.name, _controller.virtualObject);
                    if (targetVo != null)
                    {
                        if (_controller.virtualObject != null && _controller.virtualObject != targetVo)
                        {
                            _controller.virtualObject.SetActive(false);
                        }
                        targetVo.SetActive(true);
                        _controller.virtualObject = targetVo;
                        data.virtualObject.ApplyToTransform(targetVo.transform);
                    }
                }

                // 3. 手メッシュの自動検索・切り替え
                if (data.handMesh != null)
                {
                    GameObject targetHand = ResolveGameObject(data.handMesh.name, _controller.groundTruthObject);
                    if (targetHand != null)
                    {
                        if (_controller.groundTruthObject != null && _controller.groundTruthObject != targetHand)
                        {
                            _controller.groundTruthObject.SetActive(false);
                        }
                        targetHand.SetActive(true);
                        _controller.groundTruthObject = targetHand;
                        data.handMesh.ApplyToTransform(targetHand.transform);
                    }
                }

                // 4. カメラ群の Transform 適用
                if (data.leftCamera != null && _controller.leftEyeCamera != null)
                {
                    data.leftCamera.ApplyToTransform(_controller.leftEyeCamera.transform);
                }
                if (data.rightCamera != null && _controller.rightEyeCamera != null)
                {
                    data.rightCamera.ApplyToTransform(_controller.rightEyeCamera.transform);
                }
                if (data.sceneCamera != null && _controller.sceneCaptureCamera != null)
                {
                    data.sceneCamera.ApplyToTransform(_controller.sceneCaptureCamera.transform);
                }

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.LogError(_controller, $"[SICESI_BatchReSweep] JSON 解析エラー ({jsonPath}): {ex.Message}", SICESI_StereoEvaluationController.TagCore);
                return false;
            }
        }

        /// <summary>
        /// 名前を手がかりに、非アクティブ含むシーン全体から GameObject を探索します。
        /// </summary>
        private GameObject ResolveGameObject(string objectName, GameObject currentFallback)
        {
            if (string.IsNullOrEmpty(objectName)) return currentFallback;

            // 1. 現在のオブジェクトと名前が一致していればそのまま使用
            if (currentFallback != null && currentFallback.name == objectName)
            {
                return currentFallback;
            }

            // 2. データセット設定があればリスト内を優先検索
            if (_controller.datasetConfig != null)
            {
                var fromVoList = _controller.datasetConfig.FindVirtualObjectByName(objectName);
                if (fromVoList != null) return fromVoList;

                var fromHandList = _controller.datasetConfig.FindHandMeshByName(objectName);
                if (fromHandList != null) return fromHandList;
            }

            // 3. 非アクティブ含むシーン全体から同名オブジェクトを検索
            var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in allObjects)
            {
                // エディタ専用やプレハブアセットを除外
                if (go.hideFlags == HideFlags.NotEditable || go.hideFlags == HideFlags.HideAndDontSave) continue;
                if (!go.scene.isLoaded) continue;

                if (go.name == objectName)
                {
                    return go;
                }
            }

            return currentFallback;
        }

        /// <summary>
        /// scene_transforms.json を含む Case フォルダ一覧を取得しソートします。
        /// </summary>
        private List<string> GetSortedCaseDirectories(string parentDir)
        {
            var result = new List<string>();
            var subDirs = Directory.GetDirectories(parentDir);

            foreach (var dir in subDirs)
            {
                string jsonPath = Path.Combine(dir, "scene_transforms.json");
                if (File.Exists(jsonPath))
                {
                    result.Add(dir);
                }
            }

            // 自然順ソート (1, 2, 3, 10...)
            result.Sort((a, b) =>
            {
                string nameA = Path.GetFileName(a);
                string nameB = Path.GetFileName(b);
                if (int.TryParse(nameA, out int intA) && int.TryParse(nameB, out int intB))
                {
                    return intA.CompareTo(intB);
                }
                return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
            });

            return result;
        }
    }
}
