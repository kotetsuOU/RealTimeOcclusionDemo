#if AUTD3_SDK_CURRENT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Core;
using Features.Haptics.Debug;
using Features.Haptics.Processors;
using AUTD3;
using AUTD3.Link;

#nullable enable

namespace Features.Haptics.Backends.Current
{
    /// <summary>
    /// autd3-sdk (v0.9.0 / Current SDK) を用いた具象バックエンド実装。
    /// 接続ライフサイクル管理とフレーム送信のオーケストレーションに特化したスリムなファサード。
    /// ハードウェア設定は <see cref="HAP_CurrentHardwareConfigurator"/>、
    /// パターン構築は <see cref="HAP_CurrentPatternBuilder"/> に委譲します。
    /// </summary>
    public class HAP_CurrentAUTDBackend : IHAP_AUTDBackend
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoRegister()
        {
            HAP_AUTDBackendRegistry.RegisterBackend("Current (autd3-sdk 0.9.0)", () => new HAP_CurrentAUTDBackend());
        }

        public string BackendName => "Current (autd3-sdk 0.9.0)";

        private Client? _client;
        private Geometry? _geometry;

        public bool IsConnected => _client != null && _geometry != null;

        private Task? _sendTask;
        private readonly HAP_CurrentPatternBuilder _patternBuilder = new HAP_CurrentPatternBuilder();
        private readonly HAP_CurrentHardwareConfigurator _hardwareConfigurator = new HAP_CurrentHardwareConfigurator();

        public async Task OpenAsync(AUTDLinkType linkType, string soemAdapterName, IReadOnlyList<AUTD3Device> devices)
        {
            var orderedDevices = devices.OrderBy(obj => obj.ID).ToList();
            var sdkDevices = orderedDevices.Select(obj => new Autd3(obj.transform.position, obj.transform.rotation)).ToList();

            _geometry = new Geometry(sdkDevices);
            AppLogger.Log(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Connecting to AUTD3 via {linkType}. Found {sdkDevices.Count} devices.");

            try
            {
                ILink link;
                switch (linkType)
                {
                    case AUTDLinkType.TwinCAT:
                        link = TwinCATLinkOption.Local();
                        break;
                    case AUTDLinkType.Simulator:
                        link = new RemoteLinkOption("127.0.0.1:8080");
                        break;
                    default:
                        AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Unsupported link type {linkType}, falling back to TwinCAT.");
                        link = TwinCATLinkOption.Local();
                        break;
                }

                _client = await Client.OpenAsync(_geometry, link, new ClientConfig());

                if (IsConnected)
                {
                    SendNull();
                    AppLogger.Log(null, HAP_LogTriggers.TagLinkService, "[CurrentBackend] Connected and initialized successfully.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Failed to connect: {ex.Message}");
            }
        }

        public async Task CloseAsync()
        {
            if (_client != null)
            {
                try
                {
                    // 1. 出力を確実に停止（Null 送信および StopAsync）
                    await _hardwareConfigurator.SendNullAsync(_client, _geometry);
                    await _client.StopAsync();

                    // 2. サイレンサーを無効化（初期状態へ復帰）
                    await _hardwareConfigurator.SendSilencerDisableAsync(_client, _geometry);

                    // 3. 冷却ファンを確実に停止（ForceFan(false)）
                    await _hardwareConfigurator.SendFanStopAsync(_client, _geometry);

                    // 4. クライアント通信をクローズ
                    await _client.CloseAsync();
                    _client.Dispose();
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Error during close: {ex.Message}");
                }
                finally
                {
                    _client = null;
                    _hardwareConfigurator.ResetCachedStates();
                }
            }

            if (_geometry != null)
            {
                _geometry.Dispose();
                _geometry = null;
            }

            AppLogger.Log(null, HAP_LogTriggers.TagLinkService, "[CurrentBackend] AUTD3 connection closed.");
        }

        public async void SendNull()
        {
            await _hardwareConfigurator.SendNullAsync(_client, _geometry);
        }

        public void ApplyModulation(ModulationMode mode, float sineFreq, float staticAmp)
            => _hardwareConfigurator.ApplyModulation(_client, _geometry, mode, sineFreq, staticAmp);

        public void ApplySilencer(SilencerMode mode, ushort stepPhase, ushort stepAmp)
            => _hardwareConfigurator.ApplySilencer(_client, _geometry, mode, stepPhase, stepAmp);

        public void ApplyFan(bool enableFan)
            => _hardwareConfigurator.ApplyFan(_client, _geometry, enableFan);

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
            if (_client == null || _geometry == null) return;

            profiler?.BeginDeviceAllocate();
            using var builder = _client.DatagramBuilder();
            _patternBuilder.BuildPatterns(
                builder,
                _geometry,
                _hardwareConfigurator.SoundSpeed,
                clusterData,
                holoAlgorithm,
                focusIntensityPascal,
                gspatRepeat);
            profiler?.EndDeviceAllocate();

            var frames = builder.Build();

            profiler?.BeginSend();
            if (synchronousSend)
            {
                foreach (var frame in frames)
                {
                    _client.SendCheckedAsync(frame).GetAwaiter().GetResult();
                }
                profiler?.EndSend();
            }
            else
            {
                if (_sendTask == null || _sendTask.IsCompleted)
                {
                    _sendTask = Task.Run(async () =>
                    {
                        try
                        {
                            foreach (var frame in frames)
                            {
                                await _client.SendCheckedAsync(frame);
                            }
                        }
                        finally
                        {
                            profiler?.EndSend();
                        }
                    });
                }
                else
                {
                    profiler?.EndSend();
                }
            }
        }

        public void Dispose()
        {
            if (_client != null)
            {
                try
                {
                    Task.Run(async () => await CloseAsync()).Wait(TimeSpan.FromSeconds(3));
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Error during dispose: {ex.Message}");
                }
                finally
                {
                    if (_client != null)
                    {
                        try { _client.Dispose(); } catch { }
                        _client = null;
                    }
                    _hardwareConfigurator.ResetCachedStates();
                }
            }

            if (_geometry != null)
            {
                _geometry.Dispose();
                _geometry = null;
            }
        }
    }
}
#endif
