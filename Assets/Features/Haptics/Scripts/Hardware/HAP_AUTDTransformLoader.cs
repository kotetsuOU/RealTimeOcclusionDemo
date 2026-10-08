using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Debug;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 個々のAUTDデバイスのローカル座標・回転とIDを保存する構造体
/// </summary>
[Serializable]
public struct HAP_AUTDTransformSnapshot
{
    public int id;
    public Vector3 localPosition;
    public Quaternion localRotation;
}

/// <summary>
/// 複数のAUTDデバイスの配置データ（位置・回転）を保存・管理する設定クラス (JSON用)
/// </summary>
[Serializable]
public class HAP_AUTDTransformConfig

{
    public List<HAP_AUTDTransformSnapshot> snapshots = new List<HAP_AUTDTransformSnapshot>();
}

/// <summary>
/// シーン上のAUTDデバイス群の配置（位置・回転）をScriptableObjectに保存し、復元するクラス
/// </summary>
[DisallowMultipleComponent]
public class HAP_AUTDTransformLoader : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("保存・読み込み先のJSONファイル名")]
    public string configFileName = "AUTDTransforms.json";
    
    [Tooltip("自動生成するAUTD3デバイスのプレハブ")]
    public GameObject devicePrefab;
    
    [Tooltip("デバイスを配置する親オブジェクト（未指定時は自身）")]
    public Transform deviceRoot;
    
    [Tooltip("自動生成するデバイスの最大数")]
    public int prefabCount = 20;

    [Header("Calibration / Coordinate Settings")]
    [Tooltip("すべての焦点位置に加算されるオフセット。デバイスの原点とUnity上の位置を微調整するのに使います。")]
    public Vector3 offset = Vector3.zero;

    /// <summary>
    /// 現在のシーン上のAUTDデバイスの配置をJSONファイルに保存します。
    /// （IDの重複は無視されます）
    /// </summary>
    public void Save()
    {
        string fullPath = Path.Combine(AppPaths.HapticsConfigDir, configFileName);
        string directoryPath = Path.GetDirectoryName(fullPath);
        
        if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var devices = ResolveDeviceRoot().GetComponentsInChildren<AUTD3Device>(false);
        var deviceById = new Dictionary<int, AUTD3Device>();

        foreach (var dev in devices)
        {
            if (deviceById.ContainsKey(dev.ID))
            {
                AppLogger.LogWarning(this, HAP_LogTriggers.TagTransformLoader, $"Duplicate AUTD3Device ID {dev.ID} found. Skipping duplicate.");
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

        string json = JsonUtility.ToJson(config, true);
        File.WriteAllText(fullPath, json);

#if UNITY_EDITOR
        AssetDatabase.Refresh();
#endif
        AppLogger.Log(this, HAP_LogTriggers.TagTransformLoader, $"Saved {config.snapshots.Count} AUTD transform snapshots to {fullPath}.");
    }

    /// <summary>
    /// JSONファイルに保存されている配置データを読み込み、シーン上のAUTDデバイスに適用します。
    /// 足りないIDのデバイスがある場合は自動的にプレハブを生成して配置します。
    /// </summary>
    public void Load()
    {
        string fullPath = Path.Combine(AppPaths.HapticsConfigDir, configFileName);
        if (!File.Exists(fullPath))
        {
            AppLogger.LogWarning(this, HAP_LogTriggers.TagTransformLoader, $"Config JSON file not found at {fullPath}");
            return;
        }

        string json = File.ReadAllText(fullPath);
        var config = JsonUtility.FromJson<HAP_AUTDTransformConfig>(json);
        
        if (config == null || config.snapshots == null)
        {
            AppLogger.LogWarning(this, HAP_LogTriggers.TagTransformLoader, "Failed to parse JSON config.");
            return;
        }

        var devices = ResolveDeviceRoot().GetComponentsInChildren<AUTD3Device>(false);
        var deviceById = new Dictionary<int, AUTD3Device>();

        foreach (var dev in devices)
        {
            if (!deviceById.ContainsKey(dev.ID))
                deviceById.Add(dev.ID, dev);
        }

        var snapshotById = config.snapshots.ToDictionary(s => s.id, s => s);

        // Generate missing devices based on config
        foreach (var id in snapshotById.Keys.OrderBy(id => id))
        {
            if (!deviceById.ContainsKey(id))
            {
                var newDevice = CreateDevice(id, true);
                if (newDevice != null) deviceById.Add(id, newDevice);
            }
        }

        // Apply transformations
        foreach (var snapshot in snapshotById.Values.OrderBy(s => s.id))
        {
            if (deviceById.TryGetValue(snapshot.id, out var dev))
            {
                dev.transform.localPosition = snapshot.localPosition;
                dev.transform.localRotation = snapshot.localRotation;
            }
        }

        AppLogger.Log(this, HAP_LogTriggers.TagTransformLoader, $"Loaded {snapshotById.Count} AUTD transform snapshots.");
    }

    /// <summary>
    /// IDが0から prefabCount-1 までのデバイスを一括で生成します（まだ存在しないIDのみ）。
    /// </summary>
    public void GeneratePrefabs()
    {
        if (prefabCount < 0) return;
        if (devicePrefab == null)
        {
            AppLogger.LogWarning(this, HAP_LogTriggers.TagTransformLoader, "Device Prefab is missing!");
            return;
        }

        var devices = ResolveDeviceRoot().GetComponentsInChildren<AUTD3Device>(false);
        var existingIds = new HashSet<int>(devices.Select(d => d.ID));

        int generatedCount = 0;
        for (int i = 0; i < prefabCount; i++)
        {
            if (!existingIds.Contains(i))
            {
                CreateDevice(i, true);
                generatedCount++;
            }
        }

        AppLogger.Log(this, HAP_LogTriggers.TagTransformLoader, $"Generated {generatedCount} AUTD prefab(s).");
    }

    private AUTD3Device CreateDevice(int id, bool recordUndo)
    {
        if (devicePrefab == null) return null;

        GameObject instance = null;

#if UNITY_EDITOR
        if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(devicePrefab))
        {
            var prefabObj = PrefabUtility.InstantiatePrefab(devicePrefab) as GameObject;
            if (prefabObj != null)
            {
                if (recordUndo) Undo.RegisterCreatedObjectUndo(prefabObj, "Generate AUTD Prefabs");
                instance = prefabObj;
            }
        }
#endif

        if (instance == null)
        {
            instance = Instantiate(devicePrefab);
#if UNITY_EDITOR
            if (recordUndo && !Application.isPlaying) Undo.RegisterCreatedObjectUndo(instance, "Generate AUTD Prefabs");
#endif
        }

        instance.transform.SetParent(ResolveDeviceRoot(), false);
        instance.name = $"Autd{id}";

        var device = instance.GetComponent<AUTD3Device>();
        if (device == null)
        {
#if UNITY_EDITOR
            if (recordUndo && !Application.isPlaying) device = Undo.AddComponent<AUTD3Device>(instance);
            else
#endif
                device = instance.AddComponent<AUTD3Device>();
        }

        device.ID = id;

#if UNITY_EDITOR
        EditorUtility.SetDirty(device);
        EditorUtility.SetDirty(instance);
#endif

        return device;
    }

    private Transform ResolveDeviceRoot()
    {
        return deviceRoot != null ? deviceRoot : transform;
    }
}
