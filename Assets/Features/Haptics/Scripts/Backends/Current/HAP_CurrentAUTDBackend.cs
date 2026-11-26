#if AUTD3_SDK_CURRENT
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using AUTD3;
using Core.Logging;
using Features.Haptics.Core;
using Features.Haptics.Debug;
using Features.Haptics.Processors;

#nullable enable

namespace Features.Haptics.Backends.Current
{
    /// <summary>
    /// autd3-sdk (v0.14.1 / Current SDK) を用いた具象バックエンドファサード。
    /// 接続セッション管理・ハードウェア設定・パターン構築・フレーム送信・送信統計の各専任クラスを統合します。
    /// </summary>
    public class HAP_CurrentAUTDBackend : IHAP_AUTDBackend
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoRegister()
        {
            HAP_AUTDBackendRegistry.RegisterBackend("Current (autd3-sdk 0.14.1)", () => new HAP_CurrentAUTDBackend());
        }

        public string BackendName => "Current (autd3-sdk 0.14.1)";

        private static readonly TimeSpan ConfigCommandTimeout = TimeSpan.FromSeconds(2);

        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly HAP_CurrentPatternBuilder _patternBuilder = new HAP_CurrentPatternBuilder();
        private readonly HAP_CurrentHardwareConfigurator _hardwareConfigurator = new HAP_CurrentHardwareConfigurator();
        private readonly HAP_CurrentSendDiagnostics _diagnostics = new HAP_CurrentSendDiagnostics();
        private readonly HAP_CurrentConnectionSession _session;
        private readonly HAP_CurrentFociSender _fociSender;

        public bool IsConnected => _session.IsConnected;

        public HAP_CurrentAUTDBackend()
        {
            _session = new HAP_CurrentConnectionSession(_sendLock, _hardwareConfigurator);
            _fociSender = new HAP_CurrentFociSender(
                _sendLock,
                _patternBuilder,
                _diagnostics,
                _session.CreateTimeoutTokenSource);
        }

        public Task OpenAsync(AUTDLinkType linkType, string linkTarget, IReadOnlyList<AUTD3Device> devices)
            => _session.OpenAsync(linkType, linkTarget, devices);

        public Task CloseAsync()
            => _session.CloseAsync();

        public void SendNull()
        {
            EnqueueExclusiveCommand("SendNull", HAP_LogTriggers.TagLinkService, "Error in SendNull",
                (client, geometry, token) => _hardwareConfigurator.SendNullAsync(client, geometry, token));
        }

        public void ApplyModulation(ModulationMode mode, float sineFreq, float staticAmp)
        {
            if (_hardwareConfigurator.IsModulationUnchanged(mode, sineFreq, staticAmp)) return;
            EnqueueExclusiveCommand("ApplyModulation", HAP_LogTriggers.TagModulationService, "Failed to apply modulation",
                (client, geometry, token) => _hardwareConfigurator.ApplyModulationAsync(client, geometry, mode, sineFreq, staticAmp, token));
        }

        public void ApplySilencer(SilencerMode mode, ushort stepPhase, ushort stepAmp)
        {
            if (_hardwareConfigurator.IsSilencerUnchanged(mode, stepPhase, stepAmp)) return;
            EnqueueExclusiveCommand("ApplySilencer", HAP_LogTriggers.TagModulationService, "Failed to apply silencer",
                (client, geometry, token) => _hardwareConfigurator.ApplySilencerAsync(client, geometry, mode, stepPhase, stepAmp, token));
        }

        public void ApplyFan(bool enableFan)
        {
            if (_hardwareConfigurator.IsFanUnchanged(enableFan)) return;
            EnqueueExclusiveCommand("ApplyFan", HAP_LogTriggers.TagModulationService, "Failed to apply fan",
                (client, geometry, token) => _hardwareConfigurator.ApplyFanAsync(client, geometry, enableFan, token));
        }

        public void ApplyTemperature(float temperature)
            => _hardwareConfigurator.ApplyTemperature(temperature);

        public void SendFoci(
            IReadOnlyList<HAP_FociGenerator.ClusterFociData> clusterData,
            IReadOnlyList<AUTD3Device> connectedDevices,
            HoloAlgorithm holoAlgorithm,
            bool enableDirectionalGrouping,
            float directionalAngleThreshold,
            float focusIntensityPascal,
            bool synchronousSend,
            HAP_AUTDPerformanceProfiler? profiler,
            HAP_AUTDDebugDisabler? debugDisabler = null,
            uint gspatRepeat = 20)
        {
            _fociSender.SendFoci(
                _session.Client,
                _session.Geometry,
                _hardwareConfigurator.SoundSpeed,
                clusterData,
                connectedDevices,
                holoAlgorithm,
                focusIntensityPascal,
                synchronousSend,
                profiler,
                debugDisabler,
                gspatRepeat);
        }

        public void SendGroupedSingleFocus(
            Vector3 position,
            float pascal,
            IReadOnlyList<int> activeDeviceIds,
            IReadOnlyList<AUTD3Device> connectedDevices)
        {
            // 送信中に呼び出し側のリストが書き換わっても安全なようにコピーしておく
            var ids = new List<int>(activeDeviceIds);
            var devices = new List<AUTD3Device>(connectedDevices);
            float soundSpeed = _hardwareConfigurator.SoundSpeed;

            EnqueueExclusiveCommand("SendGroupedSingleFocus", HAP_LogTriggers.TagLinkService, "Error in SendGroupedSingleFocus",
                async (client, geometry, token) =>
                {
                    using var built = _patternBuilder.BuildGroupedSingleFocus(geometry, soundSpeed, position, pascal, ids, devices);
                    await client.SendAsync(built.Command, token).ConfigureAwait(false);
                });
        }

        private void EnqueueExclusiveCommand(
            string holderName,
            string logTag,
            string errorPrefix,
            Func<Client, Geometry, CancellationToken, Task> action)
        {
            if (!_session.IsConnected) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _sendLock.WaitAsync().ConfigureAwait(false);
                    _diagnostics.MarkLockAcquired(holderName);
                    try
                    {
                        var client = _session.Client;
                        var geometry = _session.Geometry;
                        if (client == null || geometry == null) return;
                        using var cts = _session.CreateTimeoutTokenSource(ConfigCommandTimeout);
                        await action(client, geometry, cts.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        _diagnostics.MarkLockReleased();
                        _sendLock.Release();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning(null, logTag, $"[CurrentBackend] {errorPrefix}: {ex.Message}");
                }
            });
        }

        public void Dispose()
        {
            _session.Dispose();
            try { _sendLock.Dispose(); } catch { }
        }
    }
}
#endif
