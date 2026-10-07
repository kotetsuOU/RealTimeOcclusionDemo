using System;
using UnityEngine;
using Core.Logging;

namespace Features.Weather
{
    /// <summary>
    /// 雨フェード遷移、雨・雲の視点追従・強度伝達、環境音量調整、および
    /// 手ジェスチャー検知の毎フレームシミュレーションを統括する Pure C# コーディネータークラス。
    /// 完全 Zero-GC で動作します。
    /// </summary>
    public class WeatherSimulationCoordinator
    {
        private readonly WeatherStateController _stateController = new WeatherStateController();
        private readonly WeatherGestureDetector _gestureDetector = new WeatherGestureDetector();

        public WeatherStateController StateController => _stateController;
        public WeatherGestureDetector GestureDetector => _gestureDetector;

        public float CurrentIntensity => _stateController.CurrentIntensity;
        public float TargetIntensity => _stateController.TargetIntensity;
        public bool IsRaining => _stateController.IsRaining;

        public float FadeDuration
        {
            get => _stateController.FadeDuration;
            set => _stateController.FadeDuration = value;
        }

        public event Action<float> OnIntensityChanged
        {
            add => _stateController.OnIntensityChanged += value;
            remove => _stateController.OnIntensityChanged -= value;
        }

        public event Action OnRainToggleRequested
        {
            add => _gestureDetector.OnRainToggleRequested += value;
            remove => _gestureDetector.OnRainToggleRequested -= value;
        }

        public event Action<Vector3> OnStrikeRequested
        {
            add => _gestureDetector.OnStrikeRequested += value;
            remove => _gestureDetector.OnStrikeRequested -= value;
        }

        public void Initialize(float fadeDuration)
        {
            _stateController.FadeDuration = fadeDuration;
        }

        /// <summary>
        /// 毎フレームの天候シミュレーション（強度フェード、追従、強度同期、ジェスチャーサンプリング）を更新します。
        /// </summary>
        public void UpdateSimulation(
            float deltaTime,
            float currentTime,
            Transform viewer,
            WeatherRainProcessor rainProcessor,
            WeatherCloudProcessor cloudProcessor,
            WeatherAudioSynthesizer audioSynthesizer,
            WeatherHcdInputBridge inputBridge)
        {
            // 1. 強度フェード補間
            _stateController.Update(deltaTime);
            float current = _stateController.CurrentIntensity;

            // 2. 雨エミッター追従・強度反映
            if (rainProcessor != null)
            {
                if (viewer != null) rainProcessor.FollowViewer(viewer.position);
                rainProcessor.SetIntensity(current);
            }

            // 3. 雲追従・強度反映
            if (cloudProcessor != null)
            {
                if (viewer != null) cloudProcessor.FollowViewer(viewer.position);
                cloudProcessor.SetIntensity(current);
            }

            // 4. 環境音量
            if (audioSynthesizer != null)
            {
                audioSynthesizer.RainVolume = current;
            }

            // 5. 手ジェスチャー評価
            if (inputBridge != null && inputBridge.TryGetHandPosition(out Vector3 handPos))
            {
                _gestureDetector.PushHandSample(handPos, currentTime);
            }
            _gestureDetector.Update(deltaTime, viewer);
        }

        public void SetTargetIntensity(float intensity)
        {
            _stateController.SetTargetIntensity(intensity);
        }

        public void ToggleRain()
        {
            _stateController.ToggleRain();
        }

        public void Reset()
        {
            _gestureDetector.Reset();
        }
    }
}
