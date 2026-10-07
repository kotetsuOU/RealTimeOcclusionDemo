using System.Collections.Generic;
using UnityEngine;
using Core.Logging;
using Features.PhysicalResponse;

namespace Features.Weather
{
    /// <summary>
    /// 天候変化（落雷や降雨）に応じて、仮想オブジェクト（Fox等）のアニメーションや振る舞いを連携させるハンドラー。
    /// 現時点では将来的な拡張枠（スタブ）として設計されており、落雷イベントの受信とログ通知のみを行います。
    /// 将来的に特定のアニメーション（怯え、驚き、耳伏せ、雨宿り等）を実装する際は、本クラス内にロジックを追記してください。
    /// </summary>
    [AppLoggable("Weather (WeatherEffect)")]
    public class WeatherFoxReactionHandler : MonoBehaviour, IAppLoggable
    {
        public const string TagFoxReaction = "WeatherFoxReactionHandler";

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            const string label = "[WeatherFoxReactionHandler] Fox Weather Reaction";
            if (!existingLabels.Contains(label))
            {
                group.entries.Add(new LogInstanceEntry
                {
                    label = label,
                    tag = TagFoxReaction,
                    target = this,
                    enabled = true
                });
                existingLabels.Add(label);
            }
        }

        [Header("Target References")]
        [Tooltip("天候マネージャへの参照")]
        [SerializeField] private WeatherManager weatherManager;

        [Tooltip("仮想オブジェクト一元管理マネージャ (未指定時は自動検索)")]
        [SerializeField] private PR_VirtualObjectManager virtualObjectManager;

        [Tooltip("直接指定する場合の Fox Animator")]
        [SerializeField] private Animator foxAnimator;

        private void OnEnable()
        {
            if (weatherManager == null)
            {
                weatherManager = GetComponent<WeatherManager>() ?? FindFirstObjectByType<WeatherManager>();
            }

            if (virtualObjectManager == null)
            {
                virtualObjectManager = FindFirstObjectByType<PR_VirtualObjectManager>();
            }

            if (weatherManager != null)
            {
                weatherManager.OnLightningStrike += HandleLightningStrike;
                weatherManager.OnRainIntensityChanged += HandleRainIntensityChanged;
            }
        }

        private void OnDisable()
        {
            if (weatherManager != null)
            {
                weatherManager.OnLightningStrike -= HandleLightningStrike;
                weatherManager.OnRainIntensityChanged -= HandleRainIntensityChanged;
            }
        }

        /// <summary>
        /// 落雷発生時のリアクション処理（将来拡張用スタブ）。
        /// </summary>
        /// <param name="strikePosition">落雷着弾地点のワールド座標</param>
        private void HandleLightningStrike(Vector3 strikePosition)
        {
            AppLogger.Log(this, TagFoxReaction, $"[WeatherFoxReactionHandler] 落雷イベントを受信: 地点={strikePosition:F2}");

            var anim = GetActiveAnimator();
            if (anim == null) return;

            // --- 将来の拡張枠 (TODO: 驚き・怯えモーションのトリガー) ---
            // 例:
            // anim.SetTrigger("Surprise");
            // あるいは耳ボーンの急激な下がり制御など
        }

        /// <summary>
        /// 雨強度変化時のリアクション処理（将来拡張用スタブ）。
        /// </summary>
        /// <param name="rainIntensity">雨強度 (0.0 〜 1.0)</param>
        private void HandleRainIntensityChanged(float rainIntensity)
        {
            // 定期または大きな変化時のみログ
            if (AppLogger.IsEnabled(this, TagFoxReaction) && Time.frameCount % 180 == 0)
            {
                AppLogger.Log(this, TagFoxReaction, $"[WeatherFoxReactionHandler] 雨強度変化を受信: {rainIntensity:F2}");
            }

            var anim = GetActiveAnimator();
            if (anim == null) return;

            // --- 将来の拡張枠 (TODO: 豪雨時に身をすくめる、耳を垂らす、歩行速度低下など) ---
        }

        private Animator GetActiveAnimator()
        {
            if (foxAnimator != null) return foxAnimator;
            if (virtualObjectManager != null && virtualObjectManager.ActiveAnimator != null)
            {
                return virtualObjectManager.ActiveAnimator;
            }
            return null;
        }
    }
}
