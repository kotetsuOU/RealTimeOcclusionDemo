#if AUTD3_SDK_CURRENT
using System.Collections.Generic;
using System.Linq;
using Features.Haptics.Core;
using Features.Haptics.Processors;
using AUTD3;
using static AUTD3.Units;

#nullable enable

namespace Features.Haptics.Backends.Current
{
    /// <summary>
    /// autd3-sdk (v0.9.0 / Current SDK) におけるホログラフィ (Holo) および時空間変調 (STM)
    /// のパターンバッファ構築を統括するビルダー。
    /// 実際の構築処理は <see cref="HAP_CurrentHoloBuilder"/> および <see cref="HAP_CurrentSTMBuilder"/> に委譲します。
    /// </summary>
    public class HAP_CurrentPatternBuilder
    {
        private readonly HAP_CurrentHoloBuilder _holoBuilder = new HAP_CurrentHoloBuilder();
        private readonly HAP_CurrentSTMBuilder _stmBuilder = new HAP_CurrentSTMBuilder();

        /// <summary>
        /// 焦点クラスタデータ群からホログラフィパターンまたは STM を計算し、<see cref="DatagramBuilder"/> へ登録します。
        /// </summary>
        public void BuildPatterns(
            DatagramBuilder builder,
            Geometry geometry,
            float soundSpeed,
            IReadOnlyList<HAP_FociGenerator.ClusterFociData> clusterData,
            HoloAlgorithm holoAlgorithm,
            float focusIntensityPascal,
            uint gspatRepeat = 20)
        {
            var clusterList = clusterData.ToList();
            Length wavelength = Pattern.Wavelength(soundSpeed * m / s);

            bool useSTM = clusterList.Any(c => c.UseSTM && c.STMFrames != null && c.STMFrames.Count > 1);
            if (useSTM)
            {
                _stmBuilder.Build(builder, geometry, wavelength, clusterList, holoAlgorithm, focusIntensityPascal, gspatRepeat);
            }
            else
            {
                _holoBuilder.Build(builder, geometry, wavelength, clusterList, holoAlgorithm, focusIntensityPascal, gspatRepeat);
            }
        }
    }
}
#endif
