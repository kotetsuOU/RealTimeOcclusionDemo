#nullable enable
using System.Collections.Generic;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Config;
using Features.Haptics.Debug;
using Features.Haptics.Hardware;

namespace Features.Haptics.Processors
{
    /// <summary>
    /// デバッグ用単焦点照射のターゲット構築および空間キャリブレーション計算を担当する純粋プロセッサ。
    /// ホットパスでの GC アロケーションを避けるため、出力リストと焦点コンテナを再利用します。
    /// </summary>
    public class HAP_DebugFocusProcessor
    {
        private const int PeriodicLogFrameInterval = 120;

        private TrackedCluster _reusableCluster = new TrackedCluster
        {
            Centroid = Vector3.zero,
            Normal = Vector3.up,
            Force = 1.0f,
            IsAlive = true
        };

        private readonly HAP_FociGenerator.ClusterFociData _reusableFociData;
        private readonly List<HAP_FociGenerator.ClusterFociData> _reusableFociList = new List<HAP_FociGenerator.ClusterFociData>(1);
        private bool _wasActive = false;

        public HAP_DebugFocusProcessor()
        {
            _reusableFociData = new HAP_FociGenerator.ClusterFociData(_reusableCluster)
            {
                UseSTM = false,
                IsGainSTM = false,
            };
            _reusableFociList.Add(_reusableFociData);
        }

        /// <summary>
        /// 単焦点デバッグ設定とキャリブレーションオフセットから、送信用ターゲットリストを構築します。
        /// </summary>
        public List<HAP_FociGenerator.ClusterFociData> BuildSingleFocusTargets(
            HAP_DebugFocusConfig config,
            Vector3 calibrationOffset,
            Object? logContext = null)
        {
            Vector3 rawPos = config.GetRawFocusPosition();
            Vector3 effectivePos = config.GetEffectiveFocusPosition(calibrationOffset);
            float intensityPa = config.GetEffectiveTargetPascal();

            _reusableCluster.Centroid = effectivePos;
            _reusableCluster.Normal = Vector3.up;
            _reusableCluster.Force = 1.0f;
            _reusableCluster.IsAlive = true;

            _reusableFociData.Cluster = _reusableCluster;
            _reusableFociData.SequentialFoci.Clear();
            _reusableFociData.SequentialFoci.Add(new HAP_FocusPoint(effectivePos, intensityPa));
            _reusableFociData.STMFrames.Clear();
            _reusableFociData.UseSTM = false;
            _reusableFociData.IsGainSTM = false;
            _reusableFociData.AssignedDeviceIndex = -1;
            _reusableFociData.AssignedDeviceIndices.Clear();

            if (AppLogger.IsEnabled(logContext, HAP_LogTriggers.TagCalibration) &&
                (!_wasActive || Time.frameCount % PeriodicLogFrameInterval == 0))
            {
                AppLogger.Log(logContext, HAP_LogTriggers.TagCalibration,
                    $"[DebugFocus] Emit Single Focus (Naive): raw={rawPos:F4}, effective={effectivePos:F4}, " +
                    $"offset={(config.applyCalibrationOffset ? calibrationOffset : Vector3.zero):F4}, intensity={intensityPa:F0}Pa, " +
                    $"maxDuty={config.maximizeDuty}, forceSine={config.forceSineModulation}({config.debugSineFrequency:F0}Hz)");
            }
            _wasActive = true;

            return _reusableFociList;
        }

        /// <summary>
        /// 照射停止時に定期ログ初回フラグをリセットします。
        /// </summary>
        public void NotifyStopped()
        {
            _wasActive = false;
        }

        /// <summary>
        /// 現在実際に照射されている座標（知覚された仮想焦点）と、正解位置（実空間ターゲット）との差分から
        /// 新しい全体オフセット（TransformLoader.offset）を算出します。
        /// </summary>
        public static Vector3 CalculateCalibratedOffset(
            HAP_DebugFocusConfig config,
            Vector3 currentOffset,
            Vector3 trueTargetWorldPos)
        {
            Vector3 sentWorldPos = config.GetEffectiveFocusPosition(currentOffset);
            return sentWorldPos - trueTargetWorldPos;
        }

        /// <summary>
        /// アクティブな AUTD3Device 群の平均中心から、平均照射方向へ指定距離だけ前方のデフォルト焦点位置を算出します。
        /// </summary>
        public static Vector3 ComputeDefaultArrayFrontPosition(IReadOnlyList<AUTD3Device> devices, float distanceMeters = 0.20f)
        {
            if (devices == null || devices.Count == 0)
            {
                return new Vector3(0f, distanceMeters, 0f);
            }

            Vector3 centerSum = Vector3.zero;
            Vector3 dirSum = Vector3.zero;
            int validCount = 0;

            for (int i = 0; i < devices.Count; i++)
            {
                var dev = devices[i];
                if (dev == null) continue;

                // デバイス開口面中心（ローカル +X 96mm, +Y 75mm 付近）を基準にする
                centerSum += dev.transform.TransformPoint(new Vector3(0.096f, 0.075f, 0f));
                dirSum += HAP_DeviceOrientationHelper.GetAdjustedForward(dev);
                validCount++;
            }

            if (validCount == 0)
            {
                return new Vector3(0f, distanceMeters, 0f);
            }

            Vector3 avgCenter = centerSum / validCount;
            Vector3 avgDir = dirSum.sqrMagnitude > 1e-6f ? dirSum.normalized : Vector3.up;
            return avgCenter + avgDir * distanceMeters;
        }
    }
}
