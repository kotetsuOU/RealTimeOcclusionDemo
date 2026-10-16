using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// 新規仮想オブジェクトおよび手メッシュ、または保存済み配置プリセットに対して、
    /// 5手法（Mode_Average, Mode 8, 20, 36, 256）を一括適用し、
    /// 最終出力 Ground Truth とのピクセル単位 IoU 計測を進行・管理するベンチマークランナー。
    /// 各処理の実装は以下の専門クラスに委譲されています:
    /// - 評価手法定義・適用: <see cref="SICESI_BenchmarkMethodRegistry"/>
    /// - 最終出力GT撮影: <see cref="SICESI_BenchmarkGroundTruthCapturer"/>
    /// - CSV出力・画像ロード: <see cref="SICESI_BenchmarkReporter"/>
    /// </summary>
    public class SICESI_GeneralizationBenchmarkRunner
    {
        private readonly SICESI_StereoEvaluationController _controller;

        public bool isRunning { get; private set; } = false;
        public string statusMessage { get; private set; } = "Ready";
        public Dictionary<string, SICESI_MethodMetricsAccumulator> lastMethodAverages { get; private set; } = null;
        public string lastAverageSummaryText { get; private set; } = "";

        public SICESI_GeneralizationBenchmarkRunner(SICESI_StereoEvaluationController controller)
        {
            _controller = controller;
        }

        /// <summary>
        /// 評価用仮想オブジェクト × 評価用手メッシュ の全組み合わせで IoU 計測ベンチマークを実行します。
        /// </summary>
        public IEnumerator RunGeneralizationBenchmarkRoutine()
        {
            if (isRunning)
            {
                AppLogger.LogWarning(_controller, "[GeneralizationBenchmark] 既にベンチマークが実行中です。", SICESI_StereoEvaluationController.TagCore);
                yield break;
            }

            var config = _controller.datasetConfig;
            var voList = (config != null && config.evaluationVirtualObjects != null && config.evaluationVirtualObjects.Count > 0)
                ? config.evaluationVirtualObjects
                : new List<GameObject> { _controller.virtualObject };

            var handList = (config != null && config.evaluationHandMeshes != null && config.evaluationHandMeshes.Count > 0)
                ? config.evaluationHandMeshes
                : new List<GameObject> { _controller.groundTruthObject };

            voList = voList.FindAll(x => x != null);
            handList = handList.FindAll(x => x != null);

            if (voList.Count == 0 || handList.Count == 0)
            {
                AppLogger.LogError(_controller, "[GeneralizationBenchmark] 評価対象の VirtualObject または HandMesh が指定されていません。", SICESI_StereoEvaluationController.TagCore);
                yield break;
            }

            isRunning = true;
            _controller.isCapturing = true;

            string baseOutputDir = Path.Combine(_controller.outputDirectory, "AdditionalExperiments", "GeneralizationBenchmark");
            Directory.CreateDirectory(baseOutputDir);
            string csvPath = Path.Combine(baseOutputDir, "generalization_benchmark_summary.csv");
            SICESI_BenchmarkReporter.InitializeCsvHeader(csvPath);

            var accumulators = new Dictionary<string, SICESI_MethodMetricsAccumulator>();

            AppLogger.Log(_controller, $"=== [GeneralizationBenchmark] 追加実験開始 (VO: {voList.Count}種 × Hand: {handList.Count}種) ===", SICESI_StereoEvaluationController.TagCore);

            var evaluationMethods = SICESI_BenchmarkMethodRegistry.GetEvaluationMethods(_controller.occlusionPipelineController);

            int totalPairs = voList.Count * handList.Count;
            int pairIndex = 0;

            for (int vi = 0; vi < voList.Count; vi++)
            {
                var curVo = voList[vi];

                for (int hi = 0; hi < handList.Count; hi++)
                {
                    var curHand = handList[hi];
                    pairIndex++;

                    statusMessage = $"Evaluating Pair {pairIndex}/{totalPairs}: VO[{curVo.name}] x Hand[{curHand.name}]...";
                    _controller.statusMessage = statusMessage;
                    AppLogger.Log(_controller, $"--- [{pairIndex}/{totalPairs}] VO: {curVo.name}, Hand: {curHand.name} 評価開始 ---", SICESI_StereoEvaluationController.TagCore);

                    // 1. オブジェクトの切り替え & 点群同期
                    if (config != null)
                    {
                        config.ActivateSoleVirtualObject(curVo);
                        config.ActivateSoleHandMesh(curHand);
                    }
                    _controller.virtualObject = curVo;
                    _controller.groundTruthObject = curHand;
                    _controller.SyncDummyPointCloudWithGroundTruth(true);

                    string pairSubDir = Path.Combine(baseOutputDir, $"{curVo.name}_{curHand.name}");
                    Directory.CreateDirectory(pairSubDir);

                    // SICE SI 2026 メイン実験同様、俯瞰カメラで Unity 空間の配置を撮影 (手:肌色, 点群:オフ)
                    yield return SICESI_SceneOverviewCapturer.CaptureSceneOverviewRoutine(_controller, pairSubDir, $"{curVo.name}_{curHand.name}");

                    // 仮想物体を Unlit White に設定し、GT撮影〜5手法テスト撮影終了まで維持
                    var voBackup = _controller.MaterialSwapper.SetVirtualObjectUnlitWhiteState(
                        _controller.virtualObject, _controller.renderVirtualObjectAsUnlitWhite);

                    try
                    {
                        // 2. 最終出力 Ground Truth (GT) の撮影
                        string gtDir = Path.Combine(pairSubDir, "GT");
                        yield return SICESI_BenchmarkGroundTruthCapturer.CaptureFinalOutputGroundTruth(_controller, gtDir);

                        var (gtLeftTex, gtRightTex) = SICESI_BenchmarkReporter.LoadStereoTextures(gtDir, "gt");

                        if (gtLeftTex == null || gtRightTex == null)
                        {
                            AppLogger.LogError(_controller, $"最終出力 GT 画像の読み込みに失敗しました: {gtDir}", SICESI_StereoEvaluationController.TagCapture);
                            continue;
                        }

                        // 3. 5手法のスイープ評価 & IoU 計測
                        yield return ExecuteMethodsSweepRoutine(pairSubDir, csvPath, curVo.name, curHand.name, gtLeftTex, gtRightTex, accumulators, evaluationMethods);

                        Object.DestroyImmediate(gtLeftTex);
                        Object.DestroyImmediate(gtRightTex);
                    }
                    finally
                    {
                        _controller.MaterialSwapper.RestoreVirtualObjectState(voBackup);
                    }
                }
            }

            // 4. 全体5手法平均値の集計・CSV追記・サマリー出力
            SICESI_BenchmarkReporter.AppendOverallAverageSummary(csvPath, accumulators);
            string avgCsvPath = Path.Combine(baseOutputDir, "generalization_benchmark_overall_average.csv");
            SICESI_BenchmarkReporter.SaveOverallAverageCsv(avgCsvPath, accumulators);

            lastMethodAverages = accumulators;
            lastAverageSummaryText = SICESI_BenchmarkReporter.FormatOverallAverageSummaryString(accumulators);
            AppLogger.Log(_controller, lastAverageSummaryText, SICESI_StereoEvaluationController.TagCore);

            isRunning = false;
            _controller.isCapturing = false;
            statusMessage = "Generalization Benchmark Completed!";
            _controller.statusMessage = statusMessage;
            AppLogger.Log(_controller, $"=== [GeneralizationBenchmark] 全実験が完了しました！サマリー: {csvPath} (平均CSV: {avgCsvPath}) ===", SICESI_StereoEvaluationController.TagCore);
        }

        /// <summary>
        /// 登録された配置プリセット (JSON) 一覧を順次ロードしてベンチマークを実行します。
        /// </summary>
        public IEnumerator RunBenchmarkFromPresetsRoutine(List<SICESI_PresetPoseManager.PresetEntry> presets)
        {
            if (isRunning)
            {
                AppLogger.LogWarning(_controller, "[GeneralizationBenchmark] 既にベンチマークが実行中です。", SICESI_StereoEvaluationController.TagCore);
                yield break;
            }

            if (presets == null || presets.Count == 0)
            {
                AppLogger.LogError(_controller, "[GeneralizationBenchmark] 実行対象のプリセットが登録されていません。", SICESI_StereoEvaluationController.TagCore);
                yield break;
            }

            isRunning = true;
            _controller.isCapturing = true;

            string baseOutputDir = Path.Combine(_controller.outputDirectory, "AdditionalExperiments", "PresetBenchmark");
            Directory.CreateDirectory(baseOutputDir);
            string csvPath = Path.Combine(baseOutputDir, "preset_benchmark_summary.csv");
            SICESI_BenchmarkReporter.InitializeCsvHeader(csvPath);

            var accumulators = new Dictionary<string, SICESI_MethodMetricsAccumulator>();

            AppLogger.Log(_controller, $"=== [GeneralizationBenchmark] プリセット一括ベンチマーク開始 (全 {presets.Count} 件) ===", SICESI_StereoEvaluationController.TagCore);

            // 評価対象の5手法設定をベンチマーク開始時に一括固定取得（プリセットループ中のパラメータ汚染・手法名分裂を防止）
            var evaluationMethods = SICESI_BenchmarkMethodRegistry.GetEvaluationMethods(_controller.occlusionPipelineController);

            for (int pi = 0; pi < presets.Count; pi++)
            {
                var preset = presets[pi];
                statusMessage = $"Evaluating Preset {pi + 1}/{presets.Count} [{preset.caseName}]...";
                _controller.statusMessage = statusMessage;
                AppLogger.Log(_controller, $"--- [{pi + 1}/{presets.Count}] プリセット '{preset.caseName}' 実行開始 ---", SICESI_StereoEvaluationController.TagCore);

                // 1. プリセット適用
                bool applyOk = SICESI_PresetPoseManager.ApplyPresetToScene(_controller, preset.jsonPath);
                if (!applyOk)
                {
                    AppLogger.LogWarning(_controller, $"プリセット '{preset.caseName}' の適用に失敗したためスキップします。", SICESI_StereoEvaluationController.TagCore);
                    continue;
                }

                for (int f = 0; f < 5; f++) yield return null;

                string caseSubDir = Path.Combine(baseOutputDir, preset.caseName);
                Directory.CreateDirectory(caseSubDir);

                // SICE SI 2026 メイン実験同様、俯瞰カメラで Unity 空間の配置を撮影 (手:肌色, 点群:オフ)
                yield return SICESI_SceneOverviewCapturer.CaptureSceneOverviewRoutine(_controller, caseSubDir, preset.caseName);

                // 仮想物体を Unlit White に設定し、GT撮影〜5手法テスト撮影終了まで維持
                var voBackup = _controller.MaterialSwapper.SetVirtualObjectUnlitWhiteState(
                    _controller.virtualObject, _controller.renderVirtualObjectAsUnlitWhite);

                try
                {
                    // 2. 最終出力 GT 撮影
                    string gtDir = Path.Combine(caseSubDir, "GT");
                    yield return SICESI_BenchmarkGroundTruthCapturer.CaptureFinalOutputGroundTruth(_controller, gtDir);

                    var (gtLeftTex, gtRightTex) = SICESI_BenchmarkReporter.LoadStereoTextures(gtDir, "gt");

                    if (gtLeftTex == null || gtRightTex == null)
                    {
                        AppLogger.LogError(_controller, $"最終出力 GT 画像の読み込みに失敗しました: {gtDir}", SICESI_StereoEvaluationController.TagCapture);
                        continue;
                    }

                    // 3. 5手法のスイープ評価 & IoU 計測
                    yield return ExecuteMethodsSweepRoutine(caseSubDir, csvPath, preset.virtualObjectName, preset.handMeshName, gtLeftTex, gtRightTex, accumulators, evaluationMethods, preset.caseName);

                    Object.DestroyImmediate(gtLeftTex);
                    Object.DestroyImmediate(gtRightTex);
                }
                finally
                {
                    _controller.MaterialSwapper.RestoreVirtualObjectState(voBackup);
                }
            }

            // 4. 全体5手法平均値の集計・CSV追記・サマリー出力
            SICESI_BenchmarkReporter.AppendOverallAverageSummary(csvPath, accumulators);
            string avgCsvPath = Path.Combine(baseOutputDir, "preset_benchmark_overall_average.csv");
            SICESI_BenchmarkReporter.SaveOverallAverageCsv(avgCsvPath, accumulators);

            lastMethodAverages = accumulators;
            lastAverageSummaryText = SICESI_BenchmarkReporter.FormatOverallAverageSummaryString(accumulators);
            AppLogger.Log(_controller, lastAverageSummaryText, SICESI_StereoEvaluationController.TagCore);

            isRunning = false;
            _controller.isCapturing = false;
            statusMessage = "Preset Benchmark Completed!";
            _controller.statusMessage = statusMessage;
            AppLogger.Log(_controller, $"=== [GeneralizationBenchmark] プリセット一括ベンチマーク完了！サマリー: {csvPath} (平均CSV: {avgCsvPath}) ===", SICESI_StereoEvaluationController.TagCore);
        }

        private IEnumerator ExecuteMethodsSweepRoutine(
            string parentSubDir,
            string csvPath,
            string voName,
            string handName,
            Texture2D gtLeftTex,
            Texture2D gtRightTex,
            Dictionary<string, SICESI_MethodMetricsAccumulator> accumulators,
            List<SICESI_BenchmarkMethodRegistry.MethodConfig> evaluationMethods = null,
            string logPrefix = "")
        {
            if (evaluationMethods == null || evaluationMethods.Count == 0)
            {
                evaluationMethods = SICESI_BenchmarkMethodRegistry.GetEvaluationMethods(_controller.occlusionPipelineController);
            }
            float[] densities = _controller.sweepDensities != null && _controller.sweepDensities.Length > 0
                ? _controller.sweepDensities
                : new float[] { 2.0f };

            // テスト画像撮影時のみ、点群スプライトの描画色を黒に一時変更 (Python threshold=128 完全互換)
            var matCtrl = _controller.MaterialController;
            var prevColorMode = matCtrl != null ? matCtrl.colorMode : PointCloudColorMode.Skin;
            if (matCtrl != null)
            {
                matCtrl.ChangeColorMode(PointCloudColorMode.Black);
                AppLogger.Log(_controller, "[GeneralizationBenchmark] RsMaterialController 経由で点群描画色を一時的に Black に設定しました。", SICESI_StereoEvaluationController.TagMaskSweep);
            }

            try
            {
                foreach (var density in densities)
                {
                    if (_controller.dummyPointCloudProvider != null)
                    {
                        _controller.dummyPointCloudProvider.densityUnit = _controller.densityUnit;
                        _controller.dummyPointCloudProvider.densityValue = density;
                        // 次フレームの Update を待たず、密度に対応した点群を確実に再サンプリングする
                        _controller.dummyPointCloudProvider.ForceUpdateSampling();

                        int pointCount = _controller.dummyPointCloudProvider.LastSampledData.PointCount;
                        AppLogger.Log(_controller, $"[GeneralizationBenchmark] Density={density:F1} 再サンプリング: {pointCount} 点", SICESI_StereoEvaluationController.TagMaskSweep);
                    }

                    if (PCDRendererFeature.Instance != null)
                    {
                        PCDRendererFeature.Instance.MarkPointCloudDataDirty();
                    }

                    foreach (var method in evaluationMethods)
                    {
                        SICESI_BenchmarkMethodRegistry.ApplyMethodSettings(_controller.occlusionPipelineController, method);

                        for (int w = 0; w < _controller.waitFramesAfterDensityChange + 2; w++) yield return null;
                        yield return new WaitForEndOfFrame();

                        string methodDirName = $"Density_{density:F1}{SICESI_EvaluationParamsHelper.GetDensityUnitSuffix(_controller.densityUnit)}_{method.methodName}";
                        string testDir = Path.Combine(parentSubDir, methodDirName);
                        Directory.CreateDirectory(testDir);

                        // 前回実行の残存画像を誤って読み込まないよう、撮影前に削除する
                        SICESI_BenchmarkImageIO.DeleteStereoImages(testDir, "test");

                        SICESI_ScreenCaptureUtil.CaptureStereoViews(
                            _controller.leftEyeCamera, _controller.rightEyeCamera, testDir, "test", _controller.applySRGBConversion);

                        yield return new WaitForSeconds(0.15f);

                        var (testLeftTex, testRightTex) = SICESI_BenchmarkImageIO.LoadStereoTextures(testDir, "test");

                        if (testLeftTex != null && testRightTex != null)
                        {
                            var leftMetrics = SICESI_IoUCalculator.Calculate(gtLeftTex, testLeftTex);
                            var rightMetrics = SICESI_IoUCalculator.Calculate(gtRightTex, testRightTex);
                            var stereoMetrics = SICESI_IoUCalculator.StereoMetricsResult.FromStereo(leftMetrics, rightMetrics);

                            // 5手法の全体平均用アキュムレータに集計
                            if (accumulators != null)
                            {
                                if (!accumulators.TryGetValue(method.methodName, out var acc))
                                {
                                    acc = new SICESI_MethodMetricsAccumulator(method.methodName);
                                    accumulators[method.methodName] = acc;
                                }
                                acc.Add(stereoMetrics);
                            }

                            string tag = string.IsNullOrEmpty(logPrefix) ? method.methodName : $"{logPrefix} | {method.methodName}";
                            string thInfo = (method.mode == PCDRendererFeature.PCD_OcclusionEvaluationMode.Average)
                                ? $"T_ave={method.tAve:F2}"
                                : $"T_loc={method.tLoc:F2}";
                            AppLogger.Log(_controller, $"[{tag}] ({thInfo}) Density={density:F1} IoU: {stereoMetrics.meanIoU:P2} (L: {leftMetrics.iou:P2}, R: {rightMetrics.iou:P2}) L-FP(遮蔽漏れ): {leftMetrics.fpPixels}, L-FN(過剰遮蔽): {leftMetrics.fnPixels}", SICESI_StereoEvaluationController.TagMaskSweep);

                            SICESI_BenchmarkReporter.AppendCsvRecord(csvPath, voName, handName, density, method.methodName, stereoMetrics);
                            SICESI_BenchmarkImageIO.SaveDiffImage(gtLeftTex, testLeftTex, Path.Combine(testDir, "diff_left.png"));

                            Object.DestroyImmediate(testLeftTex);
                            Object.DestroyImmediate(testRightTex);
                        }
                    }
                }
            }
            finally
            {
                if (matCtrl != null)
                {
                    matCtrl.ChangeColorMode(prevColorMode);
                    AppLogger.Log(_controller, $"[GeneralizationBenchmark] RsMaterialController のカラーモードを復元しました ({prevColorMode})。", SICESI_StereoEvaluationController.TagMaskSweep);
                }
            }
        }
    }
}
