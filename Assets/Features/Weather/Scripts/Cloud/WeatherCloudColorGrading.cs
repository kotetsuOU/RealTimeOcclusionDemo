using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 雨の激しさ (Intensity) に応じた雲の色グラデーションおよびボリュームスケール計算を行う Pure C# ユーティリティ。
    /// 完全 Zero-GC で動作します。
    /// </summary>
    public static class WeatherCloudColorGrading
    {
        /// <summary>
        /// 雨強度 (0.0: 穏やか/晴れ 〜 1.0: 激しい嵐) に応じた雲の基本色をグラデーション評価します。
        /// </summary>
        public static Color EvaluateColor(Color lightColor, Color stormColor, float intensity)
        {
            float t = Mathf.Clamp01(intensity);
            // S字曲線 (SmoothStep) で自然な天候悪化の階調変化を表現
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            return Color.Lerp(lightColor, stormColor, smoothT);
        }

        /// <summary>
        /// 雨強度に応じた雲の体積・カバレッジ倍率を評価します。
        /// </summary>
        public static float EvaluateScale(float minScale, float maxScale, float intensity)
        {
            float t = Mathf.Clamp01(intensity);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            return Mathf.Lerp(minScale, maxScale, smoothT);
        }
    }
}
