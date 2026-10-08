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
using AUTD3.Holo;
using AUTD3.Link;
using static AUTD3.Units;

#nullable enable

namespace Features.Haptics.Backends.Current
{
    /// <summary>
    /// autd3-sdk (v0.9.0 / Current SDK) を用いた具象バックエンド実装。
    /// 非同期 Client 通信、DatagramBuilder、および GSPAT/FociSTM の出力生成と送信を担当します。
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

        private float _soundSpeed = 340f;
        private Task? _sendTask;

        // 設定値キャッシュ
        private ModulationMode _prevModMode;
        private float _prevSineFreq;
        private float _prevStaticAmp;

        private SilencerMode _prevSilencerMode;
        private ushort _prevSilStepPhase;
        private ushort _prevSilStepAmp;

        private bool _prevFanState;

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
                    SendNull();
                    await _client.StopAsync();
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
            if (_client == null || _geometry == null) return;
            try
            {
                using var builder = _client.DatagramBuilder();
                using var patterns = _geometry.PatternBuffer();
                Pattern.SetPhaseAndIntensity(Phase.Zero, Intensity.Min, patterns);
                builder.Push(new Pattern(patterns));

                using var frames = builder.Build();
                foreach (var frame in frames)
                {
                    await _client.SendCheckedAsync(frame);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Failed to send Null: {ex.Message}");
            }
        }

        public async void ApplyModulation(ModulationMode mode, float sineFreq, float staticAmp)
        {
            if (_client == null || _geometry == null) return;
            if (_prevModMode == mode && Mathf.Approximately(_prevSineFreq, sineFreq) && Mathf.Approximately(_prevStaticAmp, staticAmp)) return;

            try
            {
                using var builder = _client.DatagramBuilder();
                using var modulation = Modulation.ModulationBuffer();

                switch (mode)
                {
                    case ModulationMode.Sine:
                        Modulation.Sine(Nearest(sineFreq * Hz), new SineOption(), modulation);
                        break;
                    case ModulationMode.Static:
                        byte intensity = (byte)Mathf.Clamp(staticAmp * 255f, 0, 255);
                        Modulation.Constant(intensity, modulation);
                        break;
                }

                builder.Push(new Modulation(SamplingConfig.Freq4k, modulation));
                using var frames = builder.Build();
                foreach (var frame in frames)
                {
                    await _client.SendCheckedAsync(frame);
                }

                _prevModMode = mode;
                _prevSineFreq = sineFreq;
                _prevStaticAmp = staticAmp;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagModulationService, $"[CurrentBackend] Failed to apply modulation: {ex.Message}");
            }
        }

        public async void ApplySilencer(SilencerMode mode, ushort stepPhase, ushort stepAmp)
        {
            if (_client == null || _geometry == null) return;
            if (_prevSilencerMode == mode && _prevSilStepPhase == stepPhase && _prevSilStepAmp == stepAmp) return;

            try
            {
                using var builder = _client.DatagramBuilder();

                switch (mode)
                {
                    case SilencerMode.Disabled:
                        builder.Push(SetSilencer.Disable());
                        break;
                    case SilencerMode.FixedUpdateRate:
                        builder.Push(new SetSilencer(new FixedUpdateRate(stepAmp, stepPhase)));
                        break;
                    case SilencerMode.FixedCompletionTime:
                        builder.Push(new SetSilencer(new FixedCompletionTime()));
                        break;
                }

                using var frames = builder.Build();
                foreach (var frame in frames)
                {
                    await _client.SendCheckedAsync(frame);
                }

                _prevSilencerMode = mode;
                _prevSilStepPhase = stepPhase;
                _prevSilStepAmp = stepAmp;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagModulationService, $"[CurrentBackend] Failed to apply silencer: {ex.Message}");
            }
        }

        public async void ApplyFan(bool enableFan)
        {
            if (_client == null || _geometry == null) return;
            if (_prevFanState == enableFan) return;

            try
            {
                using var builder = _client.DatagramBuilder();
                builder.Push(new ForceFan(enableFan));
                using var frames = builder.Build();
                foreach (var frame in frames)
                {
                    await _client.SendCheckedAsync(frame);
                }

                _prevFanState = enableFan;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagModulationService, $"[CurrentBackend] Failed to apply fan: {ex.Message}");
            }
        }

        public void ApplyTemperature(float temperature)
        {
            // 温度 T から音速 c = 331.5 + 0.6 * T (m/s) を計算して保持
            _soundSpeed = 331.5f + 0.6f * temperature;
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
            if (_client == null || _geometry == null) return;

            profiler?.BeginDeviceAllocate();
            var devList = connectedDevices.ToList();
            var clusterList = clusterData.ToList();

            using var builder = _client.DatagramBuilder();
            var wavelength = Pattern.Wavelength(_soundSpeed * m / s);

            bool useSTM = clusterList.Any(c => c.UseSTM && c.STMFrames != null && c.STMFrames.Count > 1);

            if (useSTM)
            {
                int maxSamples = clusterList.Max(c => c.UseSTM ? c.STMFrames.Count : 1);
                float stmFreq = clusterList.First(c => c.UseSTM).STMFrequency;
                bool isGainStm = clusterList.Any(c => c.IsGainSTM);

                if (isGainStm && holoAlgorithm == HoloAlgorithm.GSPAT)
                {
                    var patternBuffers = new PatternBuffer[maxSamples];
                    var option = new GspatOption(repeat: gspatRepeat);

                    for (int i = 0; i < maxSamples; i++)
                    {
                        var activeFoci = new List<AmplitudeTarget>();
                        foreach (var cData in clusterList)
                        {
                            if (cData.UseSTM && i < cData.STMFrames.Count)
                            {
                                foreach (var p in cData.STMFrames[i])
                                {
                                    activeFoci.Add(new AmplitudeTarget(p, Amplitude.FromPascal(focusIntensityPascal)));
                                }
                            }
                            else
                            {
                                foreach (var sf in cData.SequentialFoci)
                                {
                                    activeFoci.Add(new AmplitudeTarget(sf.Position, Amplitude.FromPascal(sf.Pascal)));
                                }
                            }
                        }

                        patternBuffers[i] = _geometry.PatternBuffer();
                        Holo.Gspat(_geometry, activeFoci.ToArray(), wavelength, option, patternBuffers[i]);
                    }

                    builder.Push(new PatternStm(Nearest(stmFreq * Hz), patternBuffers));
                }
                else
                {
                    byte intensityVal = (byte)Mathf.Clamp((focusIntensityPascal / 10000f) * 255f, 0, 255);
                    var intensity = new Intensity(intensityVal);
                    var controlPointsList = new List<ControlPoints>();

                    for (int i = 0; i < maxSamples; i++)
                    {
                        var points = new List<AUTD3.ControlPoint>();
                        foreach (var cData in clusterList)
                        {
                            if (cData.UseSTM && i < cData.STMFrames.Count)
                            {
                                foreach (var p in cData.STMFrames[i])
                                {
                                    points.Add(new AUTD3.ControlPoint(p));
                                }
                            }
                            else
                            {
                                foreach (var sf in cData.SequentialFoci)
                                {
                                    points.Add(new AUTD3.ControlPoint(sf.Position));
                                }
                            }
                        }
                        controlPointsList.Add(new ControlPoints(points.ToArray(), intensity));
                    }

                    builder.Push(new FociStm(Nearest(stmFreq * Hz), controlPointsList.ToArray()));
                }
            }
            else
            {
                var mergedFoci = new List<AmplitudeTarget>();
                foreach (var cData in clusterList)
                {
                    if (cData.SequentialFoci.Count > 0)
                    {
                        foreach (var sf in cData.SequentialFoci)
                        {
                            mergedFoci.Add(new AmplitudeTarget(sf.Position, Amplitude.FromPascal(sf.Pascal)));
                        }
                    }
                    else if (cData.STMFrames != null && cData.STMFrames.Count > 0 && cData.STMFrames[0].Count > 0)
                    {
                        foreach (var p in cData.STMFrames[0])
                        {
                            mergedFoci.Add(new AmplitudeTarget(p, Amplitude.FromPascal(focusIntensityPascal * cData.Cluster.Force)));
                        }
                    }
                }

                if (mergedFoci.Count == 0)
                    mergedFoci.Add(new AmplitudeTarget(Vector3.zero, Amplitude.FromPascal(0f)));

                var patterns = _geometry.PatternBuffer();
                if (holoAlgorithm == HoloAlgorithm.GSPAT)
                {
                    Holo.Gspat(_geometry, mergedFoci.ToArray(), wavelength, new GspatOption(repeat: gspatRepeat), patterns);
                }
                else
                {
                    Holo.Naive(_geometry, mergedFoci.ToArray(), wavelength, new NaiveOption(), patterns);
                }

                builder.Push(new Pattern(patterns));
            }
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
                    SendNull();
                    _client.CloseAsync().GetAwaiter().GetResult();
                    _client.Dispose();
                }
                catch (Exception ex)
                {
                    AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Error during dispose: {ex.Message}");
                }
                finally
                {
                    _client = null;
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
