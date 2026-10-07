using System;
using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 天候の雨強度状態の推移・フェード補間を専門に管理する Pure C# コントローラークラス。
    /// WeatherManager から状態遷移計算の責務を分離し、GCフリーで決定論的な遷移計算を提供します。
    /// </summary>
    public class WeatherStateController
    {
        private float _currentIntensity;
        private float _targetIntensity;
        private float _fadeDuration = 2.0f;

        public event Action<float> OnIntensityChanged;

        public float CurrentIntensity => _currentIntensity;
        public float TargetIntensity => _targetIntensity;
        public bool IsRaining => _targetIntensity > 0f;
        public float FadeDuration
        {
            get => _fadeDuration;
            set => _fadeDuration = Mathf.Max(0.01f, value);
        }

        public WeatherStateController(float initialFadeDuration = 2.0f)
        {
            _fadeDuration = Mathf.Max(0.01f, initialFadeDuration);
        }

        public void SetTargetIntensity(float intensity)
        {
            _targetIntensity = Mathf.Clamp01(intensity);
        }

        public void ToggleRain()
        {
            SetTargetIntensity(_targetIntensity > 0f ? 0f : 1.0f);
        }

        public void Reset(float intensity = 0f)
        {
            _currentIntensity = Mathf.Clamp01(intensity);
            _targetIntensity = _currentIntensity;
            OnIntensityChanged?.Invoke(_currentIntensity);
        }

        /// <summary>
        /// 毎フレームのフェード線形補間を更新します。強度が変化した場合は true を返します。
        /// </summary>
        public bool Update(float deltaTime)
        {
            if (Mathf.Approximately(_currentIntensity, _targetIntensity))
            {
                return false;
            }

            float step = deltaTime / _fadeDuration;
            float prev = _currentIntensity;
            _currentIntensity = Mathf.MoveTowards(_currentIntensity, _targetIntensity, step);

            if (!Mathf.Approximately(prev, _currentIntensity))
            {
                OnIntensityChanged?.Invoke(_currentIntensity);
                return true;
            }

            return false;
        }
    }
}
