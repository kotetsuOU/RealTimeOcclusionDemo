using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// WeatherLightningSettings からの設定パラメータ展開、共有空間バウンズの統合、
    /// 各サブコンポーネント（Planner, Sequencer, BoltPool）への一括同期、
    /// およびボルトプールの階層配線を担当する Pure C# クラス。
    /// </summary>
    public static class WeatherLightningConfigApplier
    {
        /// <summary>
        /// WeatherLightningSettings の一括設定を各コンポーネントに反映します。
        /// </summary>
        public static void ApplySettings(
            WeatherLightningSettings settings,
            Vector2? sharedCenter,
            Vector2? sharedSize,
            bool? sharedFollowViewer,
            WeatherLightningStrikePlanner planner,
            WeatherLightningSequencer sequencer,
            WeatherLightningBoltPool boltPool,
            ref Material lightningMaterial,
            ref bool isFixedBounds,
            ref bool followViewer,
            ref Vector2 strikeAreaCenter,
            ref Vector2 strikeAreaSize,
            ref float strikeMinRadius,
            ref float strikeMaxRadius,
            ref float strikeFov,
            ref bool requireLineOfSight,
            ref LayerMask environmentMask,
            ref float minStrikeInterval,
            ref float flashDuration,
            ref float fadeDuration,
            ref float trunkWidth)
        {
            if (settings == null) return;

            if (settings.useCustomBounds)
            {
                isFixedBounds = true;
                followViewer = false;
                strikeAreaCenter = settings.strikeAreaCenter;
                strikeAreaSize = settings.strikeAreaSize;
            }
            else
            {
                isFixedBounds = true;
                followViewer = sharedFollowViewer ?? false;
                strikeAreaCenter = sharedCenter ?? settings.strikeAreaCenter;
                strikeAreaSize = sharedSize ?? settings.strikeAreaSize;
            }

            strikeMinRadius = settings.minRadius;
            strikeMaxRadius = settings.maxRadius;
            strikeFov = settings.fov;
            requireLineOfSight = settings.requireLineOfSight;
            environmentMask = settings.environmentMask;
            minStrikeInterval = settings.minStrikeInterval;
            flashDuration = settings.flashDuration;
            fadeDuration = settings.fadeDuration;
            trunkWidth = settings.trunkWidth;

            SyncComponents(
                planner,
                sequencer,
                isFixedBounds,
                followViewer,
                strikeAreaCenter,
                strikeAreaSize,
                strikeMinRadius,
                strikeMaxRadius,
                strikeFov,
                requireLineOfSight,
                environmentMask,
                minStrikeInterval,
                flashDuration,
                fadeDuration);

            if (boltPool != null)
            {
                boltPool.SetWidth(trunkWidth);
                boltPool.SetColors(settings.coreColor, settings.glowColor);
                if (settings.material != null)
                {
                    boltPool.SetMaterial(settings.material);
                }
            }

            if (settings.material != null)
            {
                lightningMaterial = settings.material;
            }
        }

        /// <summary>
        /// Planner および Sequencer への個別設定を同期します。
        /// </summary>
        public static void SyncComponents(
            WeatherLightningStrikePlanner planner,
            WeatherLightningSequencer sequencer,
            bool isFixedBounds,
            bool followViewer,
            Vector2 strikeAreaCenter,
            Vector2 strikeAreaSize,
            float strikeMinRadius,
            float strikeMaxRadius,
            float strikeFov,
            bool requireLineOfSight,
            LayerMask environmentMask,
            float minStrikeInterval,
            float flashDuration,
            float fadeDuration)
        {
            if (planner != null)
            {
                var gp = planner.GroundPicker;
                gp.IsFixedBounds = isFixedBounds;
                gp.FollowViewer = followViewer;
                gp.StrikeAreaCenter = strikeAreaCenter;
                gp.StrikeAreaSize = strikeAreaSize;
                gp.StrikeMinRadius = strikeMinRadius;
                gp.StrikeMaxRadius = strikeMaxRadius;
                gp.StrikeFov = strikeFov;
                gp.RequireLineOfSight = requireLineOfSight;
                gp.EnvironmentMask = environmentMask;
                planner.MinStrikeInterval = minStrikeInterval;
            }

            if (sequencer != null)
            {
                sequencer.FlashDuration = flashDuration;
                sequencer.FadeDuration = fadeDuration;
            }
        }

        /// <summary>
        /// WeatherLightningBoltPool の存在を保証し、未配置なら子 GameObject として自動生成します。
        /// </summary>
        public static WeatherLightningBoltPool EnsureBoltPool(Transform root, WeatherLightningBoltPool existing)
        {
            if (existing != null) return existing;
            if (root == null) return null;

            existing = root.GetComponentInChildren<WeatherLightningBoltPool>();
            if (existing != null) return existing;

            var poolGo = new GameObject("LightningBoltPool");
            poolGo.transform.SetParent(root, false);
            return poolGo.AddComponent<WeatherLightningBoltPool>();
        }
    }
}
