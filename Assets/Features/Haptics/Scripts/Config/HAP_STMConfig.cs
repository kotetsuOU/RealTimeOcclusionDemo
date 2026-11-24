using System;
using System.Collections.Generic;
using UnityEngine;

#nullable enable

namespace Features.Haptics.Config
{
    /// <summary>
    /// 時空間変調 (Spatio-Temporal Modulation: STM) の出力モード・周波数、
    /// および AutoHCD 用の楕円 STM／接触形状焦点生成ソース設定を保持する設定クラス。
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

        [Header("AutoHCD Foci Generation (Ellipse STM / Precision Sources)")]
        [Tooltip("Simplified: 1クラスタ1点の単純出力(軽量)。\nPrecision: 楕円STMやランダムノイズなど接触形状に沿った刺激を生成します。")]
        public HapticsGenerationMode generationMode = HapticsGenerationMode.Simplified;

        [Tooltip("接触領域の「重心」に対して基本的な超音波の焦点を生成するソース設定")]
        public HAP_HapticsCentroidSource centroidSource = new HAP_HapticsCentroidSource();

        [Tooltip("接触領域の「形状」を主成分分析(PCA)し、楕円状になぞるSTMを生成するソース設定")]
        public HAP_HapticsEllipseSource ellipseSource = new HAP_HapticsEllipseSource();

        [Tooltip("接触領域内でランダムに16点をサンプリングし、不規則に飛び回るSTM（ザラザラ感）を生成するソース設定")]
        public HAP_HapticsRandomSource randomSource = new HAP_HapticsRandomSource();

        /// <summary>
        /// AutoHCD の接触クラスタ群から、現在の生成モードおよび楕円 STM 等ソース設定に応じた焦点データを生成します。
        /// </summary>
        public List<HAP_FociGenerator.ClusterFociData> GenerateAutoHcdFoci(
            List<TrackedCluster> activeClusters,
            float focusIntensityPascal,
            Vector3 offset)
        {
            return HAP_FociGenerator.Generate(
                activeClusters,
                generationMode,
                centroidSource,
                ellipseSource,
                randomSource,
                focusIntensityPascal,
                offset,
                stmMode,
                stmFrequency);
        }
    }
}
