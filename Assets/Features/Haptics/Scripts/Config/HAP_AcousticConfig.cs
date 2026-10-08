using System;
using UnityEngine;

#nullable enable

namespace Features.Haptics.Config
{
    /// <summary>
    /// 超音波音響放射・ホログラフィ計算・指向性グルーピングに関する設定パラメータを保持する設定クラス。
    /// </summary>
    [Serializable]
    public class HAP_AcousticConfig
    {
        [Tooltip("ホログラフィアルゴリズム。\nGSPAT: 高速・高品質。\nNaive: 計算は軽いが音圧や精度が落ちます。")]
        public HoloAlgorithm holoAlgorithm = HoloAlgorithm.GSPAT;

        [Tooltip("超音波の出力強度 (Pascal)。最大で 10000 程度。大きすぎるとデバイスの保護回路が働くか、クリッピングが発生します。")]
        public float focusIntensityPascal = 10000f;

        [Tooltip("有効にすると、接触点の法線ベクトル（向き）とAUTDデバイスの向きを比較し、最適なデバイスからのみ超音波を照射します。")]
        public bool enableDirectionalGrouping = false;

        [Tooltip("デバイスが面の法線方向から何度まで傾いていても担当として許容するか（0〜90度）。0度で真正面のみ。")]
        [Range(0, 90)]
        public float directionalAngleThreshold = 45.0f;

        [Tooltip("GSPATホログラフィ計算の反復回数（デフォルト20回）。値を小さくするとCPU負荷が劇的に軽減されます。")]
        [Range(1, 100)]
        public uint gspatRepeatCount = 20;
    }
}
