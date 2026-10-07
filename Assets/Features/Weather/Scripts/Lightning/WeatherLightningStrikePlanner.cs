using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 落雷実行可否判定（最小インターバル・ビジー状態チェック）、着地点サンプリング、
    /// および雲の空中始点（SkyPoint）のスケール連動オフセット算出を担当する Pure C# クラス。
    /// 完全 Zero-GC で動作します。
    /// </summary>
    public class WeatherLightningStrikePlanner
    {
        private readonly WeatherLightningGroundPicker _groundPicker = new WeatherLightningGroundPicker();
        private float _lastStrikeTime = -999f;
        private float _minStrikeInterval = 1.0f;

        public WeatherLightningGroundPicker GroundPicker => _groundPicker;

        public float MinStrikeInterval
        {
            get => _minStrikeInterval;
            set => _minStrikeInterval = value;
        }

        public float LastStrikeTime => _lastStrikeTime;

        /// <summary>
        /// 現在時刻において落雷可能か判定します。
        /// </summary>
        public bool CanStrike(float currentTime, bool isBusy)
        {
            if (isBusy) return false;
            return (currentTime - _lastStrikeTime) >= _minStrikeInterval;
        }

        /// <summary>
        /// 落雷対象地点および雲の始点を計画・選定します。
        /// </summary>
        public bool TryPlanStrike(
            Transform viewer,
            Transform origin,
            float cloudY,
            float groundY,
            Vector3? targetOverride,
            Vector2 strikeAreaSize,
            float currentTime,
            out Vector3 skyPoint,
            out Vector3 targetGround)
        {
            skyPoint = Vector3.zero;
            targetGround = Vector3.zero;

            if (!_groundPicker.TryPickGround(viewer, origin, cloudY, groundY, targetOverride, out targetGround))
            {
                return false;
            }

            _lastStrikeTime = currentTime;

            // 雲の始点 (落雷エリアのスケールに合わせて小さなランダムオフセットを加える)
            float maxOffset = Mathf.Min(0.04f, strikeAreaSize.x * 0.1f);
            Vector2 randOffset = Random.insideUnitCircle * maxOffset;
            skyPoint = new Vector3(targetGround.x + randOffset.x, cloudY, targetGround.z + randOffset.y);

            return true;
        }

        public void Reset()
        {
            _lastStrikeTime = -999f;
        }
    }
}
