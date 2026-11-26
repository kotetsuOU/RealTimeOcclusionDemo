using System;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Debug;
using AUTD3Sharp;
using AUTD3Sharp.Driver.Datagram;
using AUTD3Sharp.Modulation;
using static AUTD3Sharp.Units;

#nullable enable

namespace Features.Haptics.Backends.Legacy
{
    /// <summary>
    /// AUTD3Sharp (v3.x / Legacy SDK) におけるハードウェア設定（Modulation, Silencer, Fan, Temperature）
    /// のキャッシュ管理・送信、および安全シャットダウンコマンド送信を担当する専任クラス。
    /// </summary>
    public sealed class HAP_LegacyHardwareConfigurator
    {
        private ModulationMode _prevModMode;
        private float _prevSineFreq;
        private float _prevStaticAmp;

        private SilencerMode _prevSilencerMode;
        private ushort _prevSilStepPhase;
        private ushort _prevSilStepAmp;

        private bool _prevFanState;
        private float _prevTemperature;

        /// <summary>
        /// 出力停止（Null）コマンドを送信します。
        /// </summary>
        public void SendNull(Controller? autd, object sendLock)
        {
            if (autd == null) return;
            try
            {
                lock (sendLock)
                {
                    autd.Send(new Null());
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, $"[LegacyBackend] Failed to send Null: {ex.Message}");
            }
        }

        /// <summary>
        /// 変調設定を適用します（前回値と同一の場合はスキップ）。
        /// </summary>
        public void ApplyModulation(Controller? autd, object sendLock, ModulationMode mode, float sineFreq, float staticAmp)
        {
            if (autd == null) return;
            if (_prevModMode == mode && Mathf.Approximately(_prevSineFreq, sineFreq) && Mathf.Approximately(_prevStaticAmp, staticAmp)) return;

            try
            {
                switch (mode)
                {
                    case ModulationMode.Sine:
                        lock (sendLock) { autd.Send(new Sine(freq: sineFreq * Hz, option: new SineOption())); }
                        break;
                    case ModulationMode.Static:
                        lock (sendLock) { autd.Send(new Static()); }
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

        /// <summary>
        /// サイレンサー設定を適用します（前回値と同一の場合はスキップ）。
        /// </summary>
        public void ApplySilencer(Controller? autd, object sendLock, SilencerMode mode, ushort stepPhase, ushort stepAmp)
        {
            if (autd == null) return;
            if (_prevSilencerMode == mode && _prevSilStepPhase == stepPhase && _prevSilStepAmp == stepAmp) return;

            try
            {
                switch (mode)
                {
                    case SilencerMode.Disabled:
                        lock (sendLock) { autd.Send(Silencer.Disable()); }
                        break;
                    case SilencerMode.FixedUpdateRate:
                        lock (sendLock) { autd.Send(new Silencer(new FixedUpdateRate { Intensity = stepAmp, Phase = stepPhase })); }
                        break;
                    case SilencerMode.FixedCompletionTime:
                        lock (sendLock) { autd.Send(new Silencer(new FixedCompletionTime())); }
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

        /// <summary>
        /// 冷却ファンの ON/OFF を適用します（前回値と同一の場合はスキップ）。
        /// </summary>
        public void ApplyFan(Controller? autd, object sendLock, bool enableFan)
        {
            if (autd == null) return;
            if (_prevFanState == enableFan) return;

            try
            {
                lock (sendLock) { autd.Send(new ForceFan(dev => enableFan)); }
                _prevFanState = enableFan;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagModulationService, $"[LegacyBackend] Failed to apply fan: {ex.Message}");
            }
        }

        /// <summary>
        /// 環境温度 (℃) から音速を更新します（前回値と同一の場合はスキップ）。
        /// </summary>
        public void ApplyTemperature(Controller? autd, float temperature)
        {
            if (autd == null) return;
            if (Mathf.Approximately(_prevTemperature, temperature)) return;

            try
            {
                autd.Environment.SetSoundSpeedFromTemp(temperature);
                _prevTemperature = temperature;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagModulationService, $"[LegacyBackend] Failed to set temperature: {ex.Message}");
            }
        }

        /// <summary>
        /// 終了時の安全シャットダウン手順（Null 送信・サイレンサー無効化・ファン停止）を実行します。
        /// </summary>
        public void ShutdownHardware(Controller autd, object sendLock)
        {
            lock (sendLock)
            {
                autd.Send(new Null());
                autd.Send(Silencer.Disable());
                autd.Send(new ForceFan(dev => false));
                autd.Close();
                autd.Dispose();
            }
        }

        /// <summary>
        /// キャッシュされている全設定状態をリセットします。
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
            _prevTemperature = -999f;
        }
    }
}

