#if AUTD3_SDK_CURRENT
using System;
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
    /// autd3-sdk (v0.14.1 / Current SDK) における静的超音波ホログラフィ (Naive / GSPAT) パターンの構築を担当する専任ビルダー。
    /// </summary>
    public class HAP_CurrentHoloBuilder
    {
        /// <summary>
        /// クラスタ焦点データからホログラフィパターンを計算し、バッファを所有する <see cref="HAP_CurrentPatternBuilder.BuiltCommand"/> を返します。
        /// </summary>
        public HAP_CurrentPatternBuilder.BuiltCommand Build(
            Geometry geometry,
            Length wavelength,
            List<HAP_FociGenerator.ClusterFociData> clusterList,
            HoloAlgorithm holoAlgorithm,
            float focusIntensityPascal,
            uint gspatRepeat,
            TransducerMask transducerMask)
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
            {
                mergedFoci.Add(new AmplitudeTarget(Vector3.zero, Amplitude.FromPascal(0f)));
            }

            var phases = geometry.PhaseBuffer();
            var intensities = geometry.IntensityBuffer();
            try
            {
                if (holoAlgorithm == HoloAlgorithm.GSPAT)
                {
                    Holo.Gspat(
                        geometry,
                        mergedFoci.ToArray(),
                        wavelength,
                        new GspatOption { Repeat = gspatRepeat, Mask = transducerMask },
                        phases,
                        intensities);
                }
                else
                {
                    Holo.Naive(
                        geometry,
                        mergedFoci.ToArray(),
                        wavelength,
                        new NaiveOption { Mask = transducerMask },
                        phases,
                        intensities);
                }

                return new HAP_CurrentPatternBuilder.BuiltCommand(
                    new Pattern(phases, intensities),
                    useStreaming: false,
                    phases,
                    intensities);
            }
            catch
            {
                phases.Dispose();
                intensities.Dispose();
                throw;
            }
        }
    }
}
#endif
