#if AUTD3_SDK_CURRENT
using System;
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
    /// autd3-sdk (v0.14.1 / Current SDK) における時空間変調 STM (PatternStm / FociStm) パターンの構築を担当する専任ビルダー。
    /// </summary>
    public class HAP_CurrentSTMBuilder
    {
        /// <summary>
        /// 時空間変調 STM パターン（PatternStm または FociStm）を計算し、<see cref="HAP_CurrentPatternBuilder.BuiltCommand"/> を返します。
        /// </summary>
        public HAP_CurrentPatternBuilder.BuiltCommand Build(
            Geometry geometry,
            float soundSpeed,
            Length wavelength,
            List<HAP_FociGenerator.ClusterFociData> clusterList,
            HoloAlgorithm holoAlgorithm,
            float focusIntensityPascal,
            uint gspatRepeat,
            TransducerMask transducerMask)
        {
            int maxSamples = clusterList.Max(c => c.UseSTM ? c.STMFrames.Count : 1);
            float stmFreq = clusterList.First(c => c.UseSTM).STMFrequency;
            bool isGainStm = clusterList.Any(c => c.IsGainSTM);

            if (isGainStm && holoAlgorithm == HoloAlgorithm.GSPAT)
            {
                return BuildGainStm(geometry, wavelength, clusterList, maxSamples, stmFreq, focusIntensityPascal, gspatRepeat, transducerMask);
            }

            return BuildFociStm(soundSpeed, clusterList, maxSamples, stmFreq, focusIntensityPascal);
        }

        private HAP_CurrentPatternBuilder.BuiltCommand BuildGainStm(
            Geometry geometry,
            Length wavelength,
            List<HAP_FociGenerator.ClusterFociData> clusterList,
            int maxSamples,
            float stmFreq,
            float focusIntensityPascal,
            uint gspatRepeat,
            TransducerMask transducerMask)
        {
            var phaseBuffers = new PhaseBuffer[maxSamples];
            var intensityBuffers = new IntensityBuffer[maxSamples];
            var disposables = new IDisposable[maxSamples * 2];
            var option = new GspatOption { Repeat = gspatRepeat, Mask = transducerMask };

            try
            {
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

                    if (activeFoci.Count == 0)
                    {
                        activeFoci.Add(new AmplitudeTarget(Vector3.zero, Amplitude.FromPascal(0f)));
                    }

                    var phases = geometry.PhaseBuffer();
                    var intensities = geometry.IntensityBuffer();
                    phaseBuffers[i] = phases;
                    intensityBuffers[i] = intensities;
                    disposables[i * 2] = phases;
                    disposables[i * 2 + 1] = intensities;

                    Holo.Gspat(geometry, activeFoci.ToArray(), wavelength, option, phases, intensities);
                }

                var samplingConfig = CreateNearestSamplingConfig(stmFreq * maxSamples);
                var cmd = new PatternStm(
                    samplingConfig,
                    phaseBuffers,
                    intensityBuffers,
                    new PatternStmOption
                    {
                        Bank = PatternBank.B0,
                        PhaseDepth = PhaseDepth.Bits8,
                        LoopBehavior = LoopBehavior.Infinite,
                        TransitionMode = TransitionMode.Immediate,
                    });

                return new HAP_CurrentPatternBuilder.BuiltCommand(cmd, useStreaming: true, disposables);
            }
            catch
            {
                for (int i = 0; i < disposables.Length; i++)
                {
                    try { disposables[i]?.Dispose(); } catch { }
                }
                throw;
            }
        }

        private HAP_CurrentPatternBuilder.BuiltCommand BuildFociStm(
            float soundSpeed,
            List<HAP_FociGenerator.ClusterFociData> clusterList,
            int maxSamples,
            float stmFreq,
            float focusIntensityPascal)
        {
            byte intensityVal = (byte)Mathf.Clamp((focusIntensityPascal / 10000f) * 255f, 0, 255);
            var intensity = new Intensity(intensityVal);

            // 各サンプルの焦点リストを収集し、FociStm の制約（全サンプル同一焦点数・1..NumFociMax）に正規化する
            var rawSamplePoints = new List<List<AUTD3.ControlPoint>>(maxSamples);
            int maxFociInSample = 0;
            for (int i = 0; i < maxSamples; i++)
            {
                var points = new List<AUTD3.ControlPoint>();
                foreach (var cData in clusterList)
                {
                    if (cData.UseSTM && i < cData.STMFrames.Count)
                    {
                        foreach (var p in cData.STMFrames[i])
                        {
                            points.Add(new AUTD3.ControlPoint(p, Phase.Zero));
                        }
                    }
                    else
                    {
                        foreach (var sf in cData.SequentialFoci)
                        {
                            points.Add(new AUTD3.ControlPoint(sf.Position, Phase.Zero));
                        }
                    }
                }
                if (points.Count > maxFociInSample)
                {
                    maxFociInSample = points.Count;
                }
                rawSamplePoints.Add(points);
            }

            if (maxFociInSample == 0)
            {
                intensity = Intensity.Min;
                maxFociInSample = 1;
            }

            int targetFociCount = Mathf.Clamp(maxFociInSample, 1, Params.NumFociMax);
            var controlPointsList = new List<ControlPoints>(maxSamples);
            for (int i = 0; i < maxSamples; i++)
            {
                var src = rawSamplePoints[i];
                var fixedPoints = new AUTD3.ControlPoint[targetFociCount];
                for (int j = 0; j < targetFociCount; j++)
                {
                    if (src.Count == 0)
                    {
                        fixedPoints[j] = new AUTD3.ControlPoint(Vector3.zero, Phase.Zero);
                    }
                    else if (j < src.Count)
                    {
                        fixedPoints[j] = src[j];
                    }
                    else
                    {
                        fixedPoints[j] = src[src.Count - 1];
                    }
                }
                controlPointsList.Add(new ControlPoints(fixedPoints, intensity));
            }

            var samplingConfig = CreateNearestSamplingConfig(stmFreq * controlPointsList.Count);
            var cmd = new FociStm(
                samplingConfig,
                controlPointsList,
                new FociStmOption
                {
                    Bank = PatternBank.B0,
                    SoundSpeed = soundSpeed * m / s,
                    LoopBehavior = LoopBehavior.Infinite,
                    TransitionMode = TransitionMode.Immediate,
                });

            return new HAP_CurrentPatternBuilder.BuiltCommand(cmd, useStreaming: true);
        }

        /// <summary>
        /// v0.14 で廃止された Nearest の代替として、目標サンプリング周波数 (Hz) に最も近い分周比を持つ <see cref="SamplingConfig"/> を生成します。
        /// </summary>
        private static SamplingConfig CreateNearestSamplingConfig(float targetSamplingFreqHz)
        {
            if (targetSamplingFreqHz <= 0f || float.IsNaN(targetSamplingFreqHz) || float.IsInfinity(targetSamplingFreqHz))
            {
                return SamplingConfig.Freq4k;
            }

            float ultrasoundFreq = Params.UltrasoundFreqHz;
            int divide = Mathf.Clamp(Mathf.RoundToInt(ultrasoundFreq / targetSamplingFreqHz), 1, ushort.MaxValue);
            return new SamplingConfig((ushort)divide);
        }
    }
}
#endif
