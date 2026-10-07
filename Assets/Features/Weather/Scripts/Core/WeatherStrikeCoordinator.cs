using System;
using UnityEngine;
using Core.Logging;

namespace Features.Weather
{
    /// <summary>
    /// 落雷演出、自動落雷タイマー、閃光ライティング、および立体音響（雷鳴）の
    /// イベント調停・連携を担当する Pure C# コーディネータークラス。
    /// </summary>
    public class WeatherStrikeCoordinator
    {
        private readonly WeatherAutoStrikeTimer _autoStrikeTimer = new WeatherAutoStrikeTimer();

        public event Action<Vector3> OnLightningStrike;

        /// <summary>
        /// 落雷イベントリスナーをサブプロセッサに購読します。
        /// </summary>
        public void BindEvents(WeatherLightningProcessor lightningProcessor, Action<Vector3> onStarted, Action onEnded)
        {
            if (lightningProcessor == null) return;
            lightningProcessor.OnStrikeStarted -= onStarted;
            lightningProcessor.OnStrikeStarted += onStarted;
            lightningProcessor.OnStrikeEnded -= onEnded;
            lightningProcessor.OnStrikeEnded += onEnded;
        }

        /// <summary>
        /// 落雷イベントリスナーの購読を解除します。
        /// </summary>
        public void UnbindEvents(WeatherLightningProcessor lightningProcessor, Action<Vector3> onStarted, Action onEnded)
        {
            if (lightningProcessor == null) return;
            lightningProcessor.OnStrikeStarted -= onStarted;
            lightningProcessor.OnStrikeEnded -= onEnded;
        }

        /// <summary>
        /// 落雷を開始します。
        /// </summary>
        public bool TriggerStrike(
            WeatherLightningProcessor lightningProcessor,
            Transform viewer,
            float cloudY,
            float groundY,
            Vector3? targetPosition = null)
        {
            if (lightningProcessor == null) return false;
            return lightningProcessor.TriggerStrike(viewer, cloudY, groundY, targetPosition);
        }

        /// <summary>
        /// 激しい雨（嵐）における自動落雷タイマーを評価し、必要なら落雷を発火します。
        /// </summary>
        public void EvaluateAutoStrike(
            float currentIntensity,
            WeatherLightningSettings settings,
            float currentTime,
            WeatherLightningProcessor lightningProcessor,
            Transform viewer,
            float cloudY,
            float groundY)
        {
            if (_autoStrikeTimer.Evaluate(currentIntensity, settings, currentTime))
            {
                TriggerStrike(lightningProcessor, viewer, cloudY, groundY, null);
            }
        }

        /// <summary>
        /// 落雷開始時の照明・音響・イベントを調停します。
        /// </summary>
        public void HandleStrikeStarted(
            Vector3 groundPos,
            Transform viewer,
            WeatherLightingProcessor lightingProcessor,
            WeatherAudioSynthesizer audioSynthesizer,
            WeatherLightningSettings lightningSettings,
            MonoBehaviour logContext)
        {
            lightingProcessor?.OnStrikeStarted(groundPos);

            if (audioSynthesizer != null)
            {
                Vector3 listenerPos = viewer != null ? viewer.position : Vector3.zero;
                float dist = Vector3.Distance(listenerPos, groundPos);
                audioSynthesizer.PlayThunder(dist, lightningSettings.minRadius, lightningSettings.maxRadius);
            }

            OnLightningStrike?.Invoke(groundPos);

            if (logContext != null)
            {
                AppLogger.Log(logContext, WeatherManager.TagWeatherManager,
                    $"[WeatherManager] 落雷発生: 地点={groundPos:F2}");
            }
        }

        /// <summary>
        /// 落雷終了時の照明演出を終了します。
        /// </summary>
        public void HandleStrikeEnded(WeatherLightingProcessor lightingProcessor)
        {
            lightingProcessor?.OnStrikeEnded();
        }

        /// <summary>
        /// 閃光強度に応じたシーンライティングを反映します (LateUpdate での実行を推奨)。
        /// </summary>
        public void ApplyLighting(
            WeatherLightingProcessor lightingProcessor,
            WeatherLightningProcessor lightningProcessor,
            float currentIntensity)
        {
            if (lightingProcessor != null && lightningProcessor != null)
            {
                lightingProcessor.ApplyLighting(currentIntensity, lightningProcessor.CurrentFlash);
            }
        }

        /// <summary>
        /// タイマー状態をリセットします。
        /// </summary>
        public void Reset()
        {
            _autoStrikeTimer.Reset();
        }
    }
}
