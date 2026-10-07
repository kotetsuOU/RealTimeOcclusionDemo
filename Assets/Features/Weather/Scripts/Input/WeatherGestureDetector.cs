using System;
using System.Collections.Generic;
using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 手の重心移動履歴から、天候制御用ジェスチャー（雨トグル・落雷振り下ろし）を検出する純粋 C# 判定ロジック。
    /// 掌の法線推定を行わず重心の変位と速度のみを評価するため、点群の表裏あいまいさやノイズに対して堅牢に動作します。
    /// </summary>
    public class WeatherGestureDetector
    {
        public struct Sample
        {
            public float Time;
            public Vector3 Position;
        }

        // パラメータ
        public float RaiseOffset = 0.10f;      // 頭(HMD/視点)Yからの持ち上げオフセット (m)
        public float StillSpeed = 0.25f;       // 静止とみなす最大速度 (m/s)
        public float RainHoldDuration = 1.0f;  // 雨トグルに必要な静止保持時間 (s)

        public float SwingWindow = 0.25f;      // 振り下ろし判定時間幅 (s)
        public float SwingDrop = 0.30f;        // 振り下ろしに必要な最小下降距離 (m)
        public float SwingMinSpeed = -1.2f;    // 振り下ろし時の最低Y速度 (m/s)
        public float StrikeDistance = 2.5f;    // 落雷目標の前方距離 (m)
        public float Cooldown = 1.5f;          // 落雷トリガー後のクールダウン (s)

        public float LostTimeout = 0.25f;      // 追跡途絶とみなす時間 (s)
        public float JumpRejectDistance = 0.35f;// 瞬間移動を棄却する閾値 (m)

        public event Action OnRainToggleRequested;
        public event Action<Vector3> OnStrikeRequested; // 目標地点

        private readonly List<Sample> _history = new List<Sample>(32);
        private float _lastSeenTime = -999f;
        private float _holdTimer;
        private float _cooldownTimer;
        private bool _rainLatched;

        public bool IsTracked => (Time.time - _lastSeenTime <= LostTimeout) && _history.Count >= 2;
        public float HoldProgress => Mathf.Clamp01(_holdTimer / Mathf.Max(0.01f, RainHoldDuration));

        /// <summary>
        /// 新しい手の追跡座標を入力します。
        /// </summary>
        public void PushHandSample(Vector3 worldPos, float time)
        {
            if (_history.Count > 0)
            {
                var last = _history[_history.Count - 1];
                // 追跡復帰または急激なジャンプ時は履歴をリセット（誤検出防止）
                if (time - last.Time > LostTimeout || (worldPos - last.Position).magnitude > JumpRejectDistance)
                {
                    _history.Clear();
                }
                else if (time - last.Time < 1e-4f)
                {
                    _history.RemoveAt(_history.Count - 1);
                }
            }

            _history.Add(new Sample { Time = time, Position = worldPos });
            _lastSeenTime = time;

            // 0.6秒以上古い履歴を破棄
            while (_history.Count > 0 && time - _history[0].Time > 0.6f)
            {
                _history.RemoveAt(0);
            }
        }

        /// <summary>
        /// 毎フレーム呼び出してジェスチャーの状態遷移とイベント発火を評価します。
        /// </summary>
        public void Update(float deltaTime, Transform headTransform)
        {
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= deltaTime;
            }

            if (headTransform == null || !IsTracked)
            {
                _holdTimer = 0f;
                return;
            }

            Sample current = _history[_history.Count - 1];
            float headY = headTransform.position.y;
            Vector3 velocity = CalculateVelocity(0.1f);

            // 1. 雨トグル判定 (頭上で一定時間静止)
            bool isRaised = current.Position.y > (headY + RaiseOffset);
            bool isStill = velocity.magnitude < StillSpeed;

            if (!isRaised)
            {
                _holdTimer = 0f;
                _rainLatched = false;
            }
            else if (!_rainLatched)
            {
                _holdTimer = isStill ? (_holdTimer + deltaTime) : 0f;
                if (_holdTimer >= RainHoldDuration)
                {
                    _rainLatched = true;
                    _holdTimer = 0f;
                    OnRainToggleRequested?.Invoke();
                }
            }

            // 2. 雷振り下ろし判定
            if (_cooldownTimer <= 0f && DetectSwing(current, headY, velocity))
            {
                Vector3 dir = current.Position - headTransform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-4f)
                {
                    dir = headTransform.forward;
                    dir.y = 0f;
                }
                if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;

                Vector3 strikeTarget = headTransform.position + dir.normalized * StrikeDistance;
                _cooldownTimer = Cooldown;
                OnStrikeRequested?.Invoke(strikeTarget);
            }
        }

        private bool DetectSwing(Sample current, float headY, Vector3 velocity)
        {
            if (velocity.y > SwingMinSpeed) return false; // 下降中であること

            float maxY = float.NegativeInfinity;
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (current.Time - _history[i].Time > SwingWindow) break;
                maxY = Mathf.Max(maxY, _history[i].Position.y);
            }

            // 頭付近の高さから SwingDrop 以上を急降下したか
            return (maxY > headY + RaiseOffset - 0.1f) && (maxY - current.Position.y > SwingDrop);
        }

        private Vector3 CalculateVelocity(float windowDuration)
        {
            if (_history.Count < 2) return Vector3.zero;

            Sample last = _history[_history.Count - 1];
            Sample first = last;
            for (int i = _history.Count - 2; i >= 0; i--)
            {
                first = _history[i];
                if (last.Time - first.Time >= windowDuration) break;
            }

            float dt = last.Time - first.Time;
            return dt > 1e-3f ? (last.Position - first.Position) / dt : Vector3.zero;
        }

        public void Reset()
        {
            _history.Clear();
            _holdTimer = 0f;
            _cooldownTimer = 0f;
            _rainLatched = false;
        }
    }
}
