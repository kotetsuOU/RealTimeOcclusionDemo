using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Core.Logging;

namespace OPE
{
    /// <summary>
    /// 独自に再構築した理想深度ピラミッド (L0〜L6) から得た 8方向理論正解 (GT) マスクと、
    /// 複数密度 (sweepDensities) の疎・ノイズあり実測点群評価キャッシュを用いて、
    /// 全プリセットに対する T_loc スイープ評価をメモリ上で超高速かつ厳密に行う Evaluator クラス。
    /// </summary>
    public static class OPE_LocalThresholdSweepEvaluator
    {
        private const string Tag = "TLocOpt";

        /// <summary>
        /// 全プリセットおよび各密度に対して各 T_loc の 8方向 IoU を計算し、最適値を導出するコルーチン。
        /// </summary>
        public static IEnumerator EvaluateSweepRoutine(
            OPE_StereoEvaluationController controller,
            List<OPE_PresetPoseManager.PresetEntry> presets,
            float[] tLocValues,
            Action<string, float> onProgress,
            Action<OPE_LocalThresholdSweepResult> onCompleted,
            bool ensureNoiseEnabled = true,
            float overrideNoiseMm = 2.0f)
        {
            if (controller == null || presets == null || presets.Count == 0 || tLocValues == null || tLocValues.Length == 0)
            {
                onCompleted?.Invoke(null);
                yield break;
            }

            int stepCount = tLocValues.Length;
            int presetCount = presets.Count;

            float[] densities = (controller.sweepDensities != null && controller.sweepDensities.Length > 0)
                ? controller.sweepDensities
                : new float[] { 0.25f, 1.0f, 4.0f };

            // [プリセット番号, T_locインデックス]
            float[,] presetIoUMatrix = new float[presetCount, stepCount];

            // [セクター番号 0..7, T_locインデックス] の累積合計
            float[,] sectorIoUSumMatrix = new float[8, stepCount];
            float[,] sectorOverOccSumMatrix = new float[8, stepCount];
            float[,] sectorUnderOccSumMatrix = new float[8, stepCount];

            float[] overallMeanIoUSum = new float[stepCount];
            float[] overallOverOccSum = new float[stepCount];
            float[] overallUnderOccSum = new float[stepCount];

            var provider = controller.dummyPointCloudProvider;
            float originalDensity = provider != null ? provider.densityValue : 1.0f;
            bool prevEnableNoise = false;
            float prevNoiseAmountMm = 2.0f;
            float prevNoiseRatio = 1.0f;
            bool noiseWasOverridden = false;

            if (provider != null && provider.noiseSettings != null)
            {
                prevEnableNoise = provider.noiseSettings.enableNoise;
                prevNoiseAmountMm = provider.noiseSettings.noiseAmountMm;
                prevNoiseRatio = provider.noiseSettings.noiseRatio;

                if (ensureNoiseEnabled && (!provider.noiseSettings.enableNoise || provider.noiseSettings.noiseAmountMm <= 0.001f))
                {
                    provider.noiseSettings.enableNoise = true;
                    provider.noiseSettings.noiseAmountMm = Mathf.Max(0.1f, overrideNoiseMm);
                    if (provider.noiseSettings.noiseRatio <= 0.001f)
                    {
                        provider.noiseSettings.noiseRatio = 1.0f;
                    }
                    noiseWasOverridden = true;
                    AppLogger.Log(controller,
                        $"[TLocOpt] 評価点群のノイズ設定を有効化しました (Noise: {provider.noiseSettings.noiseAmountMm:F2} mm, Ratio: {provider.noiseSettings.noiseRatio:P0})",
                        Tag);
                }
            }

            AppLogger.Log(controller,
                $"[TLocOpt] スイープ開始: プリセット数={presetCount}, 評価密度=[{string.Join(", ", densities)}], T_locステップ数={stepCount}",
                Tag);

            int validPresetCount = 0;

            try
            {
                for (int pi = 0; pi < presetCount; pi++)
                {
                    var preset = presets[pi];
                    AppLogger.Log(controller, $"--- [{pi + 1}/{presetCount}] プリセット '{preset.caseName}' T_loc 計測開始 ---", Tag);

                    // 1. プリセット適用
                    bool applyOk = OPE_PresetPoseManager.ApplyPresetToScene(controller, preset.jsonPath);
                    if (!applyOk)
                    {
                        AppLogger.LogWarning(controller, $"プリセット '{preset.caseName}' の適用に失敗したためスキップします。", Tag);
                        continue;
                    }

                    if (provider != null) provider.ForceUpdateSampling();
                    if (PCDRendererFeature.Instance != null) PCDRendererFeature.Instance.MarkPointCloudDataDirty();

                    yield return null;

                    // 2. メッシュから理想深度ピラミッド (L0〜L6) を1度だけ構築 (点群密度に依存しないため高速)
                    var gtData = OPE_LocalThresholdGroundTruthCapturer.BuildIdealPyramidsOnly(controller);
                    if (gtData == null || gtData.leftIdealPyramid == null)
                    {
                        AppLogger.LogError(controller, $"理想深度ピラミッドの構築に失敗しました: {preset.caseName}", Tag);
                        gtData?.Dispose();
                        continue;
                    }

                    validPresetCount++;

                    try
                    {
                        // プリセット内・全密度における T_loc 別累積メトリクス [ti]
                        float[] presetIoUAcc = new float[stepCount];
                        float[] presetOverAcc = new float[stepCount];
                        float[] presetUnderAcc = new float[stepCount];
                        float[,] presetSectorIoUAcc = new float[8, stepCount];
                        float[,] presetSectorOverAcc = new float[8, stepCount];
                        float[,] presetSectorUnderAcc = new float[8, stepCount];

                        // 3. 複数密度ループ (0.25, 1.0, 4.0 等)
                        for (int di = 0; di < densities.Length; di++)
                        {
                            float currentDensity = densities[di];
                            if (provider != null)
                            {
                                provider.densityUnit = controller.densityUnit;
                                provider.densityValue = currentDensity;
                                provider.ForceUpdateSampling();
                            }

                            // 現在の点群密度に応じた探索半径と評価キャッシュ、および理論GTマスクを構築
                            OPE_LocalThresholdGroundTruthCapturer.UpdateEvaluationCacheForCurrentPointCloud(controller, gtData);

                            if (gtData.gtLeftData == null || gtData.leftTestCache == null)
                            {
                                continue;
                            }

                            // 4. 各 T_loc に対する 8方向セクター別 IoU 計算 (Pure C# メモリ演算)
                            for (int ti = 0; ti < stepCount; ti++)
                            {
                                float curT = tLocValues[ti];

                                var stereoMetrics = OPE_Sector8IoUCalculator.CalculateStereoFromEvaluationCache(
                                    gtData.gtLeftData,
                                    gtData.leftTestCache,
                                    gtData.gtRightData,
                                    gtData.rightTestCache,
                                    curT);

                                presetIoUAcc[ti] += stereoMetrics.meanIoU;
                                float overRatio = (stereoMetrics.left.meanOverOcc + stereoMetrics.right.meanOverOcc) * 0.5f;
                                float underRatio = (stereoMetrics.left.meanUnderOcc + stereoMetrics.right.meanUnderOcc) * 0.5f;
                                presetOverAcc[ti] += overRatio;
                                presetUnderAcc[ti] += underRatio;

                                for (int s = 0; s < 8; s++)
                                {
                                    presetSectorIoUAcc[s, ti] += stereoMetrics.sectorMeanIoU[s];
                                    float sOver = (stereoMetrics.left.sectors[s].overOccRatio + stereoMetrics.right.sectors[s].overOccRatio) * 0.5f;
                                    float sUnder = (stereoMetrics.left.sectors[s].underOccRatio + stereoMetrics.right.sectors[s].underOccRatio) * 0.5f;
                                    presetSectorOverAcc[s, ti] += sOver;
                                    presetSectorUnderAcc[s, ti] += sUnder;
                                }

                                if (ti % 25 == 0 || ti == stepCount - 1)
                                {
                                    float currentProgress = ((float)pi + ((float)di + (float)ti / stepCount) / densities.Length) / presetCount;
                                    string stepStatus = $"[{pi + 1}/{presetCount}] {preset.caseName} (Density={currentDensity:F2}): T_loc={curT:F2}";
                                    onProgress?.Invoke(stepStatus, currentProgress);
                                }
                            }
                        }

                        // プリセット内の全密度平均を全体集計バッファへ加算
                        for (int ti = 0; ti < stepCount; ti++)
                        {
                            float avgIoU = presetIoUAcc[ti] / densities.Length;
                            float avgOver = presetOverAcc[ti] / densities.Length;
                            float avgUnder = presetUnderAcc[ti] / densities.Length;

                            presetIoUMatrix[pi, ti] = avgIoU;
                            overallMeanIoUSum[ti] += avgIoU;
                            overallOverOccSum[ti] += avgOver;
                            overallUnderOccSum[ti] += avgUnder;

                            for (int s = 0; s < 8; s++)
                            {
                                sectorIoUSumMatrix[s, ti] += presetSectorIoUAcc[s, ti] / densities.Length;
                                sectorOverOccSumMatrix[s, ti] += presetSectorOverAcc[s, ti] / densities.Length;
                                sectorUnderOccSumMatrix[s, ti] += presetSectorUnderAcc[s, ti] / densities.Length;
                            }
                        }
                    }
                    finally
                    {
                        gtData.Dispose();
                    }

                    yield return null;
                }
            }
            finally
            {
                // ノイズ設定および点群密度の復元
                if (provider != null)
                {
                    provider.densityValue = originalDensity;
                    if (noiseWasOverridden && provider.noiseSettings != null)
                    {
                        provider.noiseSettings.enableNoise = prevEnableNoise;
                        provider.noiseSettings.noiseAmountMm = prevNoiseAmountMm;
                        provider.noiseSettings.noiseRatio = prevNoiseRatio;
                    }
                    provider.ForceUpdateSampling();
                }
                if (PCDRendererFeature.Instance != null) PCDRendererFeature.Instance.MarkPointCloudDataDirty();
            }

            int divisor = Mathf.Max(1, validPresetCount);

            // 5. 全配置における平均値と最適値の集計
            var result = new OPE_LocalThresholdSweepResult
            {
                tLocValues = tLocValues,
                presetIoUMatrix = presetIoUMatrix,
                sectorIoUMatrix = new float[8, stepCount],
                overallMeanIoU = new float[stepCount],
                overallOverOccRatio = new float[stepCount],
                overallUnderOccRatio = new float[stepCount],
                bestSectorIoU = new float[8],
                bestSectorOverOcc = new float[8],
                bestSectorUnderOcc = new float[8],
                bestMeanIoU = -1f,
                bestTIndex = 0
            };

            for (int ti = 0; ti < stepCount; ti++)
            {
                result.overallMeanIoU[ti] = overallMeanIoUSum[ti] / divisor;
                result.overallOverOccRatio[ti] = overallOverOccSum[ti] / divisor;
                result.overallUnderOccRatio[ti] = overallUnderOccSum[ti] / divisor;

                for (int s = 0; s < 8; s++)
                {
                    result.sectorIoUMatrix[s, ti] = sectorIoUSumMatrix[s, ti] / divisor;
                }

                if (result.overallMeanIoU[ti] > result.bestMeanIoU)
                {
                    result.bestMeanIoU = result.overallMeanIoU[ti];
                    result.bestTLoc = tLocValues[ti];
                    result.bestTIndex = ti;
                }
            }

            // 最適値における各セクターのメトリクスを記録 (全体共通 T_loc* 時)
            for (int s = 0; s < 8; s++)
            {
                result.bestSectorIoU[s] = result.sectorIoUMatrix[s, result.bestTIndex];
                result.bestSectorOverOcc[s] = sectorOverOccSumMatrix[s, result.bestTIndex] / divisor;
                result.bestSectorUnderOcc[s] = sectorUnderOccSumMatrix[s, result.bestTIndex] / divisor;
            }

            // 6. 8セクター独立での最適閾値 T_loc,s* および最大 IoU の算出
            float sectorIndependentIoUSum = 0f;
            for (int s = 0; s < 8; s++)
            {
                float bestSIoU = -1f;
                int bestSTI = 0;
                for (int ti = 0; ti < stepCount; ti++)
                {
                    if (result.sectorIoUMatrix[s, ti] > bestSIoU)
                    {
                        bestSIoU = result.sectorIoUMatrix[s, ti];
                        bestSTI = ti;
                    }
                }
                result.bestSectorIndependentTLoc[s] = tLocValues[bestSTI];
                result.bestSectorIndependentIoU[s] = bestSIoU;
                sectorIndependentIoUSum += bestSIoU;
            }
            result.bestSectorIndependentMeanIoU = sectorIndependentIoUSum / 8f;

            AppLogger.Log(controller,
                $"[TLocOpt] 最適化完了: 全体共通最適 T_loc = {result.bestTLoc:F2} (IoU: {result.bestMeanIoU:P2}), 8方向独立平均 IoU = {result.bestSectorIndependentMeanIoU:P2}",
                Tag);

            onCompleted?.Invoke(result);
        }
    }
}

