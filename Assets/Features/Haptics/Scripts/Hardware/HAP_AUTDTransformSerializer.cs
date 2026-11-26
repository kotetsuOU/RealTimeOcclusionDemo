#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Debug;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Features.Haptics.Hardware
{
    /// <summary>
    /// AUTD3 デバイス群の配置スナップショット JSON の読み書き I/O を担当する純粋ヘルパークラス。
    /// </summary>
    public static class HAP_AUTDTransformSerializer
    {
        /// <summary>
        /// 指定デバイス群のローカル位置・回転を重複 ID 除外のうえ JSON ファイルへ保存します。
        /// </summary>
        public static void SaveSnapshots(string configFileName, IEnumerable<AUTD3Device> devices, Object? logContext = null)
        {
            string fullPath = Path.Combine(AppPaths.HapticsConfigDir, configFileName);
            string? directoryPath = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var deviceById = new Dictionary<int, AUTD3Device>();
            foreach (var dev in devices)
            {
                if (dev == null) continue;
                if (deviceById.ContainsKey(dev.ID))
                {
                    AppLogger.LogWarning(logContext, HAP_LogTriggers.TagTransformLoader,
                        $"Duplicate AUTD3Device ID {dev.ID} found. Skipping duplicate.");
                    continue;
                }
                deviceById[dev.ID] = dev;
            }

            var config = new HAP_AUTDTransformConfig();
            foreach (var dev in deviceById.Values.OrderBy(d => d.ID))
            {
                config.snapshots.Add(new HAP_AUTDTransformSnapshot
                {
                    id = dev.ID,
                    localPosition = dev.transform.localPosition,
                    localRotation = dev.transform.localRotation
                });
            }

            File.WriteAllText(fullPath, JsonUtility.ToJson(config, true));

#if UNITY_EDITOR
            AssetDatabase.Refresh();
#endif
            AppLogger.Log(logContext, HAP_LogTriggers.TagTransformLoader,
                $"Saved {config.snapshots.Count} AUTD transform snapshots to {fullPath}.");
        }

        /// <summary>
        /// JSON ファイルから配置スナップショットを読み込み、ID ごとの辞書を構築します。
        /// </summary>
        public static bool TryLoadSnapshots(
            string configFileName,
            Object? logContext,
            out Dictionary<int, HAP_AUTDTransformSnapshot> snapshotById)
        {
            snapshotById = new Dictionary<int, HAP_AUTDTransformSnapshot>();

            string fullPath = Path.Combine(AppPaths.HapticsConfigDir, configFileName);
            if (!File.Exists(fullPath))
            {
                AppLogger.LogWarning(logContext, HAP_LogTriggers.TagTransformLoader,
                    $"Config JSON file not found at {fullPath}");
                return false;
            }

            string json = File.ReadAllText(fullPath);
            var config = JsonUtility.FromJson<HAP_AUTDTransformConfig>(json);
            if (config == null || config.snapshots == null)
            {
                AppLogger.LogWarning(logContext, HAP_LogTriggers.TagTransformLoader,
                    "Failed to parse JSON config.");
                return false;
            }

            foreach (var snapshot in config.snapshots)
            {
                snapshotById[snapshot.id] = snapshot;
            }

            return true;
        }
    }
}

