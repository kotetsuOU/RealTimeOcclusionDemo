using System;
using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 落雷閃光レベル F(t) の状態管理、ボルトプールへの輝度反映、
    /// および落雷終了・中断時のクリーンアップを担当する Pure C# クラス。
    /// 完全 Zero-GC で動作します。
    /// </summary>
    public class WeatherLightningFlashController
    {
        private float _currentFlash;
        private bool _isBusy;

        public float CurrentFlash => _currentFlash;
        public bool IsBusy
        {
            get => _isBusy;
            set => _isBusy = value;
        }

        /// <summary>
        /// 閃光レベル F(t) を反映し、ボルトプールおよびリスナーに通知します。
        /// </summary>
        public void ApplyFlash(float level, WeatherLightningBoltPool boltPool, Action<float> onFlashLevelChanged)
        {
            _currentFlash = level;
            if (boltPool != null)
            {
                boltPool.SetLevel(level);
            }
            onFlashLevelChanged?.Invoke(level);
        }

        /// <summary>
        /// 落雷終了処理を実行し、ボルトを非アクティブ化して待機状態に戻します。
        /// </summary>
        public void EndStrike(WeatherLightningBoltPool boltPool, Action<float> onFlashLevelChanged, Action onStrikeEnded)
        {
            _currentFlash = 0f;
            if (boltPool != null)
            {
                boltPool.Hide();
            }
            onFlashLevelChanged?.Invoke(0f);
            onStrikeEnded?.Invoke();
            _isBusy = false;
        }

        /// <summary>
        /// 演出中断・強制停止時のリセットを行います。
        /// </summary>
        public void Reset(WeatherLightningBoltPool boltPool, Action<float> onFlashLevelChanged, Action onStrikeEnded)
        {
            EndStrike(boltPool, onFlashLevelChanged, onStrikeEnded);
        }
    }
}
