using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Core;
using Features.Haptics.Debug;
using Features.Haptics.Processors;
using AUTD3Sharp;
using AUTD3Sharp.Driver.Datagram;
using AUTD3Sharp.Gain;
using AUTD3Sharp.Gain.Holo;
using AUTD3Sharp.Modulation;
using static AUTD3Sharp.Units;

#nullable enable

namespace Features.Haptics.Backends.Legacy
{
    /// <summary>
    /// AUTD3Sharp (v3.x / Legacy SDK) を用いた具象バックエンド実装。
    /// 通信接続、変調設定、サイレンサー、および GSPAT/FociSTM の出力生成と送信を担当します。
    /// </summary>
    public class HAP_LegacyAUTDBackend : IHAP_AUTDBackend
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoRegister()
        {
            HAP_AUTDBackendRegistry.RegisterBackend("Legacy (AUTD3Sharp)", () => new HAP_LegacyAUTDBackend());
        }

        public string BackendName => "Legacy (AUTD3Sharp)";

        private Controller? _autd;
        public Controller? Autd => _autd;
        public bool IsConnected => _autd != null;

        private readonly object _sendLock = new object();
        private readonly HAP_LegacyDatagramBuilder _datagramBuilder = new HAP_LegacyDatagramBuilder();

        // 設定値キャッシュ
        private ModulationMode _prevModMode;
        private float _prevSineFreq;
        private float _prevStaticAmp;

        private SilencerMode _prevSilencerMode;
        private ushort _prevSilStepPhase;
        private ushort _prevSilStepAmp;

        private bool _prevFanState;
        private float _prevTemperature;

        public Task OpenAsync(AUTDLinkType linkType, string soemAdapterName, IReadOnlyList<AUTD3Device> devices)
        {
            var orderedDevices = devices.OrderBy(obj => obj.ID).ToList();
            var sdkDevices = orderedDevices.Select(obj => new AUTD3Sharp.AUTD3(
                pos: obj.transform.position,
                rot: Features.Haptics.Hardware.HAP_DeviceOrientationHelper.GetAdjustedRotation(obj)
            )).ToList();

            AppLogger.Log(null, HAP_LogTriggers.TagLinkService, $"[LegacyBackend] Connecting to AUTD3. Found {sdkDevices.Count} devices (local Y-inversion applied uniformly).");

            try
            {
                var option = new SenderOption { Timeout = Duration.FromMillis(5000) };
                switch (linkType)
                {
                    case AUTDLinkType.TwinCAT:
                        _autd = Controller.OpenWithOption(sdkDevices, new AUTD3Sharp.Link.TwinCAT(), option);
                        AppLogger.Log(null, HAP_LogTriggers.TagLinkService, "[LegacyBackend] Connected via TwinCAT.");
                        break;

                    case AUTDLinkType.SOEM:
                        AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, "[LegacyBackend] SOEM link requires SOEM package.");
                        break;

                    case AUTDLinkType.Simulator:
                        var simLink = new AUTD3Sharp.Link.Remote(
                            new System.Net.IPEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 8080),
                            new AUTD3Sharp.Link.RemoteOption());
                        _autd = Controller.OpenWithOption(sdkDevices, simLink, option);
                        AppLogger.Log(null, HAP_LogTriggers.TagLinkService, "[LegacyBackend] Connected via Simulator.");
                        break;
                }

                if (IsConnected)
                {
                    SendNull();
                    AppLogger.Log(null, HAP_LogTriggers.TagLinkService, "[LegacyBackend] Initialization complete.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError(null, HAP_LogTriggers.TagLinkService, $"[LegacyBackend] Connection failed: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            Dispose();
            return Task.CompletedTask;
        }

        public void SendNull()
        {
            if (_autd == null) return;
            try
            {
                lock (_sendLock)
                {
                    _autd.Send(new Null());
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[LegacyBackend] Failed to send Null: {ex.Message}");
            }
        }

        public void ApplyModulation(ModulationMode mode, float sineFreq, float staticAmp)
        {
            if (_autd == null) return;
            if (_prevModMode == mode && Mathf.Approximately(_prevSineFreq, sineFreq) && Mathf.Approximately(_prevStaticAmp, staticAmp)) return;

            try
            {
                switch (mode)
                {
                    case ModulationMode.Sine:
                        lock (_sendLock) { _autd.Send(new Sine(freq: sineFreq * Hz, option: new SineOption())); }
                        break;
                    case ModulationMode.Static:
                        lock (_sendLock) { _autd.Send(new Static()); }
                        break;
                }

                _prevModMode = mode;
                _prevSineFreq = sineFreq;
                _prevStaticAmp = staticAmp;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagModulationService, $"[LegacyBackend] Failed to apply modulation: {ex.Message}");
            }
        }

        public void ApplySilencer(SilencerMode mode, ushort stepPhase, ushort stepAmp)
        {
            if (_autd == null) return;
            if (_prevSilencerMode == mode && _prevSilStepPhase == stepPhase && _prevSilStepAmp == stepAmp) return;

            try
            {
                switch (mode)
                {
                    case SilencerMode.Disabled:
                        lock (_sendLock) { _autd.Send(Silencer.Disable()); }
                        break;
                    case SilencerMode.FixedUpdateRate:
                        lock (_sendLock) { _autd.Send(new Silencer(new FixedUpdateRate { Intensity = stepAmp, Phase = stepPhase })); }
                        break;
                    case SilencerMode.FixedCompletionTime:
                        lock (_sendLock) { _autd.Send(new Silencer(new FixedCompletionTime())); }
                        break;
                }

                _prevSilencerMode = mode;
                _prevSilStepPhase = stepPhase;
                _prevSilStepAmp = stepAmp;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagModulationService, $"[LegacyBackend] Failed to apply silencer: {ex.Message}");
            }
        }

        public void ApplyFan(bool enableFan)
        {
            if (_autd == null) return;
            if (_prevFanState == enableFan) return;

            try
            {
                lock (_sendLock) { _autd.Send(new ForceFan(dev => enableFan)); }
                _prevFanState = enableFan;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagModulationService, $"[LegacyBackend] Failed to apply fan: {ex.Message}");
            }
        }

        public void ApplyTemperature(float temperature)
        {
            if (_autd == null) return;
            if (Mathf.Approximately(_prevTemperature, temperature)) return;

            try
            {
                _autd.Environment.SetSoundSpeedFromTemp(temperature);
                _prevTemperature = temperature;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagModulationService, $"[LegacyBackend] Failed to set temperature: {ex.Message}");
            }
        }

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
            if (_autd == null) return;

            profiler?.BeginDeviceAllocate();
            var datagram = _datagramBuilder.BuildDatagram(
                clusterData,
                connectedDevices,
                holoAlgorithm,
                enableDirectionalGrouping,
                directionalAngleThreshold,
                focusIntensityPascal,
                debugDisabler);
            profiler?.EndDeviceAllocate();

            profiler?.BeginSend();
            try
            {
                lock (_sendLock)
                {
                    _autd.Send(datagram);
                }
            }
            finally
            {
                profiler?.EndSend();
            }
        }


        public void Dispose()
        {
            if (_autd != null)
            {
                try
                {
                    lock (_sendLock)
                    {
                        // 1. 出力を確実に停止
                        _autd.Send(new Null());

                        // 2. サイレンサーを無効化（初期状態へ復帰）
                        _autd.Send(Silencer.Disable());

                        // 3. 冷却ファンを停止
                        _autd.Send(new ForceFan(dev => false));

                        // 4. クライアント通信切断
                        _autd.Close();
                        _autd.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[LegacyBackend] Error during close: {ex.Message}");
                }
                finally
                {
                    _autd = null;
                    ResetCachedStates();
                    AppLogger.Log(null, HAP_LogTriggers.TagLinkService, "[LegacyBackend] AUTD3 connection closed.");
                }
            }
        }

        private void ResetCachedStates()
        {
            _prevModMode = (ModulationMode)(-1);
            _prevSineFreq = -1f;
            _prevStaticAmp = -1f;
            _prevSilencerMode = (SilencerMode)(-1);
            _prevSilStepPhase = 0;
            _prevSilStepAmp = 0;
            _prevFanState = false;
            _prevTemperature = -999f;
        }
    }
}
