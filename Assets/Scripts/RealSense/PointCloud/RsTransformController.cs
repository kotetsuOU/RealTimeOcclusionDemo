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
    [Tooltip("設定ファイルの保存名 (.json)。Assets/Configフォルダに保存されます。")]
    public string configFileName = "ChildTransforms.json";

    [Tooltip("起動時に自動的に設定をロードするかどうか")]
    public bool loadOnStart = false;

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
    // ---------------------

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

    public void SaveTransformConfig()
    {
        if (string.IsNullOrEmpty(configFileName))
        {
            UnityEngine.Debug.LogError("[RsTransformController] 設定ファイル名が指定されていません。", this);
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
            UnityEngine.Debug.Log($"[RsTransformController] Saved config to: {fullPath}", this);

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
        if (string.IsNullOrEmpty(configFileName))
        {
            UnityEngine.Debug.LogError("[RsTransformController] 設定ファイル名が指定されていません。", this);
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

            UnityEngine.Debug.Log($"[RsTransformController] Loaded config from: {fullPath}", this);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[RsTransformController] Failed to load file: {e.Message}", this);
        }
    }
}