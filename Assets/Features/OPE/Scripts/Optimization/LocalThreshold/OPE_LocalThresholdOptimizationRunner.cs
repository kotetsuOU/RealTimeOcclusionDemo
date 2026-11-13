using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Core.Logging;

namespace OPE
{
    /// <summary>
    /// SICE SI 追加実験において、8セクター二値化閾値 T_loc を
    /// 0.00 から 1.00 までグリッドスイープし、
    /// 手メッシュから独自再構築した理想深度ピラミッド正解マスクとの 8方向 IoU が最大となる
    /// 最適値 T_loc* を導出・適用するメインランナー（オーケストレータ）。
    /// </summary>
    [AppLoggable("OPE (TLocOpt)")]
    public class OPE_LocalThresholdOptimizationRunner
    {
        public const string Tag = "TLocOpt";

        private readonly OPE_StereoEvaluationController _controller;

        public bool isRunning { get; private set; } = false;
        public string statusMessage { get; private set; } = "Ready";
        public float progress { get; private set; } = 0f;

        /// <summary> 直前の最適化実行で得られた最適 T_loc </summary>
        public float lastBestTLoc { get; private set; } = 0.8f;

        /// <summary> 直前の最適化実行で得られた最大 8方向平均 IoU </summary>
        public float lastBestMeanIoU { get; private set; } = 0f;

        /// <summary> 直前の最適化実行で得られたセクター別 (0..7) IoU (全体共通 T_loc* 時) </summary>
        public float[] lastBestSectorIoU { get; private set; } = new float[8];

        /// <summary> 直前の最適化実行で得られた 8方向独立 最適 T_loc,s* 配列 (サイズ 8) </summary>
        public float[] lastBestSectorIndependentTLoc { get; private set; } = new float[8];

        /// <summary> 直前の最適化実行で得られた 8方向独立 各方向の最大 IoU 配列 (サイズ 8) </summary>
        public float[] lastBestSectorIndependentIoU { get; private set; } = new float[8];

        /// <summary> 直前の最適化実行で得られた 8方向独立 最適閾値適用時の平均 IoU </summary>
        public float lastBestSectorIndependentMeanIoU { get; private set; } = 0f;

        /// <summary> 直前の最適化実行サマリー文字列 </summary>
        public string lastSummaryText { get; private set; } = "";

        /// <summary> 出力先ディレクトリ </summary>
        public string lastOutputDir { get; private set; } = "";

        public OPE_LocalThresholdOptimizationRunner(OPE_StereoEvaluationController controller)
        {
            _controller = controller;
        }

        /// <summary>
        /// 指定されたプリセット一覧に対して T_loc スイープ最適化コルーチンを実行します。
        /// </summary>
        public IEnumerator RunOptimizationRoutine(
            List<OPE_PresetPoseManager.PresetEntry> presets,
            float startT = 0.0f,
            float endT = 1.0f,
            float stepT = 0.01f,
            bool autoApplyToPipeline = true,
            bool ensureNoiseEnabled = true,
            float overrideNoiseMm = 2.0f)
        {
            if (isRunning)
            {
                AppLogger.LogWarning(_controller, "[TLocOpt] 既に最適化処理が実行中です。", Tag);
                yield break;
            }

            if (presets == null || presets.Count == 0)
            {
                AppLogger.LogError(_controller, "[TLocOpt] 対象プリセットが指定されていません。", Tag);
                yield break;
            }

            if (_controller.occlusionPipelineController == null)
            {
                AppLogger.LogError(_controller, "[TLocOpt] PCDOcclusionPipelineController が見つかりません。", Tag);
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

            string baseOutputDir = Path.Combine(_controller.outputDirectory, "Optimization", "LocalThreshold");
            Directory.CreateDirectory(baseOutputDir);
            lastOutputDir = baseOutputDir;

            AppLogger.Log(_controller, $"=== [TLocOpt] 8セクター二値化閾値 T_loc 最適化スイープ開始 (理想深度ピラミッドGT探索) ===", Tag);
            AppLogger.Log(_controller, $"  対象プリセット数: {presets.Count} 件", Tag);
            AppLogger.Log(_controller, $"  スイープ範囲: T_loc = {startT:F2} .. {endT:F2} (step: {stepT:F2}, 計 {stepCount} 点)", Tag);
            AppLogger.Log(_controller, $"  ノイズ強制有効化: {ensureNoiseEnabled} (振幅: {overrideNoiseMm:F2} mm)", Tag);
            AppLogger.Log(_controller, $"  保存先: {baseOutputDir}", Tag);

            float prevTLoc = _controller.occlusionPipelineController.T_loc;

            try
            {
                // 1. 理想深度ピラミッド再構築 & メモリ上での超高速スイープ評価を実行
                OPE_LocalThresholdSweepResult sweepResult = null;
                yield return OPE_LocalThresholdSweepEvaluator.EvaluateSweepRoutine(
                    _controller,
                    presets,
                    tValues,
                    onProgress: (msg, prog) =>
                    {
                        statusMessage = msg;
                        _controller.statusMessage = msg;
                        progress = prog;
                    },
                    onCompleted: res => sweepResult = res,
                    ensureNoiseEnabled: ensureNoiseEnabled,
                    overrideNoiseMm: overrideNoiseMm);

                if (sweepResult == null)
                {
                    AppLogger.LogError(_controller, "[TLocOpt] スイープ評価結果の取得に失敗しました。", Tag);
                    yield break;
                }

                lastBestTLoc = sweepResult.bestTLoc;
                lastBestMeanIoU = sweepResult.bestMeanIoU;
                lastBestSectorIoU = sweepResult.bestSectorIoU;
                lastBestSectorIndependentTLoc = sweepResult.bestSectorIndependentTLoc;
                lastBestSectorIndependentIoU = sweepResult.bestSectorIndependentIoU;
                lastBestSectorIndependentMeanIoU = sweepResult.bestSectorIndependentMeanIoU;

                AppLogger.Log(_controller, $"=== [TLocOpt] 最適化集計完了 ===", Tag);
                AppLogger.Log(_controller, $"👑 全体共通 最適 T_loc = {sweepResult.bestTLoc:F2} (全 {presets.Count} 配置 8方向平均IoU: {sweepResult.bestMeanIoU:P2})", Tag);
                AppLogger.Log(_controller, $"🌐 8方向独立 最適平均 IoU: {sweepResult.bestSectorIndependentMeanIoU:P2}", Tag);

                // 2. 最適値における画像（GT & 最適マスク）を各プリセットで生成・保存
                yield return OPE_LocalThresholdOptimalImageCapturer.CaptureOptimalImagesRoutine(
                    _controller,
                    presets,
                    sweepResult.bestTLoc,
                    baseOutputDir,
                    onStatusUpdate: status =>
                    {
                        statusMessage = status;
                        _controller.statusMessage = status;
                    },
                    bestSectorIndependentTLoc: sweepResult.bestSectorIndependentTLoc);

                // 3. CSV 出力
                string summaryCsvPath = Path.Combine(baseOutputDir, "t_loc_sweep_summary.csv");
                OPE_LocalThresholdOptimizationReporter.ExportSummaryCsv(
                    summaryCsvPath,
                    sweepResult.tLocValues,
                    sweepResult.overallMeanIoU,
                    sweepResult.overallOverOccRatio,
                    sweepResult.overallUnderOccRatio,
                    sweepResult.sectorIoUMatrix,
                    sweepResult.bestTLoc,
                    sweepResult.bestMeanIoU,
                    sweepResult.bestSectorIndependentTLoc,
                    sweepResult.bestSectorIndependentMeanIoU);

                string bySectorCsvPath = Path.Combine(baseOutputDir, "t_loc_sweep_by_sector.csv");
                OPE_LocalThresholdOptimizationReporter.ExportBySectorCsv(
                    bySectorCsvPath,
                    sweepResult.tLocValues,
                    sweepResult.sectorIoUMatrix,
                    sweepResult.bestSectorIoU,
                    sweepResult.bestSectorOverOcc,
                    sweepResult.bestSectorUnderOcc,
                    sweepResult.bestSectorIndependentTLoc,
                    sweepResult.bestSectorIndependentIoU);

                string byPresetCsvPath = Path.Combine(baseOutputDir, "t_loc_sweep_by_preset.csv");
                OPE_LocalThresholdOptimizationReporter.ExportByPresetCsv(
                    byPresetCsvPath,
                    presets,
                    sweepResult.tLocValues,
                    sweepResult.presetIoUMatrix,
                    sweepResult.bestTIndex);

                // 4. サマリーログ構築
                lastSummaryText = OPE_LocalThresholdOptimizationReporter.BuildSummaryText(
                    presets.Count,
                    startT,
                    endT,
                    stepT,
                    stepCount,
                    sweepResult.bestTLoc,
                    sweepResult.bestMeanIoU,
                    sweepResult.bestSectorIoU,
                    baseOutputDir,
                    summaryCsvPath,
                    sweepResult.bestSectorIndependentTLoc,
                    sweepResult.bestSectorIndependentIoU,
                    sweepResult.bestSectorIndependentMeanIoU);

                AppLogger.Log(_controller, lastSummaryText, Tag);

                // 5. パイプラインへの自動適用
                if (autoApplyToPipeline)
                {
                    ApplyBestValueToPipeline(sweepResult.bestTLoc, sweepResult.bestMeanIoU);
                }
            }
            finally
            {
                if (!autoApplyToPipeline)
                {
                    _controller.occlusionPipelineController.T_loc = prevTLoc;
                    if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
                    {
                        PCDRendererFeature.Instance.settings.occlusionThreshold_T_loc = prevTLoc;
                    }
                }

                isRunning = false;
                _controller.isCapturing = false;
                progress = 1.0f;
                statusMessage = $"T_loc Optimization Completed! Best T_loc = {lastBestTLoc:F2} (8Dir-IoU: {lastBestMeanIoU:P2})";
                _controller.statusMessage = statusMessage;
            }
        }

        public void ApplyBestValueToPipeline(float bestTLoc, float bestMeanIoU)
        {
            if (_controller.occlusionPipelineController != null)
            {
                _controller.occlusionPipelineController.useSectorSpecificThreshold = false;
                _controller.occlusionPipelineController.T_loc = bestTLoc;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(_controller.occlusionPipelineController);
#endif
            }

            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                PCDRendererFeature.Instance.settings.useSectorSpecificThreshold = false;
                PCDRendererFeature.Instance.settings.occlusionThreshold_T_loc = bestTLoc;
            }

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(_controller);
#endif

            AppLogger.Log(_controller, $"[TLocOpt] パイプラインコントローラーに全体共通 T_loc 最適値 {bestTLoc:F2} を適用しました。", Tag);
        }

        /// <summary>
        /// 8セクター個別の最適閾値ベクトルをパイプラインに適用します。
        /// </summary>
        public void ApplySectorIndependentToPipeline(float[] sectorThresholds, float meanIoU = 0f)
        {
            if (sectorThresholds == null || sectorThresholds.Length != 8) return;

            if (_controller.occlusionPipelineController != null)
            {
                _controller.occlusionPipelineController.useSectorSpecificThreshold = true;
                _controller.occlusionPipelineController.sectorThreshold_T_loc = (float[])sectorThresholds.Clone();
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(_controller.occlusionPipelineController);
#endif
            }

            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                PCDRendererFeature.Instance.settings.useSectorSpecificThreshold = true;
                PCDRendererFeature.Instance.settings.sectorThreshold_T_loc = (float[])sectorThresholds.Clone();
            }

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(_controller);
#endif

            AppLogger.Log(_controller, $"[TLocOpt] パイプラインコントローラーに 8方向個別 T_loc 最適ベクトルを適用しました (平均IoU: {meanIoU:P2})", Tag);
        }
    }
}

