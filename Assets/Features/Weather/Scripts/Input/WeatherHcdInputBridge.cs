using System.Collections.Generic;
using UnityEngine;
using Features.HapticsCollision;
using Core.Logging;

namespace Features.Weather
{
    /// <summary>
    /// HCD_Pipeline による GPU 点群クラスタ追跡結果から手の平クラスタの重心を取得し、
    /// WeatherGestureDetector へ座標を供給するブリッジコンポーネント。
    /// HCD が非アクティブな場合は、フォールバック用 Transform や手動指定座標を利用可能です。
    /// </summary>
    [AppLoggable("Weather (WeatherEffect)")]
    public class WeatherHcdInputBridge : MonoBehaviour, IWeatherHandPositionProvider, IAppLoggable
    {
        public const string TagHcdBridge = "WeatherHcdInputBridge";

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            const string label = "[WeatherHcdInputBridge] Hand Tracking State";
            if (!existingLabels.Contains(label))
            {
                group.entries.Add(new LogInstanceEntry
                {
                    label = label,
                    tag = TagHcdBridge,
                    target = this,
                    enabled = true
                });
                existingLabels.Add(label);
            }
        }

        [Header("Fallback / Debug")]
        [Tooltip("HCDクラスタが取得できない場合のフォールバック追跡対象 (VRコントローラーや手のTransform等)")]
        [SerializeField] private Transform fallbackHandTransform;

        [Tooltip("最小点数閾値 (ノイズクラスタを除外)")]
        [SerializeField] private int minClusterPoints = 15;

        /// <summary>
        /// WeatherManager の一元管理設定を適用します。
        /// </summary>
        public void ApplySettings(WeatherInputSettings settings)
        {
            if (settings == null) return;
            if (settings.fallbackHandTransform != null) fallbackHandTransform = settings.fallbackHandTransform;
            minClusterPoints = settings.minClusterPoints;
        }

        private Vector3 _currentPosition;
        private bool _isTracked;
        private float _lastTrackedTime = -999f;

        public bool IsTracked => _isTracked;

        public bool TryGetHandPosition(out Vector3 worldPosition)
        {
            worldPosition = _currentPosition;
            return _isTracked;
        }

        private void Update()
        {
            _isTracked = false;

            // 1. HCD_Pipeline から追跡クラスタを取得
            if (HCD_Pipeline.Instance != null)
            {
                var clusters = HCD_Pipeline.Instance.GetTrackedClusters();
                if (clusters != null && clusters.Count > 0)
                {
                    // 点数が十分で、最も有効なクラスタを選択
                    float maxWeight = -1f;
                    Vector3 bestCentroid = Vector3.zero;

                    for (int i = 0; i < clusters.Count; i++)
                    {
                        var cl = clusters[i];
                        if (cl.ContactCount >= minClusterPoints && cl.ContactCount > maxWeight)
                        {
                            maxWeight = cl.ContactCount;
                            bestCentroid = cl.Centroid;
                        }
                    }

                    if (maxWeight > 0)
                    {
                        _currentPosition = bestCentroid;
                        _isTracked = true;
                        _lastTrackedTime = Time.time;
                    }
                }
            }

            // 2. フォールバック
            if (!_isTracked && fallbackHandTransform != null)
            {
                _currentPosition = fallbackHandTransform.position;
                _isTracked = true;
                _lastTrackedTime = Time.time;
            }

            // 定期デバッグログ
            if (AppLogger.IsEnabled(this, TagHcdBridge) && Time.frameCount % 120 == 0)
            {
                AppLogger.Log(this, $"[WeatherHcdInputBridge] HandTracked={_isTracked}, Pos={_currentPosition:F2}");
            }
        }

        /// <summary>
        /// 生点群の配列から重心を計算して手動設定する互換用メソッド。
        /// </summary>
        public void SetPoints(IReadOnlyList<Vector3> points, Transform pointSpace = null)
        {
            if (points == null || points.Count < minClusterPoints) return;

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = pointSpace != null ? pointSpace.TransformPoint(points[i]) : points[i];
                sum += p;
            }

            _currentPosition = sum / points.Count;
            _isTracked = true;
            _lastTrackedTime = Time.time;
        }
    }
}
