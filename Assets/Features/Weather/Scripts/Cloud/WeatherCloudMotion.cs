using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 雲（Cloud）の視点追従基準位置およびスケール連動サイン波浮遊（Drift）運動計算を担当する Pure C# クラス。
    /// 完全 Zero-GC で動作します。
    /// </summary>
    public class WeatherCloudMotion
    {
        private Vector3 _basePosition;
        private float _cloudY = 2.4f;
        private Vector2 _areaCenter = Vector2.zero;
        private bool _followViewer = true;
        private float _driftSpeed = 0.05f;

        public Vector3 BasePosition => _basePosition;
        public float CloudY { get => _cloudY; set => _cloudY = value; }
        public Vector2 AreaCenter { get => _areaCenter; set => _areaCenter = value; }
        public bool FollowViewer { get => _followViewer; set => _followViewer = value; }
        public float DriftSpeed { get => _driftSpeed; set => _driftSpeed = value; }

        /// <summary>
        /// 視点位置とエリア中心、雲の高さに基づいて基準ワールド座標を更新します。
        /// </summary>
        public void UpdateBasePosition(Vector3 viewerPos)
        {
            if (_followViewer)
            {
                _basePosition = new Vector3(viewerPos.x + _areaCenter.x, _cloudY, viewerPos.z + _areaCenter.y);
            }
            else
            {
                _basePosition = new Vector3(_areaCenter.x, _cloudY, _areaCenter.y);
            }
        }

        /// <summary>
        /// 固定ワールド座標での基準位置を更新します。
        /// </summary>
        public void ResetToFixedPosition()
        {
            _basePosition = new Vector3(_areaCenter.x, _cloudY, _areaCenter.y);
        }

        /// <summary>
        /// 現在時刻とスケール倍率に応じた浮遊ワールド座標を算出します。
        /// スケールが極小（0.1〜0.2m）の場合でも振幅が自動スケーリングされ、破綻しません。
        /// </summary>
        public Vector3 EvaluatePosition(float currentTime, float scaleMul)
        {
            if (_driftSpeed <= 0f) return _basePosition;

            float time = currentTime * _driftSpeed;
            float bobFactor = Mathf.Clamp(scaleMul, 0.05f, 1.0f);

            float offsetY = Mathf.Sin(time * 2.0f) * (0.015f * bobFactor);
            float offsetX = Mathf.Cos(time * 1.3f) * (0.02f * bobFactor);
            float offsetZ = Mathf.Sin(time * 0.9f) * (0.02f * bobFactor);

            return _basePosition + new Vector3(offsetX, offsetY, offsetZ);
        }
    }
}
