using System;
using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 音声アセットファイルを使用せず、DSP (OnAudioFilterRead) によるプロシージャル合成で
    /// 雨音および雷鳴をリアルタイム生成する音響シンセサイザー。
    /// 落雷位置との距離に応じた音速遅延（空気伝播）や、近接度に応じた周波数成分（クラック音／遠雷ゴロゴロ音）を再現します。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class WeatherAudioSynthesizer : MonoBehaviour
    {
        [Header("Rain Audio Settings")]
        [SerializeField, Range(0f, 1f)] private float rainGain = 0.35f;
        [SerializeField] private float rainCutoff = 2500f;

        [Header("Thunder Audio Settings")]
        [SerializeField, Range(0f, 1f)] private float thunderGain = 0.85f;
        [Tooltip("距離による音速遅延の誇張倍率 (室内では実距離の遅延が短すぎるため適宜誇張します)")]
        [SerializeField] private float soundDistanceScale = 15f;

        public float RainVolume
        {
            get => _rainVolume;
            set => _rainVolume = Mathf.Clamp01(value);
        }

        /// <summary>
        /// WeatherManager の一元管理設定を適用します。
        /// </summary>
        public void ApplySettings(WeatherAudioSettings settings)
        {
            if (settings == null) return;

            rainGain = settings.rainGain;
            rainCutoff = settings.rainCutoff;
            thunderGain = settings.thunderGain;
            soundDistanceScale = settings.soundDistanceScale;
        }

        private volatile float _rainVolume;
        private readonly object _gate = new object();
        private readonly System.Random _rng = new System.Random();
        private int _sampleRate;

        // 雨 (オーディオスレッド)
        private float _rainLp;
        private float _rainVol;

        // 雷の予約 (メインスレッド -> オーディオスレッド)
        private bool _pendingThunder;
        private int _pendingDelaySamples;
        private float _pendingNearness;

        // 雷の再生状態 (オーディオスレッド)
        private bool _isThunderActive;
        private int _thunderDelayLeft;
        private int _thunderSampleIndex;
        private float _thunderNear;
        private float _thunderLp;
        private float _f1, _f2, _p1, _p2;

        private void Awake()
        {
            _sampleRate = AudioSettings.outputSampleRate;
            if (_sampleRate <= 0) _sampleRate = 48000;

            var src = GetComponent<AudioSource>();
            src.clip = AudioClip.Create("WeatherSilenceClip", _sampleRate, 1, _sampleRate, false);
            src.loop = true;
            src.spatialBlend = 0f;
            src.playOnAwake = false;
            src.Play();
        }

        /// <summary>
        /// 落雷音の再生を予約します。
        /// </summary>
        /// <param name="distance">リスナーからの距離 (m)</param>
        /// <param name="minRadius">最小半径 (最大近接とみなす距離)</param>
        /// <param name="maxRadius">最大半径 (遠雷とみなす距離)</param>
        public void PlayThunder(float distance, float minRadius = 1.0f, float maxRadius = 4.0f)
        {
            float delaySec = (distance * soundDistanceScale) / 343.0f;
            float nearness = 1.0f - Mathf.InverseLerp(minRadius, maxRadius, distance);

            lock (_gate)
            {
                _pendingDelaySamples = Mathf.Max(0, Mathf.RoundToInt(delaySec * _sampleRate));
                _pendingNearness = Mathf.Clamp01(nearness);
                _pendingThunder = true;
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_sampleRate <= 0) return;

            lock (_gate)
            {
                if (_pendingThunder)
                {
                    _pendingThunder = false;
                    _isThunderActive = true;
                    _thunderDelayLeft = _pendingDelaySamples;
                    _thunderNear = _pendingNearness;
                    _thunderSampleIndex = 0;
                    _thunderLp = 0f;
                    _f1 = 0.8f + (float)_rng.NextDouble() * 2.0f;
                    _f2 = 2.0f + (float)_rng.NextDouble() * 3.0f;
                    _p1 = (float)_rng.NextDouble() * 6.283185f;
                    _p2 = (float)_rng.NextDouble() * 6.283185f;
                }
            }

            float aRain = OnePoleCoeff(rainCutoff);
            float gRain = CompensateFilterGain(aRain);

            // 近いほど高域 (120Hz〜900Hz)
            float thunderFc = Mathf.Lerp(120f, 900f, _thunderNear);
            float aThunder = OnePoleCoeff(thunderFc);
            float gThunder = CompensateFilterGain(aThunder);
            float tau = Mathf.Lerp(2.5f, 1.2f, _thunderNear);

            float targetRain = _rainVolume * rainGain;
            float step = 1.0f / (0.05f * _sampleRate); // 50ms スムージング
            float invFs = 1.0f / _sampleRate;

            for (int i = 0; i < data.Length; i += channels)
            {
                _rainVol += Mathf.Clamp(targetRain - _rainVol, -step, step);
                float whiteNoise = NextNoise();

                _rainLp += aRain * (whiteNoise - _rainLp);
                float rainSample = (_rainLp * gRain * 0.8f + whiteNoise * 0.2f) * _rainVol;
                float soundOutput = rainSample;

                if (_isThunderActive)
                {
                    if (_thunderDelayLeft > 0)
                    {
                        _thunderDelayLeft--;
                    }
                    else
                    {
                        float t = _thunderSampleIndex * invFs;
                        float env = Mathf.Exp(-t / tau) * (1.0f - Mathf.Exp(-t / 0.02f));
                        float rumbleMod = 0.65f + 0.35f * Mathf.Sin(6.283f * _f1 * t + _p1) * Mathf.Sin(6.283f * _f2 * t + _p2);

                        float n = NextNoise();
                        _thunderLp += aThunder * (n - _thunderLp);
                        float rumble = _thunderLp * gThunder * env * rumbleMod;
                        float crack = n * _thunderNear * Mathf.Exp(-t / 0.05f);

                        soundOutput += thunderGain * (0.5f * rumble + 0.6f * crack);
                        _thunderSampleIndex++;

                        if (t > tau * 6.0f)
                        {
                            _isThunderActive = false;
                        }
                    }
                }

                soundOutput = Mathf.Clamp(soundOutput, -1.0f, 1.0f);
                for (int c = 0; c < channels; c++)
                {
                    data[i + c] = soundOutput;
                }
            }
        }

        private float NextNoise() => (float)(_rng.NextDouble() * 2.0 - 1.0);

        private float OnePoleCoeff(float fc) => 1.0f - Mathf.Exp(-2.0f * Mathf.PI * fc / _sampleRate);

        private static float CompensateFilterGain(float a) => Mathf.Sqrt(Mathf.Max(0.001f, (2.0f - a) / a));
    }
}
