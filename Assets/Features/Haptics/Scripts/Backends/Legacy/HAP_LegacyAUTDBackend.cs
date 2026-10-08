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
            var sdkDevices = orderedDevices.Select(obj => new AUTD3Sharp.AUTD3(pos: obj.transform.position, rot: obj.transform.rotation)).ToList();

            AppLogger.Log(null, HAP_LogTriggers.TagLinkService, $"[LegacyBackend] Connecting to AUTD3. Found {sdkDevices.Count} devices.");

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

            var devList = connectedDevices.ToList();
            var clusterList = clusterData.ToList();

            profiler?.BeginDeviceAllocate();
            IDatagram datagram;

            if (devList.Count == 1)
            {
                if (debugDisabler != null && debugDisabler.IsDisabled(devList[0].ID))
                    datagram = new Null();
                else
                    datagram = GenerateDatagram(clusterList, holoAlgorithm, focusIntensityPascal);
            }
            else
            {
                bool hasExplicit = clusterList.Any(c => c.AssignedDeviceIndex >= 0 || (c.AssignedDeviceIndices != null && c.AssignedDeviceIndices.Count > 0));
                if (!hasExplicit && (!enableDirectionalGrouping || devList.Count == 0))
                {
                    if (debugDisabler != null && devList.Any(d => debugDisabler.IsDisabled(d.ID)))
                    {
                        datagram = BuildGroup(devList, devIdx =>
                        {
                            var dev = devList[devIdx];
                            if (debugDisabler.IsDisabled(dev.ID)) return new Null();
                            return GenerateDatagram(clusterList, holoAlgorithm, focusIntensityPascal);
                        });
                    }
                    else
                    {
                        datagram = GenerateDatagram(clusterList, holoAlgorithm, focusIntensityPascal);
                    }
                }
                else
                {
                    var assignments = HAP_DeviceGrouping.CalculateAssignments(
                        clusterList,
                        devList,
                        enableDirectionalGrouping,
                        directionalAngleThreshold,
                        debugDisabler);

                    datagram = BuildGroup(devList, devIdx =>
                    {
                        var dev = devList[devIdx];
                        if (debugDisabler != null && debugDisabler.IsDisabled(dev.ID))
                            return new Null();

                        if (assignments.TryGetValue(dev.ID, out var assigned) && assigned.Count > 0)
                            return GenerateDatagram(assigned, holoAlgorithm, focusIntensityPascal);

                        return new Null();
                    });
                }
            }
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

        private static (AUTD3Sharp.Utils.Point3, Amplitude) ToSdkFocus(HAP_FocusPoint f)
            => (new AUTD3Sharp.Utils.Point3(f.Position.x, f.Position.y, f.Position.z), f.Pascal * Pa);

        private static IDatagram GenerateDatagram(
            List<HAP_FociGenerator.ClusterFociData> clusterData,
            HoloAlgorithm holoAlgorithm,
            float focusIntensityPascal)
        {
            if (clusterData.Count == 0) return new Null();

            bool useSTM = clusterData.Any(c => c.UseSTM && c.STMFrames != null && c.STMFrames.Count > 1);
            if (useSTM)
            {
                int maxSamples = clusterData.Max(c => c.UseSTM ? c.STMFrames.Count : 1);
                float stmFreq = clusterData.First(c => c.UseSTM).STMFrequency;
                bool isGainStm = clusterData.Any(c => c.IsGainSTM);

                if (isGainStm && holoAlgorithm == HoloAlgorithm.GSPAT)
                {
                    var gains = new List<IGain>();
                    for (int i = 0; i < maxSamples; i++)
                    {
                        var activeFoci = new List<(AUTD3Sharp.Utils.Point3, Amplitude)>();
                        foreach (var cData in clusterData)
                        {
                            if (cData.UseSTM && i < cData.STMFrames.Count)
                            {
                                foreach (var p in cData.STMFrames[i])
                                {
                                    activeFoci.Add((new AUTD3Sharp.Utils.Point3(p.x, p.y, p.z), focusIntensityPascal * Pa));
                                }
                            }
                            else
                            {
                                foreach (var sf in cData.SequentialFoci)
                                {
                                    activeFoci.Add(ToSdkFocus(sf));
                                }
                            }
                        }
                        gains.Add(new GSPAT(activeFoci.ToArray(), new GSPATOption()));
                    }

                    return new GainSTM(gains, stmFreq * Hz, new GainSTMOption()).IntoNearest();
                }
                else
                {
                    var mergedFrames = new List<ControlPoints>();
                    byte intensityVal = (byte)Mathf.Clamp((focusIntensityPascal / 10000f) * 255f, 0, 255);
                    var intensity = new Intensity(intensityVal);

                    for (int i = 0; i < maxSamples; i++)
                    {
                        var points = new List<ControlPoint>();
                        foreach (var cData in clusterData)
                        {
                            if (cData.UseSTM && i < cData.STMFrames.Count)
                            {
                                foreach (var p in cData.STMFrames[i])
                                {
                                    points.Add(new ControlPoint(new AUTD3Sharp.Utils.Point3(p.x, p.y, p.z)));
                                }
                            }
                            else
                            {
                                foreach (var sf in cData.SequentialFoci)
                                {
                                    points.Add(new ControlPoint(ToSdkFocus(sf).Item1));
                                }
                            }
                        }
                        mergedFrames.Add(new ControlPoints(points.ToArray(), intensity));
                    }

                    return new FociSTM(mergedFrames, stmFreq * Hz).IntoNearest();
                }
            }
            else
            {
                var mergedFoci = new List<(AUTD3Sharp.Utils.Point3, Amplitude)>();
                foreach (var cData in clusterData)
                {
                    if (cData.SequentialFoci.Count > 0)
                    {
                        foreach (var sf in cData.SequentialFoci) mergedFoci.Add(ToSdkFocus(sf));
                    }
                    else if (cData.STMFrames != null && cData.STMFrames.Count > 0 && cData.STMFrames[0].Count > 0)
                    {
                        foreach (var p in cData.STMFrames[0])
                        {
                            mergedFoci.Add((new AUTD3Sharp.Utils.Point3(p.x, p.y, p.z), focusIntensityPascal * cData.Cluster.Force * Pa));
                        }
                    }
                }

                if (mergedFoci.Count == 0)
                    mergedFoci.Add((new AUTD3Sharp.Utils.Point3(0, 0, 0), 0f * Pa));
                if (mergedFoci.Count == 1)
                    mergedFoci.Add((mergedFoci[0].Item1, 0f * Pa));

                if (holoAlgorithm == HoloAlgorithm.GSPAT)
                    return new GSPAT(mergedFoci.ToArray(), new GSPATOption());
                else
                    return new Naive(mergedFoci.ToArray(), new NaiveOption());
            }
        }

        private static IDatagram BuildGroup(List<AUTD3Device> connectedDevices, Func<int, IDatagram> datagrams)
        {
            var groupDict = new GroupDictionary();
            for (int i = 0; i < connectedDevices.Count; i++)
            {
                groupDict.Add(i, datagrams(i));
            }

            int maxIdx = connectedDevices.Count;
            return new Group(dev =>
            {
                int idx = dev.Idx();
                if (idx < 0 || idx >= maxIdx)
                {
                    AppLogger.LogError(null, HAP_LogTriggers.TagController, $"[BuildGroup] idx={idx} is OUT OF RANGE [0,{maxIdx}). Skipping device.");
                    return null;
                }
                return (object)idx;
            }, groupDict);
        }

        public void Dispose()
        {
            if (_autd != null)
            {
                try
                {
                    _autd.Send(new Null());
                    _autd.Close();
                    _autd.Dispose();
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[LegacyBackend] Error during close: {ex.Message}");
                }
                finally
                {
                    _autd = null;
                    AppLogger.Log(null, HAP_LogTriggers.TagLinkService, "[LegacyBackend] AUTD3 connection closed.");
                }
            }
        }
    }
}
