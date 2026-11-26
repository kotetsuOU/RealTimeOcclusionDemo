using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Debug;
using Features.Haptics.Hardware;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// シーン上の AUTD デバイス群の配置管理（プレハブ生成および配置 JSON 保存／復元の統括）を行うコンポーネント。
/// ファイル I/O 処理は <see cref="HAP_AUTDTransformSerializer"/> へ委譲します。
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
    /// 現在のシーン上の AUTD デバイスの配置を JSON ファイルに保存します。
    /// </summary>
    public void Save()
    {
        var devices = ResolveDeviceRoot().GetComponentsInChildren<AUTD3Device>(false);
        HAP_AUTDTransformSerializer.SaveSnapshots(configFileName, devices, this);
    }

    /// <summary>
    /// JSON ファイルに保存されている配置データを読み込み、シーン上の AUTD デバイスに適用します。
    /// 足りない ID のデバイスがある場合は自動的にプレハブを生成して配置します。
    /// </summary>
    public void Load()
    {
        if (!HAP_AUTDTransformSerializer.TryLoadSnapshots(configFileName, this, out var snapshotById))
        {
            return;
        }

        var devices = ResolveDeviceRoot().GetComponentsInChildren<AUTD3Device>(false);
        var deviceById = new Dictionary<int, AUTD3Device>();
        foreach (var dev in devices)
        {
            if (!deviceById.ContainsKey(dev.ID))
            {
                deviceById.Add(dev.ID, dev);
            }
        }

        foreach (int id in snapshotById.Keys.OrderBy(id => id))
        {
            if (!deviceById.ContainsKey(id))
            {
                var newDevice = CreateDevice(id, true);
                if (newDevice != null) deviceById.Add(id, newDevice);
            }
        }

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
    /// ID が 0 から prefabCount - 1 までのデバイスを一括で生成します（まだ存在しない ID のみ）。
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

    private Transform ResolveDeviceRoot() => deviceRoot != null ? deviceRoot : transform;
}
