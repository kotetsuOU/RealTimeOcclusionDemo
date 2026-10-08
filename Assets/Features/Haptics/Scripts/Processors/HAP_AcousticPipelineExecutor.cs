#nullable enable
using System.Collections.Generic;
using UnityEngine;
using Features.Haptics.Config;

namespace Features.Haptics.Processors
{
    /// <summary>
    /// 抽出された触覚ターゲットから超音波焦点を生成し、AUTD3ハードウェアバックエンドへ送信・停止制御を行う実行者。
    /// プロファイリング計測と送信停止（Null 送信）のライフサイクルを一元管理します。
    /// </summary>
    public class HAP_AcousticPipelineExecutor
    {
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
            HAP_HCDFociSettings? hcdFociSettings,
            HAP_AUTDHardwareController? hardwareController,
            HAP_AUTDPerformanceProfiler profiler,
            HAP_AUTDDebugDisabler? debugDisabler,
            HAP_AcousticConfig acousticConfig,
            HAP_STMConfig stmConfig,
            HAP_ProfilingConfig profilingConfig,
            Vector3 offset)
        {
            if (hardwareController == null || !hardwareController.IsConnected) return;

            if (hasActiveTargets)
            {
                profiler.BeginTotal();
                try
                {
                    profiler.BeginFociGenerate();
                    List<HAP_FociGenerator.ClusterFociData> clusterFociList;

                    if (sourceMode == HapticsSourceMode.ObjectTarget)
                    {
                        clusterFociList = objectFociList;
                    }
                    else if (sourceMode == HapticsSourceMode.AutoHCD)
                    {
                        if (hcdFociSettings != null)
                        {
                            clusterFociList = hcdFociSettings.GenerateFoci(
                                activeClusters,
                                acousticConfig.focusIntensityPascal,
                                offset,
                                stmConfig.stmMode,
                                stmConfig.stmFrequency);
                        }
                        else
                        {
                            // フォールバック: 設定コンポーネントが見つからない場合は Simplified モードで計算
                            clusterFociList = HAP_FociGenerator.Generate(
                                activeClusters,
                                HapticsGenerationMode.Simplified,
                                new HAP_HapticsCentroidSource(),
                                new HAP_HapticsEllipseSource(),
                                new HAP_HapticsRandomSource(),
                                acousticConfig.focusIntensityPascal,
                                offset,
                                stmConfig.stmMode,
                                stmConfig.stmFrequency);
                        }
                    }
                    else
                    {
                        clusterFociList = new List<HAP_FociGenerator.ClusterFociData>();
                    }

                    profiler.EndFociGenerate();

                    HoloAlgorithm effectiveAlgorithm = (stmConfig.stmMode == HapticsSTMMode.FociSTM)
                        ? HoloAlgorithm.Naive
                        : acousticConfig.holoAlgorithm;

                    if (hardwareController.Backend != null && hardwareController.Backend.IsConnected)
                    {
                        hardwareController.Backend.SendFoci(
                            clusterFociList,
                            hardwareController.ConnectedDevices,
                            effectiveAlgorithm,
                            acousticConfig.enableDirectionalGrouping,
                            acousticConfig.directionalAngleThreshold,
                            acousticConfig.focusIntensityPascal,
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
                StopOutput(hardwareController);
            }
        }

        /// <summary>
        /// ハードウェアへの出力を安全に停止（Null送信）します。
        /// </summary>
        public void StopOutput(HAP_AUTDHardwareController? hardwareController)
        {
            if (!_isCurrentlyOff && hardwareController != null && hardwareController.IsConnected)
            {
                hardwareController.SetNull();
                _isCurrentlyOff = true;
            }
        }
    }
}
