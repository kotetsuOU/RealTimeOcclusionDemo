#nullable enable
using System.Collections.Generic;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Config;
using Features.Haptics.Debug;

namespace Features.Haptics.Processors
{
    /// <summary>
    /// 抽出された触覚ターゲットから超音波焦点を生成し、AUTD3ハードウェアバックエンドへ送信・停止制御を行う実行者。
    /// プロファイリング計測と送信停止（Null 送信）のライフサイクルを一元管理します。
    /// </summary>
    public class HAP_AcousticPipelineExecutor
    {
        private const int PeriodicLogFrameInterval = 60;

        private readonly HAP_DebugFocusProcessor _debugFocusProcessor = new HAP_DebugFocusProcessor();
        private bool _isCurrentlyOff = true;
        public bool IsCurrentlyOff => _isCurrentlyOff;

        /// <summary>
        /// 触覚刺激パイプラインを実行し、焦点生成およびハードウェア送信を行います。
        /// </summary>
        public void Execute(
            bool hasActiveTargets,
            HapticsSourceMode sourceMode,
            List<TrackedCluster> activeClusters,
            List<HAP_FociGenerator.ClusterFociData> objectFociList,
            HAP_AUTDHardwareController? hardwareController,
            HAP_AUTDPerformanceProfiler profiler,
            HAP_AUTDDebugDisabler? debugDisabler,
            HAP_AcousticConfig acousticConfig,
            HAP_STMConfig stmConfig,
            HAP_ProfilingConfig profilingConfig,
            Vector3 offset,
            Object? logContext = null,
            HAP_DebugFocusConfig? debugFocusConfig = null)
        {
            if (hardwareController == null || !hardwareController.IsConnected) return;

            bool isDebugFocusActive = debugFocusConfig != null && debugFocusConfig.enableDebugFocus;

            if (isDebugFocusActive || hasActiveTargets)
            {
                profiler.BeginTotal();
                try
                {
                    profiler.BeginFociGenerate();
                    List<HAP_FociGenerator.ClusterFociData> clusterFociList;

                    if (isDebugFocusActive)
                    {
                        clusterFociList = _debugFocusProcessor.BuildSingleFocusTargets(debugFocusConfig!, offset, logContext);
                    }
                    else
                    {
                        _debugFocusProcessor.NotifyStopped();
                        if (sourceMode == HapticsSourceMode.ObjectTarget)
                        {
                            clusterFociList = objectFociList;
                        }
                        else if (sourceMode == HapticsSourceMode.AutoHCD)
                        {
                            clusterFociList = stmConfig.GenerateAutoHcdFoci(
                                activeClusters,
                                acousticConfig.focusIntensityPascal,
                                offset);
                        }
                        else
                        {
                            clusterFociList = new List<HAP_FociGenerator.ClusterFociData>();
                        }
                    }

                    profiler.EndFociGenerate();

                    HoloAlgorithm effectiveAlgorithm = (isDebugFocusActive || stmConfig.stmMode == HapticsSTMMode.FociSTM)
                        ? HoloAlgorithm.Naive
                        : acousticConfig.holoAlgorithm;
                    bool effectiveDirectionalGrouping = !isDebugFocusActive && acousticConfig.enableDirectionalGrouping;
                    float effectiveIntensityPa = isDebugFocusActive
                        ? debugFocusConfig!.GetEffectiveTargetPascal()
                        : acousticConfig.focusIntensityPascal;

                    if (isDebugFocusActive && debugFocusConfig!.forceSineModulation)
                    {
                        hardwareController.SetModulationOverride(ModulationMode.Sine, debugFocusConfig.debugSineFrequency);
                    }
                    else
                    {
                        hardwareController.ClearModulationOverride();
                    }

                    if (hardwareController.Backend != null && hardwareController.Backend.IsConnected)
                    {
                        if (_isCurrentlyOff)
                        {
                            // 出力停止状態からの復帰時: FPGAに確実に変調（StaticまたはSine）を再適用
                            hardwareController.ApplyModulation();
                            if (AppLogger.IsEnabled(logContext, HAP_LogTriggers.TagController))
                            {
                                AppLogger.Log(logContext, HAP_LogTriggers.TagController,
                                    $"[HAP_Pipeline] ResumeOutput -> ApplyModulation (t={Time.realtimeSinceStartup:F1}s, mode={(isDebugFocusActive ? "DebugFocus" : sourceMode.ToString())}, clusters={clusterFociList.Count})");
                            }
                        }

                        if (!isDebugFocusActive && AppLogger.IsEnabled(logContext, HAP_LogTriggers.TagController) && Time.frameCount % PeriodicLogFrameInterval == 0)
                        {
                            LogPeriodicPipelineSummary(logContext, sourceMode, effectiveAlgorithm, stmConfig, acousticConfig, clusterFociList);
                        }

                        hardwareController.Backend.SendFoci(
                            clusterFociList,
                            hardwareController.ConnectedDevices,
                            effectiveAlgorithm,
                            effectiveDirectionalGrouping,
                            acousticConfig.directionalAngleThreshold,
                            effectiveIntensityPa,
                            profilingConfig.synchronousSend,
                            profiler,
                            debugDisabler,
                            acousticConfig.gspatRepeatCount
                        );
                        _isCurrentlyOff = false;
                    }
                }
                finally
                {
                    profiler.EndTotal();
                }
            }
            else
            {
                _debugFocusProcessor.NotifyStopped();
                StopOutput(hardwareController, logContext);
            }
        }

        /// <summary>
        /// ハードウェアへの出力を安全に停止（Null送信）します。
        /// </summary>
        public void StopOutput(HAP_AUTDHardwareController? hardwareController, Object? logContext = null)
        {
            if (hardwareController != null)
            {
                hardwareController.ClearModulationOverride();
            }

            if (!_isCurrentlyOff && hardwareController != null && hardwareController.IsConnected)
            {
                hardwareController.SetNull();
                _isCurrentlyOff = true;
                if (AppLogger.IsEnabled(logContext, HAP_LogTriggers.TagController))
                {
                    AppLogger.Log(logContext, HAP_LogTriggers.TagController,
                        $"[HAP_Pipeline] StopOutput -> SetNull (t={Time.realtimeSinceStartup:F1}s)");
                }
            }
        }

        /// <summary>
        /// 焦点数・振幅レンジ・変調設定などの出力サマリーを定期ログ出力します。
        /// </summary>
        private static void LogPeriodicPipelineSummary(
            Object? logContext,
            HapticsSourceMode sourceMode,
            HoloAlgorithm effectiveAlgorithm,
            HAP_STMConfig stmConfig,
            HAP_AcousticConfig acousticConfig,
            List<HAP_FociGenerator.ClusterFociData> clusterFociList)
        {
            int totalFoci = 0;
            int maxStmFrames = 0;
            float minAmp = float.MaxValue;
            float maxAmp = float.MinValue;

            for (int i = 0; i < clusterFociList.Count; i++)
            {
                var cluster = clusterFociList[i];
                if (cluster.SequentialFoci != null)
                {
                    totalFoci += cluster.SequentialFoci.Count;
                    for (int j = 0; j < cluster.SequentialFoci.Count; j++)
                    {
                        float amp = cluster.SequentialFoci[j].Pascal;
                        if (amp < minAmp) minAmp = amp;
                        if (amp > maxAmp) maxAmp = amp;
                    }
                }

                if (cluster.STMFrames != null && cluster.STMFrames.Count > maxStmFrames)
                {
                    maxStmFrames = cluster.STMFrames.Count;
                }
            }

            if (totalFoci == 0)
            {
                minAmp = 0f;
                maxAmp = 0f;
            }

            AppLogger.Log(logContext, HAP_LogTriggers.TagController,
                $"[HAP_Pipeline] t={Time.realtimeSinceStartup:F1}s | mode={sourceMode} | algo={effectiveAlgorithm} | " +
                $"stm={stmConfig.stmMode}({stmConfig.stmFrequency:F0}Hz, maxFrames={maxStmFrames}) | " +
                $"targetPa={acousticConfig.focusIntensityPascal:F0} | clusters={clusterFociList.Count} | " +
                $"foci={totalFoci} | ampRange=[{minAmp:F1}, {maxAmp:F1}]Pa");
        }
    }
}
