using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 天候演出システムにおける共有空間バウンズ（X, Z 平面）の解決および、
    /// 各サブプロセッサへの設定データモデルの一括分配を担当する Pure C# ディスパッチャークラス。
    /// </summary>
    public static class WeatherConfigDispatcher
    {
        /// <summary>
        /// 共有空間設定と各サブシステム設定を各プロセッサに分配・適用します。
        /// </summary>
        public static void DispatchAllSettings(
            bool useSharedBounds,
            Vector2 boundsCenter,
            Vector2 boundsSize,
            bool followViewer,
            float cloudY,
            float groundY,
            WeatherRainSettings rainSettings,
            WeatherCloudSettings cloudSettings,
            WeatherLightningSettings lightningSettings,
            WeatherLightingSettings lightingSettings,
            WeatherAudioSettings audioSettings,
            WeatherInputSettings inputSettings,
            WeatherRainProcessor rainProcessor,
            WeatherCloudProcessor cloudProcessor,
            WeatherLightningProcessor lightningProcessor,
            WeatherLightingProcessor lightingProcessor,
            WeatherAudioSynthesizer audioSynthesizer,
            WeatherHcdInputBridge inputBridge,
            WeatherKeyController keyController)
        {
            Vector2? sharedCenter = useSharedBounds ? boundsCenter : (Vector2?)null;
            Vector2? sharedSize = useSharedBounds ? boundsSize : (Vector2?)null;
            bool? sharedFollow = useSharedBounds ? followViewer : (bool?)null;

            rainProcessor?.ApplySettings(rainSettings, cloudY, groundY, sharedCenter, sharedSize, sharedFollow);
            cloudProcessor?.ApplySettings(cloudSettings, cloudY, sharedCenter, sharedSize, sharedFollow);
            lightningProcessor?.ApplySettings(lightningSettings, sharedCenter, sharedSize, sharedFollow);
            lightingProcessor?.ApplySettings(lightingSettings);
            audioSynthesizer?.ApplySettings(audioSettings);
            inputBridge?.ApplySettings(inputSettings);
            keyController?.ApplySettings(inputSettings);
        }
    }
}
