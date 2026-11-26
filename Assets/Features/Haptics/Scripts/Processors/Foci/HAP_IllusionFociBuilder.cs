#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Features.Haptics.Processors
{
    /// <summary>
    /// 触覚錯覚コントローラー向けの独立単焦点／円環 STM 焦点データ (<see cref="HAP_FociGenerator.ClusterFociData"/>)
    /// を構築する純粋ビルダー。
    /// </summary>
    public static class HAP_IllusionFociBuilder
    {
        /// <summary>
        /// <see cref="HapticsIllusionTargetConfig"/> の設定に基づき、ターゲット位置・STM 軌跡・割り当てデバイスを持つ焦点データを構築します。
        /// </summary>
        public static HAP_FociGenerator.ClusterFociData BuildFromTargetConfig(
            HapticsIllusionTargetConfig cfg,
            float defaultIntensityPascal,
            Vector3 globalOffset)
        {
            Transform target = cfg.targetTransform!;
            float intensity = cfg.focusIntensityPascal > 0f ? cfg.focusIntensityPascal : defaultIntensityPascal;
            Vector3 centerPos = target.position + target.TransformDirection(cfg.offsetPosition) + globalOffset;

            var deviceIds = cfg.assignedDeviceGroup != null
                ? cfg.assignedDeviceGroup.SelectedDeviceIDs
                : new List<int>();

            return BuildCore(
                centerPos,
                target.forward,
                target.right,
                target.up,
                deviceIds,
                intensity,
                cfg.useSTM,
                cfg.stmFrequency,
                cfg.stmRadius,
                cfg.stmPoints);
        }

        /// <summary>
        /// 法線ベクトルとデバイス ID リストから直交基底を算出し、焦点または円環 STM データを構築します。
        /// </summary>
        public static HAP_FociGenerator.ClusterFociData BuildForDirectionalGroup(
            Vector3 position,
            Vector3 normal,
            List<int> deviceIDs,
            float intensityPascal,
            bool useSTM,
            float stmFrequency,
            float stmRadius,
            int stmPoints)
        {
            Vector3 right = Vector3.Cross(normal, Vector3.up);
            if (right.sqrMagnitude < 0.001f)
            {
                right = Vector3.Cross(normal, Vector3.right);
            }
            right.Normalize();
            Vector3 up = Vector3.Cross(right, normal).normalized;

            return BuildCore(position, normal, right, up, deviceIDs, intensityPascal, useSTM, stmFrequency, stmRadius, stmPoints);
        }

        private static HAP_FociGenerator.ClusterFociData BuildCore(
            Vector3 position,
            Vector3 normal,
            Vector3 right,
            Vector3 up,
            List<int> deviceIDs,
            float intensityPascal,
            bool useSTM,
            float stmFrequency,
            float stmRadius,
            int stmPoints)
        {
            TrackedCluster dummyCluster = new TrackedCluster
            {
                Centroid = position,
                Normal = normal,
                Force = 1.0f,
                IsAlive = true
            };

            var fociData = new HAP_FociGenerator.ClusterFociData(dummyCluster)
            {
                AssignedDeviceIndices = new List<int>(deviceIDs),
                AssignedDeviceIndex = deviceIDs.Count > 0 ? deviceIDs[0] : -1,
                UseSTM = useSTM,
                STMFrequency = stmFrequency
            };

            if (useSTM && stmRadius > 0f && stmPoints >= 4)
            {
                for (int i = 0; i < stmPoints; i++)
                {
                    float angle = (2.0f * Mathf.PI * i) / stmPoints;
                    Vector3 p = position + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * stmRadius;
                    fociData.STMFrames.Add(new List<Vector3> { p });
                }
            }

            fociData.SequentialFoci.Add(new HAP_FocusPoint(position, intensityPascal));
            return fociData;
        }
    }
}

