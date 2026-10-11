#if AUTD3_SDK_CURRENT
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Features.Haptics.Core;
using Features.Haptics.Processors;
using AUTD3;
using AUTD3.Holo;
using static AUTD3.Units;

#nullable enable

namespace Features.Haptics.Backends.Current
{
    /// <summary>
    /// autd3-sdk (v0.9.0 / Current SDK) における時空間変調 STM (GainSTM / FociSTM) パターンの構築を担当する専任ビルダー。
    /// </summary>
    public class HAP_CurrentSTMBuilder
    {
        /// <summary>
        /// 時空間変調 STM パターン（GainSTM または FociStm）を計算し、<see cref="DatagramBuilder"/> に登録します。
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
            int maxSamples = clusterList.Max(c => c.UseSTM ? c.STMFrames.Count : 1);
            float stmFreq = clusterList.First(c => c.UseSTM).STMFrequency;
            bool isGainStm = clusterList.Any(c => c.IsGainSTM);

            if (isGainStm && holoAlgorithm == HoloAlgorithm.GSPAT)
            {
                BuildGainStm(builder, geometry, wavelength, clusterList, maxSamples, stmFreq, focusIntensityPascal, gspatRepeat);
            }
            else
            {
                BuildFociStm(builder, clusterList, maxSamples, stmFreq, focusIntensityPascal);
            }
        }

        private void BuildGainStm(
            DatagramBuilder builder,
            Geometry geometry,
            Length wavelength,
            List<HAP_FociGenerator.ClusterFociData> clusterList,
            int maxSamples,
            float stmFreq,
            float focusIntensityPascal,
            uint gspatRepeat)
        {
            var patternBuffers = new PatternBuffer[maxSamples];
            var option = new GspatOption(repeat: gspatRepeat);

            for (int i = 0; i < maxSamples; i++)
            {
                var activeFoci = new List<AmplitudeTarget>();
                foreach (var cData in clusterList)
                {
                    if (cData.UseSTM && i < cData.STMFrames.Count)
                    {
                        foreach (var p in cData.STMFrames[i])
                        {
                            activeFoci.Add(new AmplitudeTarget(p, Amplitude.FromPascal(focusIntensityPascal)));
                        }
                    }
                    else
                    {
                        foreach (var sf in cData.SequentialFoci)
                        {
                            activeFoci.Add(new AmplitudeTarget(sf.Position, Amplitude.FromPascal(sf.Pascal)));
                        }
                    }
                }

                patternBuffers[i] = geometry.PatternBuffer();
                Holo.Gspat(geometry, activeFoci.ToArray(), wavelength, option, patternBuffers[i]);
            }

            builder.Push(new PatternStm(Nearest(stmFreq * Hz), patternBuffers));
        }

        private void BuildFociStm(
            DatagramBuilder builder,
            List<HAP_FociGenerator.ClusterFociData> clusterList,
            int maxSamples,
            float stmFreq,
            float focusIntensityPascal)
        {
            byte intensityVal = (byte)Mathf.Clamp((focusIntensityPascal / 10000f) * 255f, 0, 255);
            var intensity = new Intensity(intensityVal);
            var controlPointsList = new List<ControlPoints>();

            for (int i = 0; i < maxSamples; i++)
            {
                var points = new List<AUTD3.ControlPoint>();
                foreach (var cData in clusterList)
                {
                    if (cData.UseSTM && i < cData.STMFrames.Count)
                    {
                        foreach (var p in cData.STMFrames[i])
                        {
                            points.Add(new AUTD3.ControlPoint(p));
                        }
                    }
                    else
                    {
                        foreach (var sf in cData.SequentialFoci)
                        {
                            points.Add(new AUTD3.ControlPoint(sf.Position));
                        }
                    }
                }
                controlPointsList.Add(new ControlPoints(points.ToArray(), intensity));
            }

            builder.Push(new FociStm(Nearest(stmFreq * Hz), controlPointsList.ToArray()));
        }
    }
}
#endif
