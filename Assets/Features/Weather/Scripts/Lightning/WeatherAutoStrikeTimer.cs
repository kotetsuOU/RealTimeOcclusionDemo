using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 嵐 (Storm) モード時におけるポアソン過程に基づく自動落雷タイマー判定を担当する Pure C# クラス。
    /// WeatherManager から落雷タイマー評価・乱数計算責務を分離します。
    /// </summary>
    public class WeatherAutoStrikeTimer
    {
        private float _nextStrikeTime = -1f;
        private bool _wasStormy;

        /// <summary>
        /// 現在の雨強度と設定に基づいて自動落雷が必要かを評価します。
        /// </summary>
        public bool Evaluate(float currentRainIntensity, WeatherLightningSettings settings, float currentTime)
        {
            if (settings == null || !settings.autoThunder)
            {
                _wasStormy = false;
                return false;
            }

            bool isStormy = currentRainIntensity >= settings.stormThreshold;
            if (isStormy && !_wasStormy)
            {
                _nextStrikeTime = currentTime + NextPoissonInterval(settings.meanStrikeInterval);
            }

            bool shouldStrike = false;
            if (isStormy && currentTime >= _nextStrikeTime)
            {
                shouldStrike = true;
                _nextStrikeTime = currentTime + NextPoissonInterval(settings.meanStrikeInterval);
            }

            _wasStormy = isStormy;
            return shouldStrike;
        }

        public void Reset()
        {
            _nextStrikeTime = -1f;
            _wasStormy = false;
        }

        public static float NextPoissonInterval(float meanInterval)
        {
            float u = Mathf.Max(1e-4f, Random.value);
            return Mathf.Max(1.0f, -meanInterval * Mathf.Log(u));
        }
    }
}
