using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class RsTransformController : MonoBehaviour
{
    [Header("Config")]
    [Tooltip("現在選択中のスロット番号 (0-2)")]
    [Range(0, 2)]
    public int currentSlotIndex = 0;

    [Tooltip("スロット1の設定ファイル名 (.json)。Assets/Configフォルダに保存されます。")]
    public string configFileNameSlot1 = "ChildTransforms_Slot1.json";

    [Tooltip("スロット2の設定ファイル名 (.json)。Assets/Configフォルダに保存されます。")]
    public string configFileNameSlot2 = "ChildTransforms_Slot2.json";

    [Tooltip("スロット3の設定ファイル名 (.json)。Assets/Configフォルダに保存されます。")]
    public string configFileNameSlot3 = "ChildTransforms_Slot3.json";

    [Tooltip("起動時に自動的に設定をロードするかどうか")]
    public bool loadOnStart = false;

    public string CurrentConfigFileName
    {
        get
        {
            switch (currentSlotIndex)
            {
                case 0: return configFileNameSlot1;
                case 1: return configFileNameSlot2;
                case 2: return configFileNameSlot3;
                default: return configFileNameSlot1;
            }
        }
    }

    [Header("Calibration Box Guide")]
    [Tooltip("シーンビューに位置合わせ用のガイドボックスを表示するか")]
    public bool showCalibrationGuide = true;

    [Tooltip("ボックス枠線の色")]
    public Color guideFrameColor = Color.green;

    [Tooltip("各頂点のマーカー色")]
    public Color cornerMarkerColor = Color.red;

    [Tooltip("直方体の起点となる座標（親からのローカル座標）")]
    public Vector3 calibrationOrigin = new Vector3(0.30f, 0.0f, 0.25f);

    [Tooltip("直方体のサイズ（幅・高さ・奥行き）")]
    public Vector3 calibrationBoxSize = new Vector3(0.29f, 0.405f, 0.08f);

    private const string SAVE_FOLDER_PATH = "Assets/Config";

    [Serializable]
    private class TransformItem
    {
        public string childName;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }

    [Serializable]
    private class TransformDataList
    {
        public List<TransformItem> items = new List<TransformItem>();
    }

    private void Start()
    {
        if (loadOnStart)
        {
            LoadTransformConfig();
        }
    }

    private void OnDrawGizmos()
    {
        if (UnityEngine.Application.isPlaying) return;

        if (!showCalibrationGuide) return;

        Gizmos.matrix = transform.localToWorldMatrix;

        Gizmos.color = guideFrameColor;

        Vector3 localCenter = calibrationOrigin + (calibrationBoxSize * 0.5f);
        Gizmos.DrawWireCube(localCenter, calibrationBoxSize);

        Gizmos.color = cornerMarkerColor;

        float markerRadius = Mathf.Min(Mathf.Abs(calibrationBoxSize.x), Mathf.Abs(calibrationBoxSize.y), Mathf.Abs(calibrationBoxSize.z)) * 0.05f;

        Vector3[] corners = new Vector3[]
        {
            calibrationOrigin,
            calibrationOrigin + new Vector3(calibrationBoxSize.x, 0, 0),
            calibrationOrigin + new Vector3(0, calibrationBoxSize.y, 0),
            calibrationOrigin + new Vector3(0, 0, calibrationBoxSize.z),
            calibrationOrigin + new Vector3(calibrationBoxSize.x, calibrationBoxSize.y, 0),
            calibrationOrigin + new Vector3(calibrationBoxSize.x, 0, calibrationBoxSize.z),
            calibrationOrigin + new Vector3(0, calibrationBoxSize.y, calibrationBoxSize.z),
            calibrationOrigin + calibrationBoxSize
        };

        foreach (var point in corners)
        {
            Gizmos.DrawWireSphere(point, markerRadius);
        }
    }

    public void SwitchToSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex > 2)
        {
            UnityEngine.Debug.LogError($"[RsTransformController] 無効なスロット番号: {slotIndex}。0-2の範囲で指定してください。", this);
            return;
        }

        currentSlotIndex = slotIndex;
        LoadTransformConfig();
        UnityEngine.Debug.Log($"[RsTransformController] スロット {slotIndex + 1} に切り替えました。", this);
    }

    public string GetConfigFileName(int slotIndex)
    {
        switch (slotIndex)
        {
            case 0: return configFileNameSlot1;
            case 1: return configFileNameSlot2;
            case 2: return configFileNameSlot3;
            default: return configFileNameSlot1;
        }
    }

    public void SaveTransformConfig()
    {
        SaveTransformConfigToSlot(currentSlotIndex);
    }

    public void SaveTransformConfigToSlot(int slotIndex)
    {
        string configFileName = GetConfigFileName(slotIndex);

        if (string.IsNullOrEmpty(configFileName))
        {
            UnityEngine.Debug.LogError($"[RsTransformController] スロット{slotIndex + 1}の設定ファイル名が指定されていません。", this);
            return;
        }

        string fileName = configFileName.EndsWith(".json") ? configFileName : configFileName + ".json";
        string fullPath = Path.Combine(SAVE_FOLDER_PATH, fileName);

        TransformDataList dataList = new TransformDataList();
        foreach (Transform child in this.transform)
        {
            dataList.items.Add(new TransformItem
            {
                childName = child.name,
                localPosition = child.localPosition,
                localRotation = child.localRotation,
                localScale = child.localScale
            });
        }

        if (!Directory.Exists(SAVE_FOLDER_PATH))
        {
            Directory.CreateDirectory(SAVE_FOLDER_PATH);
        }

        try
        {
            string json = JsonUtility.ToJson(dataList, true);
            File.WriteAllText(fullPath, json);
            UnityEngine.Debug.Log($"[RsTransformController] Saved config to slot {currentSlotIndex + 1}: {fullPath}", this);

#if UNITY_EDITOR
            AssetDatabase.Refresh();
#endif
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[RsTransformController] Failed to save file: {e.Message}", this);
        }
    }

    public void LoadTransformConfig()
    {
        LoadTransformConfigFromSlot(currentSlotIndex);
    }

    public void LoadTransformConfigFromSlot(int slotIndex)
    {
        string configFileName = GetConfigFileName(slotIndex);

        if (string.IsNullOrEmpty(configFileName))
        {
            UnityEngine.Debug.LogError($"[RsTransformController] スロット{slotIndex + 1}の設定ファイル名が指定されていません。", this);
            return;
        }

        string fileName = configFileName.EndsWith(".json") ? configFileName : configFileName + ".json";
        string fullPath = Path.Combine(SAVE_FOLDER_PATH, fileName);

        if (!File.Exists(fullPath))
        {
            UnityEngine.Debug.LogError($"[RsTransformController] File not found: {fullPath}", this);
            return;
        }

        try
        {
            string json = File.ReadAllText(fullPath);
            TransformDataList dataList = JsonUtility.FromJson<TransformDataList>(json);

            if (dataList == null || dataList.items == null)
            {
                UnityEngine.Debug.LogError("[RsTransformController] Failed to parse JSON data.", this);
                return;
            }

#if UNITY_EDITOR
            Undo.RecordObjects(this.transform.GetComponentsInChildren<Transform>(), "Load Transforms");
#endif

            foreach (var item in dataList.items)
            {
                Transform child = this.transform.Find(item.childName);
                if (child != null)
                {
                    child.localPosition = item.localPosition;
                    child.localRotation = item.localRotation;
                    child.localScale = item.localScale;
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"[RsTransformController] Child object not found: {item.childName}", this);
                }
            }

            UnityEngine.Debug.Log($"[RsTransformController] Loaded config from slot {slotIndex + 1}: {fullPath}", this);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[RsTransformController] Failed to load file: {e.Message}", this);
        }
    }

    public bool HasConfigFile(int slotIndex)
    {
        string configFileName = GetConfigFileName(slotIndex);
        if (string.IsNullOrEmpty(configFileName)) return false;

        string fileName = configFileName.EndsWith(".json") ? configFileName : configFileName + ".json";
        string fullPath = Path.Combine(SAVE_FOLDER_PATH, fileName);
        return File.Exists(fullPath);
    }
}