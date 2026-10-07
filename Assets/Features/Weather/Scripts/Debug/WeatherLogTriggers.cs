using System.Collections.Generic;
using UnityEngine;
using Core.Logging;

namespace Features.Weather
{
    /// <summary>
    /// Weather（天候演出）モジュール全体の AppLogManager 連動ヘルパー。
    /// [AppLoggable("Weather (WeatherEffect)")] 属性により、シーン内の天候関連ログを一元登録・管理します。
    /// </summary>
    [AppLoggable("Weather (WeatherEffect)")]
    public class WeatherLogTriggers : MonoBehaviour, IAppLoggable
    {
        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            AddSubTrigger(group, "[WeatherManager] Weather State", WeatherManager.TagWeatherManager, existingLabels);
            AddSubTrigger(group, "[WeatherCloud] Cloud State", WeatherCloudProcessor.TagCloud, existingLabels);
            AddSubTrigger(group, "[WeatherGesture] Gesture Detection", WeatherManager.TagWeatherGesture, existingLabels);
            AddSubTrigger(group, "[WeatherKeyController] Key Operations", WeatherKeyController.TagWeatherKey, existingLabels);
            AddSubTrigger(group, "[WeatherHcdInputBridge] Hand Tracking State", WeatherHcdInputBridge.TagHcdBridge, existingLabels);
            AddSubTrigger(group, "[WeatherFoxReactionHandler] Fox Weather Reaction", WeatherFoxReactionHandler.TagFoxReaction, existingLabels);
        }

        private void AddSubTrigger(LogCategoryGroup group, string label, string tag, HashSet<string> existing)
        {
            if (existing.Contains(label)) return;
            group.entries.Add(new LogInstanceEntry
            {
                label = label,
                tag = tag,
                target = this,
                enabled = true
            });
            existing.Add(label);
        }
    }
}
