using System;
using UnityEngine;

#nullable enable

namespace Features.Haptics.Config
{
    /// <summary>
    /// パフォーマンス計測および同期送信に関する設定パラメータを保持する設定クラス。
    /// </summary>
    [Serializable]
    public class HAP_ProfilingConfig
    {
        [Tooltip("有効にすると、ハプティクスパイプラインの処理時間を計測します。")]
        public bool enableProfiling = false;

        [Tooltip("有効にすると、Sendを含む全処理をメインスレッドで同期実行します。")]
        public bool synchronousSend = false;

        [Tooltip("Debug.Log に処理時間を出力する間隔（フレーム数）。")]
        [Range(1, 600)]
        public int profilingLogInterval = 60;
    }
}
