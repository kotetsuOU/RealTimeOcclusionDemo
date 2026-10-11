#if AUTD3_SDK_CURRENT
using System;
using System.Threading.Tasks;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Core;
using Features.Haptics.Debug;
using AUTD3;
using static AUTD3.Units;

#nullable enable

namespace Features.Haptics.Backends.Current
{
    /// <summary>
    /// autd3-sdk (v0.9.0 / Current SDK) におけるハードウェア設定（Modulation, Silencer, Fan, Temperature）
    /// の適用・キャッシュ管理、および安全シャットダウンコマンドの送信を担当する専任コンフィグレーター。
    /// </summary>
    public class HAP_CurrentHardwareConfigurator
    {
        private float _soundSpeed = 340f;
        public float SoundSpeed => _soundSpeed;

        // 設定値キャッシュ
        private ModulationMode _prevModMode;
        private float _prevSineFreq;
        private float _prevStaticAmp;

        private SilencerMode _prevSilencerMode;
        private ushort _prevSilStepPhase;
        private ushort _prevSilStepAmp;

        private bool _prevFanState;

        /// <summary>
        /// 環境温度 (℃) を反映し、音速 c = 331.5 + 0.6 * T (m/s) を計算・保持します。
        /// </summary>
        public void ApplyTemperature(float temperature)
        {
            _soundSpeed = 331.5f + 0.6f * temperature;
        }

        /// <summary>
        /// 振幅変調（Modulation）設定を送信します。
        /// </summary>
        public async void ApplyModulation(Client? client, Geometry? geometry, ModulationMode mode, float sineFreq, float staticAmp)
        {
            if (client == null || geometry == null) return;
            if (_prevModMode == mode && Mathf.Approximately(_prevSineFreq, sineFreq) && Mathf.Approximately(_prevStaticAmp, staticAmp)) return;

            try
            {
                using var builder = client.DatagramBuilder();
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
                    await client.SendCheckedAsync(frame);
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

        /// <summary>
        /// サイレンサー（Silencer）設定を送信します。
        /// </summary>
        public async void ApplySilencer(Client? client, Geometry? geometry, SilencerMode mode, ushort stepPhase, ushort stepAmp)
        {
            if (client == null || geometry == null) return;
            if (_prevSilencerMode == mode && _prevSilStepPhase == stepPhase && _prevSilStepAmp == stepAmp) return;

            try
            {
                using var builder = client.DatagramBuilder();

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
                    await client.SendCheckedAsync(frame);
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

        /// <summary>
        /// 冷却ファンの ON/OFF を送信します。
        /// </summary>
        public async void ApplyFan(Client? client, Geometry? geometry, bool enableFan)
        {
            if (client == null || geometry == null) return;
            if (_prevFanState == enableFan) return;

            try
            {
                using var builder = client.DatagramBuilder();
                builder.Push(new ForceFan(enableFan));
                using var frames = builder.Build();
                foreach (var frame in frames)
                {
                    await client.SendCheckedAsync(frame);
                }

                _prevFanState = enableFan;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagModulationService, $"[CurrentBackend] Failed to apply fan: {ex.Message}");
            }
        }

        /// <summary>
        /// 出力停止（Null パターン）を非同期送信します。
        /// </summary>
        public async Task SendNullAsync(Client? client, Geometry? geometry)
        {
            if (client == null || geometry == null) return;
            try
            {
                using var builder = client.DatagramBuilder();
                using var patterns = geometry.PatternBuffer();
                Pattern.SetPhaseAndIntensity(Phase.Zero, Intensity.Min, patterns);
                builder.Push(new Pattern(patterns));

                using var frames = builder.Build();
                foreach (var frame in frames)
                {
                    await client.SendCheckedAsync(frame);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[CurrentBackend] Failed to send Null: {ex.Message}");
            }
        }

        /// <summary>
        /// シャットダウン時にサイレンサーを無効化（初期状態へ復帰）します。
        /// </summary>
        public async Task SendSilencerDisableAsync(Client? client, Geometry? geometry)
        {
            if (client == null || geometry == null) return;
            try
            {
                using var builder = client.DatagramBuilder();
                builder.Push(SetSilencer.Disable());
                using var frames = builder.Build();
                foreach (var frame in frames)
                {
                    await client.SendCheckedAsync(frame);
                }
                _prevSilencerMode = SilencerMode.Disabled;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagModulationService, $"[CurrentBackend] Failed to disable silencer during shutdown: {ex.Message}");
            }
        }

        /// <summary>
        /// シャットダウン時に冷却ファンを停止します。
        /// </summary>
        public async Task SendFanStopAsync(Client? client, Geometry? geometry)
        {
            if (client == null || geometry == null) return;
            try
            {
                using var builder = client.DatagramBuilder();
                builder.Push(new ForceFan(false));
                using var frames = builder.Build();
                foreach (var frame in frames)
                {
                    await client.SendCheckedAsync(frame);
                }
                _prevFanState = false;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagModulationService, $"[CurrentBackend] Failed to stop fan during shutdown: {ex.Message}");
            }
        }

        /// <summary>
        /// キャッシュ設定値をリセットします。
        /// </summary>
        public void ResetCachedStates()
        {
            _prevModMode = (ModulationMode)(-1);
            _prevSineFreq = -1f;
            _prevStaticAmp = -1f;
            _prevSilencerMode = (SilencerMode)(-1);
            _prevSilStepPhase = 0;
            _prevSilStepAmp = 0;
            _prevFanState = false;
        }
    }
}
#endif
