#if AUTD3_SDK_CURRENT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AUTD3;
using Core.Logging;
using Features.Haptics.Debug;
using Features.Haptics.Processors;

#nullable enable

namespace Features.Haptics.Backends.Current
{
    /// <summary>
    /// Current SDK (autd3-sdk v0.14.1) における焦点パターンの同期／非同期送信実行を担当する専任クラス。
    /// 送信タイムアウト、ロック排他、およびバッファの安全な破棄を一元管理します。
    /// </summary>
    public sealed class HAP_CurrentFociSender
    {
        private const double MinFociSendIntervalMs = 20.0;
        private static readonly TimeSpan FociCommandTimeout = TimeSpan.FromMilliseconds(500);

        private readonly SemaphoreSlim _sendLock;
        private readonly HAP_CurrentPatternBuilder _patternBuilder;
        private readonly HAP_CurrentSendDiagnostics _diagnostics;
        private readonly Func<TimeSpan, CancellationTokenSource> _createTimeoutTokenSource;

        public HAP_CurrentFociSender(
            SemaphoreSlim sendLock,
            HAP_CurrentPatternBuilder patternBuilder,
            HAP_CurrentSendDiagnostics diagnostics,
            Func<TimeSpan, CancellationTokenSource> createTimeoutTokenSource)
        {
            _sendLock = sendLock;
            _patternBuilder = patternBuilder;
            _diagnostics = diagnostics;
            _createTimeoutTokenSource = createTimeoutTokenSource;
        }

        /// <summary>
        /// 焦点群を同期または非同期モードで AUTD3 デバイスへ送信します。
        /// </summary>
        public void SendFoci(
            Client? client,
            Geometry? geometry,
            float soundSpeed,
            IReadOnlyList<HAP_FociGenerator.ClusterFociData> clusterData,
            IReadOnlyList<AUTD3Device> connectedDevices,
            HoloAlgorithm holoAlgorithm,
            float focusIntensityPascal,
            bool synchronousSend,
            HAP_AUTDPerformanceProfiler? profiler,
            HAP_AUTDDebugDisabler? debugDisabler,
            uint gspatRepeat)
        {
            if (client == null || geometry == null) return;

            _diagnostics.RecordSendStatsIfNeeded(synchronousSend, clusterData.Count, connectedDevices.Count);

            if (synchronousSend)
            {
                SendFociSynchronous(client, geometry, soundSpeed, clusterData, connectedDevices, holoAlgorithm, focusIntensityPascal, profiler, debugDisabler, gspatRepeat);
            }
            else
            {
                SendFociAsynchronous(client, geometry, soundSpeed, clusterData, connectedDevices, holoAlgorithm, focusIntensityPascal, profiler, debugDisabler, gspatRepeat);
            }
        }

        private void SendFociSynchronous(
            Client client,
            Geometry geometry,
            float soundSpeed,
            IReadOnlyList<HAP_FociGenerator.ClusterFociData> clusterData,
            IReadOnlyList<AUTD3Device> connectedDevices,
            HoloAlgorithm holoAlgorithm,
            float focusIntensityPascal,
            HAP_AUTDPerformanceProfiler? profiler,
            HAP_AUTDDebugDisabler? debugDisabler,
            uint gspatRepeat)
        {
            HAP_CurrentPatternBuilder.BuiltCommand built = BuildPatternsProfiled(
                geometry, soundSpeed, clusterData, connectedDevices, holoAlgorithm, focusIntensityPascal, profiler, debugDisabler, gspatRepeat);

            profiler?.BeginSend();
            long sendStart = Stopwatch.GetTimestamp();
            try
            {
                using (built)
                {
                    _sendLock.Wait();
                    _diagnostics.MarkLockAcquired("SendFociSync");
                    try
                    {
                        using var cts = _createTimeoutTokenSource(FociCommandTimeout);
                        if (built.UseStreaming)
                        {
                            client.SendStreamingAsync(built.Command, cts.Token).GetAwaiter().GetResult();
                        }
                        else
                        {
                            client.SendAsync(built.Command, cts.Token).GetAwaiter().GetResult();
                        }
                        _diagnostics.RecordSent();
                    }
                    finally
                    {
                        _diagnostics.SetLastSendDurationMs((Stopwatch.GetTimestamp() - sendStart) * 1000.0 / Stopwatch.Frequency);
                        _diagnostics.MarkLockReleased();
                        _sendLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                _diagnostics.RecordError(ex.Message);
                AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Error in synchronous SendFoci: {ex.Message}");
            }
            finally
            {
                profiler?.EndSend();
            }
        }

        private void SendFociAsynchronous(
            Client client,
            Geometry geometry,
            float soundSpeed,
            IReadOnlyList<HAP_FociGenerator.ClusterFociData> clusterData,
            IReadOnlyList<AUTD3Device> connectedDevices,
            HoloAlgorithm holoAlgorithm,
            float focusIntensityPascal,
            HAP_AUTDPerformanceProfiler? profiler,
            HAP_AUTDDebugDisabler? debugDisabler,
            uint gspatRepeat)
        {
            if (!_sendLock.Wait(0))
            {
                _diagnostics.RecordSkippedLock();
                return;
            }

            if (!_diagnostics.TryConsumeSendSlot(MinFociSendIntervalMs))
            {
                _sendLock.Release();
                return;
            }

            _diagnostics.MarkLockAcquired("SendFociAsync");
            HAP_CurrentPatternBuilder.BuiltCommand? built = null;
            try
            {
                built = BuildPatternsProfiled(
                    geometry, soundSpeed, clusterData, connectedDevices, holoAlgorithm, focusIntensityPascal, profiler, debugDisabler, gspatRepeat);

                var capturedBuilt = built;
                profiler?.BeginSend();
                long sendStart = Stopwatch.GetTimestamp();

                _ = Task.Run(async () =>
                {
                    try
                    {
                        using (capturedBuilt)
                        {
                            using var cts = _createTimeoutTokenSource(FociCommandTimeout);
                            if (capturedBuilt.UseStreaming)
                            {
                                await client.SendStreamingAsync(capturedBuilt.Command, cts.Token).ConfigureAwait(false);
                            }
                            else
                            {
                                await client.SendAsync(capturedBuilt.Command, cts.Token).ConfigureAwait(false);
                            }
                            _diagnostics.RecordSent();
                        }
                    }
                    catch (Exception ex)
                    {
                        _diagnostics.RecordError(ex.Message);
                        AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Error in SendFoci: {ex.Message}");
                    }
                    finally
                    {
                        _diagnostics.SetLastSendDurationMs((Stopwatch.GetTimestamp() - sendStart) * 1000.0 / Stopwatch.Frequency);
                        _diagnostics.MarkLockReleased();
                        _sendLock.Release();
                        profiler?.EndSend();
                    }
                });
            }
            catch (Exception ex)
            {
                built?.Dispose();
                _diagnostics.MarkLockReleased();
                _sendLock.Release();
                profiler?.EndSend();
                _diagnostics.RecordError(ex.Message);
                AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Error preparing SendFoci: {ex.Message}");
            }
        }

        private HAP_CurrentPatternBuilder.BuiltCommand BuildPatternsProfiled(
            Geometry geometry,
            float soundSpeed,
            IReadOnlyList<HAP_FociGenerator.ClusterFociData> clusterData,
            IReadOnlyList<AUTD3Device> connectedDevices,
            HoloAlgorithm holoAlgorithm,
            float focusIntensityPascal,
            HAP_AUTDPerformanceProfiler? profiler,
            HAP_AUTDDebugDisabler? debugDisabler,
            uint gspatRepeat)
        {
            profiler?.BeginDeviceAllocate();
            try
            {
                return _patternBuilder.BuildPatterns(
                    geometry,
                    soundSpeed,
                    clusterData,
                    connectedDevices,
                    holoAlgorithm,
                    focusIntensityPascal,
                    debugDisabler,
                    gspatRepeat);
            }
            finally
            {
                profiler?.EndDeviceAllocate();
            }
        }
    }
}
#endif

