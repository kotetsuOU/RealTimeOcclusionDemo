using System;
using System.Collections.Generic;
using UnityEngine;
using Core.Logging;
using Core.Keyboard;

namespace Features.Weather
{
    /// <summary>
    /// 天候機能のデバッグ・評価用キーボードコントローラー。
    /// AppKeyboard / AppKeyboardManager (Core.Keyboard) を通じて一元管理されたキーバインドに従い動作します (パターン A)。
    /// キー割り当てや重複検出・変更は AppKeyboardManager の Inspector で一元制御されます。
    /// </summary>
    [AppLoggable("Weather (WeatherEffect)")]
    public class WeatherKeyController : MonoBehaviour, IAppLoggable
    {
        public const string TagWeatherKey = "WeatherKeyController";

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            const string label = "[WeatherKeyController] Key Operations";
            if (!existingLabels.Contains(label))
            {
                group.entries.Add(new LogInstanceEntry
                {
                    label = label,
                    tag = TagWeatherKey,
                    target = this,
                    enabled = true
                });
                existingLabels.Add(label);
            }
        }

        [Header("Target Manager")]
        [SerializeField] private WeatherManager weatherManager;

        /// <summary>
        /// 後方互換性メソッド: 従来の WeatherInputSettings からの適用呼び出しを安全に受け流します。
        /// キーバインド設定は AppKeyboardManager 側で一元管理されるため、このメソッドは互換性のために保持され何もしません。
        /// </summary>
        public void ApplySettings(WeatherInputSettings settings)
        {
            // AppKeyboardManager で一元管理されるため何もしない
        }

        private void Awake()
        {
            if (weatherManager == null)
            {
                weatherManager = GetComponent<WeatherManager>() ?? FindFirstObjectByType<WeatherManager>();
            }
        }

        private void Update()
        {
            if (weatherManager == null) return;

            // 雨トグル (デフォルト: Gキー)
            if (AppKeyboard.GetKeyDown(AppKeyAction.Weather_ToggleRain))
            {
                weatherManager.ToggleRain();
                AppLogger.Log(this, TagWeatherKey, $"[WeatherKeyController] 雨トグル -> {(weatherManager.IsRaining ? "ON" : "OFF")}");
            }

            // 雲トグル (デフォルト: Vキー / PCDのCキー重複を解消)
            if (AppKeyboard.GetKeyDown(AppKeyAction.Weather_ToggleCloud))
            {
                weatherManager.EnableClouds = !weatherManager.EnableClouds;
                AppLogger.Log(this, TagWeatherKey, $"[WeatherKeyController] 雲トグル -> {(weatherManager.EnableClouds ? "ON" : "OFF")}");
            }

            // 落雷トリガー (デフォルト: Bキー)
            if (AppKeyboard.GetKeyDown(AppKeyAction.Weather_Strike))
            {
                bool success = weatherManager.TriggerStrike(null);
                AppLogger.Log(this, TagWeatherKey, $"[WeatherKeyController] 落雷トリガー -> {(success ? "成功" : "ビジー/待機中")}");
            }

            // 晴れ (0%) (デフォルト: 7キー)
            if (AppKeyboard.GetKeyDown(AppKeyAction.Weather_RainPreset0))
            {
                weatherManager.SetRainIntensity(0f);
                AppLogger.Log(this, TagWeatherKey, "[WeatherKeyController] 晴れ (雨強度 0%)");
            }

            // 小雨 (30%) (デフォルト: 8キー)
            if (AppKeyboard.GetKeyDown(AppKeyAction.Weather_RainPreset30))
            {
                weatherManager.SetRainIntensity(0.3f);
                AppLogger.Log(this, TagWeatherKey, "[WeatherKeyController] 小雨 (雨強度 30%)");
            }

            // 強い雨 (70%) (デフォルト: 9キー)
            if (AppKeyboard.GetKeyDown(AppKeyAction.Weather_RainPreset70))
            {
                weatherManager.SetRainIntensity(0.7f);
                AppLogger.Log(this, TagWeatherKey, "[WeatherKeyController] 強い雨 (雨強度 70%)");
            }

            // 豪雨 (100%) (デフォルト: 0キー)
            if (AppKeyboard.GetKeyDown(AppKeyAction.Weather_RainPreset100))
            {
                weatherManager.SetRainIntensity(1.0f);
                AppLogger.Log(this, TagWeatherKey, "[WeatherKeyController] 豪雨 (雨強度 100%)");
            }
        }
    }
}
