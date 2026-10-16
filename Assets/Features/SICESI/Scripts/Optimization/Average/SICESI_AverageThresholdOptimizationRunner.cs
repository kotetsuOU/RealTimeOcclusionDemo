using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// SICE SI 追加実験において、Average 手法のオクルージョン判定閾値 T_ave を
    /// 0.00 から 1.00 までグリッドスイープし、全配置における平均 IoU が最大となる
    /// 最適値 T_ave* を導出・適用するメインランナー（オーケストレータ）。
    /// </summary>
    [AppLoggable("SICESI (TAveOpt)")]
    public class SICESI_AverageThresholdOptimizationRunner
    {
        public const string Tag = "TAveOpt";

        private readonly SICESI_StereoEvaluationController _controller;

        public bool isRunning { get; private set; } = false;
        public string statusMessage { get; private set; } = "Ready";
        public float progress { get; private set; } = 0f;

        /// <summary> 直前の最適化実行で得られた最適 T_ave </summary>
        public float lastBestTAve { get; private set; } = 0.8f;
        /// <summary> 直前の最適化実行で得られた最大平均 IoU </summary>
        public float lastBestMeanIoU { get; private set; } = 0f;
        /// <summary> 直前の最適化実行サマリー文字列 </summary>
        public string lastSummaryText { get; private set; } = "";
        /// <summary> 出力先ディレクトリ </summary>
        public string lastOutputDir { get; private set; } = "";

        public SICESI_AverageThresholdOptimizationRunner(SICESI_StereoEvaluationController controller)
        {
            _controller = controller;
        }

        /// <summary>
        /// 指定されたプリセット一覧に対して T_ave スイープ最適化コルーチンを実行します。
        /// </summary>
        public IEnumerator RunOptimizationRoutine(
            List<SICESI_PresetPoseManager.PresetEntry> presets,
            float startT = 0.0f,
            float endT = 1.0f,
            float stepT = 0.01f,
            bool autoApplyToPipeline = true)
        {
            if (isRunning)
            {
                AppLogger.LogWarning(_controller, "[TAveOpt] 既に最適化処理が実行中です。", Tag);
                yield break;
            }

            if (presets == null || presets.Count == 0)
            {
                AppLogger.LogError(_controller, "[TAveOpt] 対象プリセットが指定されていません。", Tag);
                yield break;
            }

            if (_controller.occlusionPipelineController == null)
            {
                AppLogger.LogError(_controller, "[TAveOpt] PCDOcclusionPipelineController が見つかりません。", Tag);
                yield break;
            }

            isRunning = true;
            _controller.isCapturing = true;

            int stepCount = Mathf.RoundToInt((endT - startT) / stepT) + 1;
            float[] tValues = new float[stepCount];
            for (int i = 0; i < stepCount; i++)
            {
                tValues[i] = Mathf.Clamp01(startT + i * stepT);
            }

            string baseOutputDir = Path.Combine(_controller.outputDirectory, "Optimization", "AverageThreshold");
            Directory.CreateDirectory(baseOutputDir);
            lastOutputDir = baseOutputDir;

            AppLogger.Log(_controller, $"=== [TAveOpt] Average 手法 T_ave 最適化スイープ開始 ===", Tag);
            AppLogger.Log(_controller, $"  対象プリセット数: {presets.Count} 件", Tag);
            AppLogger.Log(_controller, $"  スイープ範囲: T_ave = {startT:F2} .. {endT:F2} (step: {stepT:F2}, 計 {stepCount} 点)", Tag);
            AppLogger.Log(_controller, $"  保存先: {baseOutputDir}", Tag);

            // パイプライン・マテリアルのバックアップ
            var prevMode = _controller.occlusionPipelineController.evaluationMode;
            var prevTAve = _controller.occlusionPipelineController.T_ave;
            var matCtrl = _controller.MaterialController;
            var prevColorMode = matCtrl != null ? matCtrl.colorMode : PointCloudColorMode.Skin;

            try
            {
                // 点群スプライト色を黒に設定 (Python UnionMaskMetrics threshold=128 完全互換)
                if (matCtrl != null)
                {
                    matCtrl.ChangeColorMode(PointCloudColorMode.Black);
                }

                // パイプラインを Average モードに設定
                _controller.occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.Average;

                // 1. メモリ上での高速スイープ評価を実行
                SICESI_AverageThresholdSweepResult sweepResult = null;
                yield return SICESI_AverageThresholdSweepEvaluator.EvaluateSweepRoutine(
                    _controller,
                    presets,
                    tValues,
                    onProgress: (msg, prog) =>
                    {
                        statusMessage = msg;
                        _controller.statusMessage = msg;
                        progress = prog;
                    },
                    onCompleted: res => sweepResult = res);

                if (sweepResult == null)
                {
                    AppLogger.LogError(_controller, "[TAveOpt] スイープ評価結果の取得に失敗しました。", Tag);
                    yield break;
                }

                lastBestTAve = sweepResult.bestTAve;
                lastBestMeanIoU = sweepResult.bestMeanIoU;

                AppLogger.Log(_controller, $"=== [TAveOpt] 最適化集計完了 ===", Tag);
                AppLogger.Log(_controller, $"👑 最適 T_ave = {sweepResult.bestTAve:F2} (全 {presets.Count} 配置 平均IoU: {sweepResult.bestMeanIoU:P2})", Tag);

                // 2. 最適値における画像のみを各プリセットで撮影・保存
                yield return SICESI_AverageThresholdOptimalImageCapturer.CaptureOptimalImagesRoutine(
                    _controller,
                    presets,
                    sweepResult.bestTAve,
                    baseOutputDir,
                    onStatusUpdate: status =>
                    {
                        statusMessage = status;
                        _controller.statusMessage = status;
                    });

                // 3. CSV 出力
                string summaryCsvPath = Path.Combine(baseOutputDir, "t_ave_sweep_summary.csv");
                SICESI_AverageThresholdOptimizationReporter.ExportSummaryCsv(
                    summaryCsvPath,
                    sweepResult.tValues,
                    sweepResult.overallMeanIoU,
                    sweepResult.overallMeanOverOcc,
                    sweepResult.overallMeanUnderOcc,
                    sweepResult.bestTAve,
                    sweepResult.bestMeanIoU);

                string byPresetCsvPath = Path.Combine(baseOutputDir, "t_ave_sweep_by_preset.csv");
                SICESI_AverageThresholdOptimizationReporter.ExportByPresetCsv(
                    byPresetCsvPath,
                    presets,
                    sweepResult.tValues,
                    sweepResult.presetIoUMatrix,
                    sweepResult.bestTIndex);

                // 4. サマリーログ構築
                lastSummaryText = SICESI_AverageThresholdOptimizationReporter.BuildSummaryText(
                    presets.Count,
                    startT,
                    endT,
                    stepT,
                    stepCount,
                    sweepResult.bestTAve,
                    sweepResult.bestMeanIoU,
                    baseOutputDir,
                    summaryCsvPath);

                AppLogger.Log(_controller, lastSummaryText, Tag);

                // 5. パイプラインへの自動適用
                if (autoApplyToPipeline)
                {
                    ApplyBestValueToPipeline(sweepResult.bestTAve, sweepResult.bestMeanIoU);
                }
            }
            finally
            {
                if (matCtrl != null)
                {
                    matCtrl.ChangeColorMode(prevColorMode);
                }

                if (!autoApplyToPipeline)
                {
                    _controller.occlusionPipelineController.evaluationMode = prevMode;
                    _controller.occlusionPipelineController.T_ave = prevTAve;
                }

                isRunning = false;
                _controller.isCapturing = false;
                progress = 1.0f;
                statusMessage = $"T_ave Optimization Completed! Best T_ave = {lastBestTAve:F2} (IoU: {lastBestMeanIoU:P2})";
                _controller.statusMessage = statusMessage;
            }
        }

        private void ApplyBestValueToPipeline(float bestTAve, float bestMeanIoU)
        {
            if (_controller.occlusionPipelineController != null)
            {
                _controller.occlusionPipelineController.T_ave = bestTAve;
                _controller.occlusionPipelineController.evaluationMode = PCDRendererFeature.PCD_OcclusionEvaluationMode.Average;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(_controller.occlusionPipelineController);
#endif
            }

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(_controller);
#endif

            AppLogger.Log(_controller, $"[TAveOpt] パイプラインコントローラーの T_ave を最適値 {bestTAve:F2} に更新しました。", Tag);

            if (SICESI_BenchmarkMethodRegistry.ActiveOptimizedParameters == null)
            {
                SICESI_BenchmarkMethodRegistry.ActiveOptimizedParameters = new SICESI_OptimizationResult();
            }
            SICESI_BenchmarkMethodRegistry.ActiveOptimizedParameters.averageBestTAve = bestTAve;
            SICESI_BenchmarkMethodRegistry.ActiveOptimizedParameters.averageMeanIoU = bestMeanIoU;
        }
    }
}
