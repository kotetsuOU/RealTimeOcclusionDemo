#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Features.Weather.Editor
{
    /// <summary>
    /// PlayMode 実行時における WeatherManager のクイックコントロール UI 描画を担当する Drawer クラス。
    /// 雨強度スライダー、天候プリセット、落雷発火、および雲トグルボタンを提供します。
    /// </summary>
    public static class WeatherPlayModeControlDrawer
    {
        public static void Draw(WeatherManager manager)
        {
            if (manager == null) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("⚡ PlayMode Quick Controls", EditorStyles.boldLabel);

                // リアルタイム雨スライダー
                float currentRain = manager.RainIntensity;
                float newRain = EditorGUILayout.Slider("Rain Intensity", currentRain, 0f, 1f);
                if (!Mathf.Approximately(currentRain, newRain))
                {
                    manager.SetRainIntensity(newRain);
                }

                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Toggle Rain (G)"))
                    {
                        manager.ToggleRain();
                    }
                    if (GUILayout.Button("Clear (0%)"))
                    {
                        manager.SetRainIntensity(0f);
                    }
                    if (GUILayout.Button("Light (30%)"))
                    {
                        manager.SetRainIntensity(0.3f);
                    }
                    if (GUILayout.Button("Heavy (70%)"))
                    {
                        manager.SetRainIntensity(0.7f);
                    }
                    if (GUILayout.Button("Storm (100%)"))
                    {
                        manager.SetRainIntensity(1f);
                    }
                }

                EditorGUILayout.Space(4);

                if (GUILayout.Button("⚡ Trigger Strike (B)", GUILayout.Height(28)))
                {
                    manager.TriggerStrike(null);
                }

                EditorGUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool cloudsActive = manager.EnableClouds;
                    string cloudBtnText = cloudsActive ? "☁️ Clouds: ON (Click to Disable)" : "☁️ Clouds: OFF (Click to Enable)";
                    GUI.backgroundColor = cloudsActive ? new Color(0.7f, 0.9f, 1.0f) : new Color(0.8f, 0.8f, 0.8f);
                    if (GUILayout.Button(cloudBtnText, GUILayout.Height(26)))
                    {
                        manager.EnableClouds = !cloudsActive;
                    }
                    GUI.backgroundColor = Color.white;
                }
            }
        }
    }
}
#endif
