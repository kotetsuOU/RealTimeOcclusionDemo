using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Core;
using Features.Haptics.Debug;
using Features.Haptics.Hardware;
using Features.Haptics.Processors;
using AUTD3Sharp;

#nullable enable

namespace Features.Haptics.Backends.Legacy
{
    /// <summary>
    /// AUTD3Sharp (v3.x / Legacy SDK) を用いた具象バックエンドファサード。
    /// 接続ライフサイクルおよび焦点送信オーケストレーションを担当し、
    /// ハードウェア設定とデータグラム生成を各専任クラスへ委譲します。
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
        private readonly HAP_LegacyHardwareConfigurator _hardwareConfigurator = new HAP_LegacyHardwareConfigurator();

        public Task OpenAsync(AUTDLinkType linkType, string linkTarget, IReadOnlyList<AUTD3Device> devices)
        {
            var orderedDevices = devices.OrderBy(obj => obj.ID).ToList();
            var sdkDevices = orderedDevices.Select(obj => new AUTD3Sharp.AUTD3(
                pos: obj.transform.position,
                rot: HAP_DeviceOrientationHelper.GetAdjustedRotation(obj)
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

                    case AUTDLinkType.LAN:
                        AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, "[LegacyBackend] LAN link is only supported in the new SDK (v0.14+). Falling back to TwinCAT.");
                        _autd = Controller.OpenWithOption(sdkDevices, new AUTD3Sharp.Link.TwinCAT(), option);
                        AppLogger.Log(null, HAP_LogTriggers.TagLinkService, "[LegacyBackend] Connected via TwinCAT (fallback from LAN).");
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

        public void SendNull() => _hardwareConfigurator.SendNull(_autd, _sendLock);

        public void ApplyModulation(ModulationMode mode, float sineFreq, float staticAmp)
            => _hardwareConfigurator.ApplyModulation(_autd, _sendLock, mode, sineFreq, staticAmp);

        public void ApplySilencer(SilencerMode mode, ushort stepPhase, ushort stepAmp)
            => _hardwareConfigurator.ApplySilencer(_autd, _sendLock, mode, stepPhase, stepAmp);

        public void ApplyFan(bool enableFan)
            => _hardwareConfigurator.ApplyFan(_autd, _sendLock, enableFan);

        public void ApplyTemperature(float temperature)
            => _hardwareConfigurator.ApplyTemperature(_autd, temperature);

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
                    _hardwareConfigurator.ShutdownHardware(_autd, _sendLock);
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[LegacyBackend] Error during close: {ex.Message}");
                }
                finally
                {
                    _autd = null;
                    _hardwareConfigurator.ResetCachedStates();
                    AppLogger.Log(null, HAP_LogTriggers.TagLinkService, "[LegacyBackend] AUTD3 connection closed.");
                }
            }
        }
    }
}
