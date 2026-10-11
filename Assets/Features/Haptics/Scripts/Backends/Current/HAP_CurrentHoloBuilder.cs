#if AUTD3_SDK_CURRENT
using System.Collections.Generic;
using UnityEngine;
using Features.Haptics.Core;
using Features.Haptics.Processors;
using AUTD3;
using AUTD3.Holo;

#nullable enable

namespace Features.Haptics.Backends.Current
{
    /// <summary>
    /// autd3-sdk (v0.9.0 / Current SDK) における静的超音波ホログラフィ (Naive / GSPAT) パターンの構築を担当する専任ビルダー。
    /// </summary>
    public class HAP_CurrentHoloBuilder
    {
        /// <summary>
        /// クラスタ焦点データからホログラフィパターンを計算し、<see cref="DatagramBuilder"/> に登録します。
        /// </summary>
        public void Build(
            DatagramBuilder builder,
            Geometry geometry,
            Length wavelength,
            List<HAP_FociGenerator.ClusterFociData> clusterList,
            HoloAlgorithm holoAlgorithm,
            float focusIntensityPascal,
            uint gspatRepeat)
        {
            var mergedFoci = new List<AmplitudeTarget>();
            foreach (var cData in clusterList)
            {
                if (cData.SequentialFoci.Count > 0)
                {
                    foreach (var sf in cData.SequentialFoci)
                    {
                        mergedFoci.Add(new AmplitudeTarget(sf.Position, Amplitude.FromPascal(sf.Pascal)));
                    }
                }
                else if (cData.STMFrames != null && cData.STMFrames.Count > 0 && cData.STMFrames[0].Count > 0)
                {
                    foreach (var p in cData.STMFrames[0])
                    {
                        mergedFoci.Add(new AmplitudeTarget(p, Amplitude.FromPascal(focusIntensityPascal * cData.Cluster.Force)));
                    }
                }
            }

            if (mergedFoci.Count == 0)
                mergedFoci.Add(new AmplitudeTarget(Vector3.zero, Amplitude.FromPascal(0f)));

            var patterns = geometry.PatternBuffer();
            if (holoAlgorithm == HoloAlgorithm.GSPAT)
            {
                Holo.Gspat(geometry, mergedFoci.ToArray(), wavelength, new GspatOption(repeat: gspatRepeat), patterns);
            }
            else
            {
                Holo.Naive(geometry, mergedFoci.ToArray(), wavelength, new NaiveOption(), patterns);
            }

            builder.Push(new Pattern(patterns));
        }
    }
}
#endif
