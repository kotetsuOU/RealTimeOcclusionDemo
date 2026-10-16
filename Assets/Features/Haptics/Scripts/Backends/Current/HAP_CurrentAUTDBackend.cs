#if AUTD3_SDK_CURRENT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly HAP_CurrentPatternBuilder _patternBuilder = new HAP_CurrentPatternBuilder();
        private readonly HAP_CurrentHardwareConfigurator _hardwareConfigurator = new HAP_CurrentHardwareConfigurator();

        public async Task OpenAsync(AUTDLinkType linkType, string soemAdapterName, IReadOnlyList<AUTD3Device> devices)
        {
            var orderedDevices = devices.OrderBy(obj => obj.ID).ToList();
            var sdkDevices = orderedDevices.Select(obj => new Autd3(
                obj.transform.position,
                Features.Haptics.Hardware.HAP_DeviceOrientationHelper.GetAdjustedRotation(obj)
            )).ToList();

            _geometry = new Geometry(sdkDevices);
            AppLogger.Log(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Connecting to AUTD3 via {linkType}. Found {sdkDevices.Count} devices (local Y-inversion applied uniformly).");

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

                var clientConfig = new ClientConfig(lowLatency: true, timeoutCycles: 100);
                _client = await Client.OpenAsync(_geometry, link, clientConfig);

                if (IsConnected)
                {
                    SendNull();
                    AppLogger.Log(null, HAP_LogTriggers.TagLinkService, "[CurrentBackend] Connected and initialized successfully.");
                }
            }
            catch (Exception ex)
            {
                if (_geometry != null)
                {
                    try { _geometry.Dispose(); } catch { }
                    _geometry = null;
                }
                _client = null;
                AppLogger.LogError(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Failed to connect: {ex.Message}");
            }
        }

        public async Task CloseAsync()
        {
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_client != null)
                {
                    try
                    {
                        // 1. 出力を確実に停止（Null 送信および StopAsync）
                        await _hardwareConfigurator.SendNullAsync(_client, _geometry).ConfigureAwait(false);
                        await _client.StopAsync().ConfigureAwait(false);

                        // 2. サイレンサーを無効化（初期状態へ復帰）
                        await _hardwareConfigurator.SendSilencerDisableAsync(_client, _geometry).ConfigureAwait(false);

                        // 3. 冷却ファンを確実に停止（ForceFan(false)）
                        await _hardwareConfigurator.SendFanStopAsync(_client, _geometry).ConfigureAwait(false);

                        // 4. クライアント通信をクローズ
                        await _client.CloseAsync().ConfigureAwait(false);
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
            finally
            {
                _sendLock.Release();
            }
        }

        public void SendNull()
        {
            if (_client == null || _geometry == null) return;
            _sendTask = Task.Run(async () =>
            {
                try
                {
                    await _sendLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (_client == null || _geometry == null) return;
                        await _hardwareConfigurator.SendNullAsync(_client, _geometry).ConfigureAwait(false);
                    }
                    finally
                    {
                        _sendLock.Release();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Error in SendNull: {ex.Message}");
                }
            });
        }

        public void ApplyModulation(ModulationMode mode, float sineFreq, float staticAmp)
        {
            if (_client == null || _geometry == null) return;
            Task.Run(async () =>
            {
                try
                {
                    await _sendLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (_client == null || _geometry == null) return;
                        await _hardwareConfigurator.ApplyModulationAsync(_client, _geometry, mode, sineFreq, staticAmp).ConfigureAwait(false);
                    }
                    finally
                    {
                        _sendLock.Release();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning(null, HAP_LogTriggers.TagModulationService, $"[CurrentBackend] Failed to apply modulation: {ex.Message}");
                }
            });
        }

        public void ApplySilencer(SilencerMode mode, ushort stepPhase, ushort stepAmp)
        {
            if (_client == null || _geometry == null) return;
            Task.Run(async () =>
            {
                try
                {
                    await _sendLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (_client == null || _geometry == null) return;
                        await _hardwareConfigurator.ApplySilencerAsync(_client, _geometry, mode, stepPhase, stepAmp).ConfigureAwait(false);
                    }
                    finally
                    {
                        _sendLock.Release();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning(null, HAP_LogTriggers.TagModulationService, $"[CurrentBackend] Failed to apply silencer: {ex.Message}");
                }
            });
        }

        public void ApplyFan(bool enableFan)
        {
            if (_client == null || _geometry == null) return;
            Task.Run(async () =>
            {
                try
                {
                    await _sendLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (_client == null || _geometry == null) return;
                        await _hardwareConfigurator.ApplyFanAsync(_client, _geometry, enableFan).ConfigureAwait(false);
                    }
                    finally
                    {
                        _sendLock.Release();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning(null, HAP_LogTriggers.TagModulationService, $"[CurrentBackend] Failed to apply fan: {ex.Message}");
                }
            });
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
                try
                {
                    _sendLock.Wait();
                    try
                    {
                        if (_client != null)
                        {
                            foreach (var frame in frames)
                            {
                                _client.SendCheckedAsync(frame).GetAwaiter().GetResult();
                            }
                        }
                    }
                    finally
                    {
                        _sendLock.Release();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Error in synchronous SendFoci: {ex.Message}");
                }
                finally
                {
                    profiler?.EndSend();
                }
            }
            else
            {
                // 非同期送信: 先行送信中（SendFoci または SendNull等）であればフレームドロップ（最新フレーム優先）
                if (_sendLock.Wait(0))
                {
                    _sendTask = Task.Run(async () =>
                    {
                        try
                        {
                            if (_client != null)
                            {
                                foreach (var frame in frames)
                                {
                                    await _client.SendCheckedAsync(frame).ConfigureAwait(false);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Error in SendFoci: {ex.Message}");
                        }
                        finally
                        {
                            _sendLock.Release();
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

            try
            {
                _sendLock.Dispose();
            }
            catch { }
        }
    }
}
#endif
