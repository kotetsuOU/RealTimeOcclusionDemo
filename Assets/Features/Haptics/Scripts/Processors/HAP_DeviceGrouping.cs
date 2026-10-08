using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#nullable enable

namespace Features.Haptics.Processors
{
    /// <summary>
    /// 各クラスタの法線方向や明示的なデバイス割り当て設定に基づき、
    /// どの AUTD デバイスにどのクラスタの焦点を配信するかを決定する純粋計算クラス。
    /// </summary>
    public static class HAP_DeviceGrouping
    {
        /// <summary>
        /// クラスタ群と接続デバイス群から、デバイス ID ごとの担当クラスタリストを計算します。
        /// </summary>
        public static Dictionary<int, List<HAP_FociGenerator.ClusterFociData>> CalculateAssignments(
            IReadOnlyList<HAP_FociGenerator.ClusterFociData> clusterData,
            IReadOnlyList<AUTD3Device> connectedDevices,
            bool enableDirectionalGrouping,
            float directionalAngleThreshold,
            HAP_AUTDDebugDisabler? debugDisabler = null)
        {
            var deviceAssignments = new Dictionary<int, List<HAP_FociGenerator.ClusterFociData>>();
            foreach (var dev in connectedDevices)
            {
                deviceAssignments[dev.ID] = new List<HAP_FociGenerator.ClusterFociData>();
            }

            if (clusterData.Count == 0 || connectedDevices.Count == 0)
            {
                return deviceAssignments;
            }

            foreach (var cData in clusterData)
            {
                bool hasGroupIndices = cData.AssignedDeviceIndices != null && cData.AssignedDeviceIndices.Count > 0;
                if (cData.AssignedDeviceIndex >= 0 || hasGroupIndices)
                {
                    // 明示的なデバイスインデックス / グループが指定されている場合
                    var candidateDevs = new List<AUTD3Device>();
                    for (int i = 0; i < connectedDevices.Count; i++)
                    {
                        var dev = connectedDevices[i];
                        if (debugDisabler != null && debugDisabler.IsDisabled(dev.ID)) continue;

                        bool match = (cData.AssignedDeviceIndex >= 0 && (i == cData.AssignedDeviceIndex || dev.ID == cData.AssignedDeviceIndex)) ||
                                     (hasGroupIndices && cData.AssignedDeviceIndices != null && (cData.AssignedDeviceIndices.Contains(i) || cData.AssignedDeviceIndices.Contains(dev.ID)));
                        if (match)
                        {
                            candidateDevs.Add(dev);
                        }
                    }

                    if (candidateDevs.Count > 0)
                    {
                        if (enableDirectionalGrouping)
                        {
                            bool assigned = false;
                            float groupMinAngle = float.MaxValue;
                            AUTD3Device? bestDev = null;

                            foreach (var dev in candidateDevs)
                            {
                                float angle = Vector3.Angle(dev.transform.forward, -cData.Cluster.Normal);
                                if (angle < groupMinAngle)
                                {
                                    groupMinAngle = angle;
                                    bestDev = dev;
                                }

                                if (angle <= directionalAngleThreshold)
                                {
                                    deviceAssignments[dev.ID].Add(cData);
                                    assigned = true;
                                }
                            }

                            if (!assigned && bestDev != null)
                            {
                                deviceAssignments[bestDev.ID].Add(cData);
                            }
                        }
                        else
                        {
                            foreach (var dev in candidateDevs)
                            {
                                deviceAssignments[dev.ID].Add(cData);
                            }
                        }
                    }
                    continue;
                }

                // 明示的指定がない場合: Directional Grouping に基づき割り当て
                bool isAssigned = false;
                float minAngle = float.MaxValue;
                AUTD3Device? bestDevice = null;

                foreach (var dev in connectedDevices)
                {
                    if (debugDisabler != null && debugDisabler.IsDisabled(dev.ID)) continue;

                    float angle = Vector3.Angle(dev.transform.forward, -cData.Cluster.Normal);
                    if (angle < minAngle)
                    {
                        minAngle = angle;
                        bestDevice = dev;
                    }

                    if (angle <= directionalAngleThreshold)
                    {
                        deviceAssignments[dev.ID].Add(cData);
                        isAssigned = true;
                    }
                }

                if (!isAssigned && bestDevice != null)
                {
                    deviceAssignments[bestDevice.ID].Add(cData);
                }
            }

            return deviceAssignments;
        }
    }
}
