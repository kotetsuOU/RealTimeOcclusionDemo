using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Core;
using Features.Haptics.Debug;
using Features.Haptics.Processors;
using AUTD3Sharp;
using AUTD3Sharp.Driver.Datagram;
using AUTD3Sharp.Gain;
using AUTD3Sharp.Gain.Holo;
using static AUTD3Sharp.Units;

#nullable enable

namespace Features.Haptics.Backends.Legacy
{
    /// <summary>
    /// AUTD3Sharp (v3.x / Legacy SDK) におけるホログラフィ (GSPAT / Naive)、STM (GainSTM / FociSTM)、
    /// およびデバイスグループ化データグラム (<see cref="IDatagram"/>) の生成を担当する専任ビルダー。
    /// </summary>
    public class HAP_LegacyDatagramBuilder
    {
        /// <summary>
        /// デバイス群およびクラスタデータから送信対象の <see cref="IDatagram"/> を構築します。
        /// </summary>
        public IDatagram BuildDatagram(
            IReadOnlyList<HAP_FociGenerator.ClusterFociData> clusterData,
            IReadOnlyList<AUTD3Device> connectedDevices,
            HoloAlgorithm holoAlgorithm,
            bool enableDirectionalGrouping,
            float directionalAngleThreshold,
            float focusIntensityPascal,
            HAP_AUTDDebugDisabler? debugDisabler = null)
        {
            var devList = connectedDevices.ToList();
            var clusterList = clusterData.ToList();

            if (devList.Count == 1)
            {
                if (debugDisabler != null && debugDisabler.IsDisabled(devList[0].ID))
                    return new Null();

                return GenerateDatagram(clusterList, holoAlgorithm, focusIntensityPascal);
            }

            bool hasExplicit = clusterList.Any(c => c.AssignedDeviceIndex >= 0 || (c.AssignedDeviceIndices != null && c.AssignedDeviceIndices.Count > 0));
            if (!hasExplicit && (!enableDirectionalGrouping || devList.Count == 0))
            {
                if (debugDisabler != null && devList.Any(d => debugDisabler.IsDisabled(d.ID)))
                {
                    return BuildGroup(devList, devIdx =>
                    {
                        var dev = devList[devIdx];
                        if (debugDisabler.IsDisabled(dev.ID)) return new Null();
                        return GenerateDatagram(clusterList, holoAlgorithm, focusIntensityPascal);
                    });
                }

                return GenerateDatagram(clusterList, holoAlgorithm, focusIntensityPascal);
            }

            var assignments = HAP_DeviceGrouping.CalculateAssignments(
                clusterList,
                devList,
                enableDirectionalGrouping,
                directionalAngleThreshold,
                debugDisabler);

            return BuildGroup(devList, devIdx =>
            {
                var dev = devList[devIdx];
                if (debugDisabler != null && debugDisabler.IsDisabled(dev.ID))
                    return new Null();

                if (assignments.TryGetValue(dev.ID, out var assigned) && assigned.Count > 0)
                    return GenerateDatagram(assigned, holoAlgorithm, focusIntensityPascal);

                return new Null();
            });
        }

        /// <summary>
        /// クラスタデータリストからホログラフィまたは STM データグラムを生成します。
        /// </summary>
        public IDatagram GenerateDatagram(
            List<HAP_FociGenerator.ClusterFociData> clusterData,
            HoloAlgorithm holoAlgorithm,
            float focusIntensityPascal)
        {
            if (clusterData.Count == 0) return new Null();

            bool useSTM = clusterData.Any(c => c.UseSTM && c.STMFrames != null && c.STMFrames.Count > 1);
            if (useSTM)
            {
                return GenerateStmDatagram(clusterData, holoAlgorithm, focusIntensityPascal);
            }

            return GenerateStaticHoloDatagram(clusterData, holoAlgorithm, focusIntensityPascal);
        }

        private IDatagram GenerateStmDatagram(
            List<HAP_FociGenerator.ClusterFociData> clusterData,
            HoloAlgorithm holoAlgorithm,
            float focusIntensityPascal)
        {
            int maxSamples = clusterData.Max(c => c.UseSTM ? c.STMFrames.Count : 1);
            float stmFreq = clusterData.First(c => c.UseSTM).STMFrequency;
            bool isGainStm = clusterData.Any(c => c.IsGainSTM);

            if (isGainStm && holoAlgorithm == HoloAlgorithm.GSPAT)
            {
                var gains = new List<IGain>();
                for (int i = 0; i < maxSamples; i++)
                {
                    var activeFoci = new List<(AUTD3Sharp.Utils.Point3, Amplitude)>();
                    foreach (var cData in clusterData)
                    {
                        if (cData.UseSTM && i < cData.STMFrames.Count)
                        {
                            foreach (var p in cData.STMFrames[i])
                            {
                                activeFoci.Add((new AUTD3Sharp.Utils.Point3(p.x, p.y, p.z), focusIntensityPascal * Pa));
                            }
                        }
                        else
                        {
                            foreach (var sf in cData.SequentialFoci)
                            {
                                activeFoci.Add(ToSdkFocus(sf));
                            }
                        }
                    }
                    gains.Add(new GSPAT(activeFoci.ToArray(), new GSPATOption()));
                }

                return new GainSTM(gains, stmFreq * Hz, new GainSTMOption()).IntoNearest();
            }
            else
            {
                var mergedFrames = new List<ControlPoints>();
                byte intensityVal = (byte)Mathf.Clamp((focusIntensityPascal / 10000f) * 255f, 0, 255);
                var intensity = new Intensity(intensityVal);

                for (int i = 0; i < maxSamples; i++)
                {
                    var points = new List<ControlPoint>();
                    foreach (var cData in clusterData)
                    {
                        if (cData.UseSTM && i < cData.STMFrames.Count)
                        {
                            foreach (var p in cData.STMFrames[i])
                            {
                                points.Add(new ControlPoint(new AUTD3Sharp.Utils.Point3(p.x, p.y, p.z)));
                            }
                        }
                        else
                        {
                            foreach (var sf in cData.SequentialFoci)
                            {
                                points.Add(new ControlPoint(ToSdkFocus(sf).Item1));
                            }
                        }
                    }
                    mergedFrames.Add(new ControlPoints(points.ToArray(), intensity));
                }

                return new FociSTM(mergedFrames, stmFreq * Hz).IntoNearest();
            }
        }

        private IDatagram GenerateStaticHoloDatagram(
            List<HAP_FociGenerator.ClusterFociData> clusterData,
            HoloAlgorithm holoAlgorithm,
            float focusIntensityPascal)
        {
            var mergedFoci = new List<(AUTD3Sharp.Utils.Point3, Amplitude)>();
            foreach (var cData in clusterData)
            {
                if (cData.SequentialFoci.Count > 0)
                {
                    foreach (var sf in cData.SequentialFoci) mergedFoci.Add(ToSdkFocus(sf));
                }
                else if (cData.STMFrames != null && cData.STMFrames.Count > 0 && cData.STMFrames[0].Count > 0)
                {
                    foreach (var p in cData.STMFrames[0])
                    {
                        mergedFoci.Add((new AUTD3Sharp.Utils.Point3(p.x, p.y, p.z), focusIntensityPascal * cData.Cluster.Force * Pa));
                    }
                }
            }

            if (mergedFoci.Count == 0)
                mergedFoci.Add((new AUTD3Sharp.Utils.Point3(0, 0, 0), 0f * Pa));
            if (mergedFoci.Count == 1)
                mergedFoci.Add((mergedFoci[0].Item1, 0f * Pa));

            if (holoAlgorithm == HoloAlgorithm.GSPAT)
                return new GSPAT(mergedFoci.ToArray(), new GSPATOption());
            else
                return new Naive(mergedFoci.ToArray(), new NaiveOption());
        }

        /// <summary>
        /// デバイスインデックスに対応した個別データグラムから <see cref="Group"/> データグラムを組み立てます。
        /// </summary>
        public IDatagram BuildGroup(List<AUTD3Device> connectedDevices, Func<int, IDatagram> datagrams)
        {
            var groupDict = new GroupDictionary();
            for (int i = 0; i < connectedDevices.Count; i++)
            {
                groupDict.Add(i, datagrams(i));
            }

            int maxIdx = connectedDevices.Count;
            return new Group(dev =>
            {
                int idx = dev.Idx();
                if (idx < 0 || idx >= maxIdx)
                {
                    AppLogger.LogError(null, HAP_LogTriggers.TagController, $"[BuildGroup] idx={idx} is OUT OF RANGE [0,{maxIdx}). Skipping device.");
                    return null;
                }
                return (object)idx;
            }, groupDict);
        }

        private static (AUTD3Sharp.Utils.Point3, Amplitude) ToSdkFocus(HAP_FocusPoint f)
            => (new AUTD3Sharp.Utils.Point3(f.Position.x, f.Position.y, f.Position.z), f.Pascal * Pa);
    }
}
