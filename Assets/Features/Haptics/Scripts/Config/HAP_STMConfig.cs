using System;
using UnityEngine;

#nullable enable

namespace Features.Haptics.Config
{
    /// <summary>
    /// 時空間変調 (Spatio-Temporal Modulation: STM) の出力モードおよび周波数設定を保持する設定クラス。
    /// </summary>
    [Serializable]
    public class HAP_STMConfig
    {
        [Tooltip("STMの種類を選択。FociSTM(ハードウェア計算・単焦点)、GainSTM(CPU計算・GSPAT等の複数焦点に対応)")]
        public HapticsSTMMode stmMode = HapticsSTMMode.FociSTM;

        [Tooltip("STMの再生周波数 (Hz)。単焦点・多焦点いずれのSTMでも再生速度として利用されます。")]
        public float stmFrequency = 150f;

        [Tooltip("GainSTM時のモード。通常は PhaseIntensityFull を使用します。")]
        public GainSTMMode gainStmMode = GainSTMMode.PhaseIntensityFull;
    }
}
