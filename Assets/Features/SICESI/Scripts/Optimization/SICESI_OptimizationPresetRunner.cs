using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// SICE SI 配置プリセット群を対象に、各配置のシーン復元、俯瞰撮影、
    /// 8セクター二値マスク収集 (Sector8MaskSweep) を一括実行し、
    /// 完了後に Python 256パターンLUT最適化スクリプトを自動起動・連携するランナークラス。
    /// </summary>
    public class SICESI_OptimizationPresetRunner
    {
        private readonly SICESI_StereoEvaluationController _controller;

        public bool isRunning { get; private set; } = false;
        public string statusMessage { get; private set; } = "Ready";

        public SICESI_OptimizationPresetRunner(SICESI_StereoEvaluationController controller)
        {
            _controller = controller;
        }

        /// <summary>
        /// 指定された最適化用プリセット一覧を順次復元し、8枚二値マスク群の画面保存と自動最適化を実行します。
        /// </summary>
        public IEnumerator RunOptimizationPresetsRoutine(
            List<SICESI_PresetPoseManager.PresetEntry> presets,
            bool autoRunPython = true)
        {
            if (isRunning)
            {
                AppLogger.LogWarning(_controller, "[SICESI_OptimizationPresetRunner] 既にスイープが実行中です。", SICESI_StereoEvaluationController.TagCore);
                yield break;
            }

            if (presets == null || presets.Count == 0)
            {
                AppLogger.LogError(_controller, "[SICESI_OptimizationPresetRunner] 実行対象の最適化用プリセットが指定されていません。", SICESI_StereoEvaluationController.TagCore);
                yield break;
            }

            isRunning = true;
            _controller.isCapturing = true;

            var collector = _controller.GetComponent<SICESI_SectorMaskCollector>();
            if (collector == null)
            {
                collector = _controller.gameObject.AddComponent<SICESI_SectorMaskCollector>();
            }

            AppLogger.Log(_controller, $"=== [SICESI_OptimizationPresetRunner] 最適化用プリセット全 {presets.Count} 件の 8枚二値マスク一括収集を開始します ===", SICESI_StereoEvaluationController.TagCore);

            string firstCaseDir = null;

            for (int i = 0; i < presets.Count; i++)
            {
                var preset = presets[i];
                string caseDir = Path.GetDirectoryName(preset.jsonPath);
                if (firstCaseDir == null) firstCaseDir = caseDir;

                statusMessage = $"Collecting 8-Sector Masks {i + 1}/{presets.Count} [{preset.caseName}]...";
                _controller.statusMessage = statusMessage;
                AppLogger.Log(_controller, $"--- [{i + 1}/{presets.Count}] 最適化プリセット '{preset.caseName}' 8枚二値マスク収集開始: {preset.jsonPath} ---", SICESI_StereoEvaluationController.TagCore);

                // 1. JSON ロード & オブジェクトの自動解決・復元 (SICESI_PresetSceneApplier 委譲)
                bool loadSuccess = SICESI_PresetSceneApplier.ApplyPresetToScene(_controller, preset.jsonPath);
                if (!loadSuccess)
                {
                    AppLogger.LogWarning(_controller, $"プリセット '{preset.caseName}' のシーン設定復元に失敗したためスキップします。", SICESI_StereoEvaluationController.TagCore);
                    continue;
                }

                // 2. 出力先ディレクトリを当該プリセットフォルダ直下に設定
                _controller.customConditionRootDir = caseDir;

                // 3. 物理・描画反映待機
                for (int f = 0; f < 5; f++) yield return null;

                // シーン俯瞰画像の撮影
                yield return SICESI_SceneOverviewCapturer.CaptureSceneOverviewRoutine(_controller, caseDir, preset.caseName);

                // 4. 8セクター二値マスクスイープを実行
                collector.RunSector8MaskSweep();

                // 5. スイープ完了まで待機
                while (collector.isCollecting)
                {
                    yield return new WaitForSeconds(0.2f);
                }

                yield return new WaitForSeconds(0.5f);
                AppLogger.Log(_controller, $"--- [{i + 1}/{presets.Count}] プリセット '{preset.caseName}' 8枚二値マスク収集完了 ---", SICESI_StereoEvaluationController.TagCore);
            }

            // 出力先リセット
            _controller.customConditionRootDir = null;
            isRunning = false;
            _controller.isCapturing = false;
            statusMessage = "All Optimization Preset Sweeps Completed!";
            _controller.statusMessage = statusMessage;
            AppLogger.Log(_controller, $"=== [SICESI_OptimizationPresetRunner] 全 {presets.Count} 件の 8枚二値マスク収集が正常に完了しました ===", SICESI_StereoEvaluationController.TagCore);

            // 6. Python 最適化の自動実行 (要求されている場合)
            if (autoRunPython)
            {
                string optDatasetRoot = !string.IsNullOrEmpty(firstCaseDir) ? Path.GetDirectoryName(firstCaseDir) : _controller.outputDirectory;
                string optOutputDir = Path.Combine(optDatasetRoot, "RuleOptimizationResults");
                AppLogger.Log(_controller, $"[SICESI_OptimizationPresetRunner] 8枚二値マスク収集完了に伴い、Python 256パターンLUT最適化を自動起動します。\n  対象データセット: {optDatasetRoot}\n  結果出力先フォルダ: {optOutputDir}", SICESI_StereoEvaluationController.TagCore);
                _controller.Dispatcher.RunPythonOptimization(datasetRoot: optDatasetRoot, outputDir: optOutputDir);
            }
        }
    }
}
