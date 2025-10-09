using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public enum FileType
{
    CSV,
    TXT
}

[AddComponentMenu("Tools/Folder Averager")]
public class FolderAverager : MonoBehaviour
{
    [Header("【機能説明】")]
    [Tooltip("指定されたフォルダ内のCSVまたはTXTファイルの平均値を計算し、コンソールに出力します。")]
    [TextArea(2, 4)]
    public string description = "下のプルダウンでファイル形式を選択し、ボタンを押して実行してください。";

    [Header("【設定項目】")]
    [Tooltip("処理するファイル形式（CSVまたはTXT）を選択します。")]
    public FileType fileTypeToProcess = FileType.CSV;

    [Tooltip("ファイルが格納されているフォルダのパスを指定します。")]
    public string folderPath = "Assets/HandTrakingData/Filter";

    [Header("【TXT用 閾値設定】")]
    [Tooltip("TXTファイル処理時に閾値フィルタを有効にするか。")]
    public bool useThreshold = false;

    [Tooltip("この値より小さいデータは計算から除外されます。")]
    public Vector3 minThreshold = new Vector3(-100f, -100f, -100f);

    [Tooltip("この値より大きいデータは計算から除外されます。")]
    public Vector3 maxThreshold = new Vector3(100f, 100f, 100f);


    public void CalculateAndLogAverages()
    {
        string searchPattern = (fileTypeToProcess == FileType.CSV) ? "*.csv" : "*.txt";

        if (!Directory.Exists(folderPath))
        {
            UnityEngine.Debug.LogError("指定されたフォルダが見つかりません: " + folderPath);
            return;
        }

        string[] files = Directory.GetFiles(folderPath, searchPattern, SearchOption.TopDirectoryOnly);

        if (files.Length == 0)
        {
            UnityEngine.Debug.LogWarning($"指定されたフォルダ内に{searchPattern}ファイルが見つかりません。");
            return;
        }

        UnityEngine.Debug.Log($"[{files.Length}]個の{fileTypeToProcess}ファイルの処理を開始します...");

        foreach (var path in files)
        {
            try
            {
                switch (fileTypeToProcess)
                {
                    case FileType.CSV:
                        ProcessCsvFile(path);
                        break;
                    case FileType.TXT:
                        ProcessTxtFile(path);
                        break;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"ファイルの処理中にエラーが発生しました ({Path.GetFileName(path)}): {ex.Message}");
            }
        }
        UnityEngine.Debug.Log("すべてのファイルの処理が完了しました。");
    }

    private void ProcessCsvFile(string path)
    {
        var lines = File.ReadAllLines(path).Skip(1);
        var rows = new List<double[]>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var values = line.Split(',');
            if (values.Length < 4) continue;

            rows.Add(new double[] {
                double.Parse(values[0]), double.Parse(values[1]),
                double.Parse(values[2]), double.Parse(values[3])
            });
        }

        if (rows.Count == 0)
        {
            UnityEngine.Debug.LogWarning($"有効なデータが存在しません: {Path.GetFileName(path)}");
            return;
        }

        double avgProcTime = rows.Average(r => r[1]);
        double avgDiscarded = rows.Average(r => r[2]);
        double avgTotal = rows.Average(r => r[3]);
        double avgRatio = rows.Average(r => (r[3] == 0) ? 0 : r[2] / r[3]);

        UnityEngine.Debug.Log($"==== {Path.GetFileName(path)} (CSV) ====");
        UnityEngine.Debug.Log($"ProcessingTime Avg: {avgProcTime}");
        UnityEngine.Debug.Log($"DiscardedCount Avg: {avgDiscarded}");
        UnityEngine.Debug.Log($"TotalCount Avg: {avgTotal}");
        UnityEngine.Debug.Log($"Discarded/Total Avg: {avgRatio}");
    }

    private void ProcessTxtFile(string path)
    {
        var lines = File.ReadAllLines(path);
        var vectors = new List<Vector3>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var values = line.Split(',');
            if (values.Length < 3) continue;

            vectors.Add(new Vector3(
                float.Parse(values[0]), float.Parse(values[1]), float.Parse(values[2])
            ));
        }

        int originalCount = vectors.Count;
        List<Vector3> filteredVectors = vectors;

        if (useThreshold)
        {
            filteredVectors = vectors.Where(v =>
                v.x >= minThreshold.x && v.x <= maxThreshold.x &&
                v.y >= minThreshold.y && v.y <= maxThreshold.y &&
                v.z >= minThreshold.z && v.z <= maxThreshold.z
            ).ToList();

            UnityEngine.Debug.Log($"閾値フィルタリング ({Path.GetFileName(path)}): {originalCount}件 → {filteredVectors.Count}件");
        }

        if (filteredVectors.Count == 0)
        {
            UnityEngine.Debug.LogWarning($"有効なデータが存在しません（フィルタ後）: {Path.GetFileName(path)}");
            return;
        }

        float avgX = filteredVectors.Average(v => v.x);
        float avgY = filteredVectors.Average(v => v.y);
        float avgZ = filteredVectors.Average(v => v.z);

        UnityEngine.Debug.Log($"==== {Path.GetFileName(path)} (TXT) ====");
        UnityEngine.Debug.Log($"Average X: {avgX}");
        UnityEngine.Debug.Log($"Average Y: {avgY}");
        UnityEngine.Debug.Log($"Average Z: {avgZ}");
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(FolderAverager))]
[CanEditMultipleObjects]
public class FolderAveragerEditor : Editor
{
    // SerializedProperty を使って変数を扱うことで、UndoやPrefabの上書きなどが正しく機能する
    SerializedProperty fileTypeProp;
    SerializedProperty folderPathProp;
    SerializedProperty useThresholdProp;
    SerializedProperty minThresholdProp;
    SerializedProperty maxThresholdProp;

    void OnEnable()
    {
        // インスペクターで表示・編集するプロパティ（変数）を名前で取得
        fileTypeProp = serializedObject.FindProperty("fileTypeToProcess");
        folderPathProp = serializedObject.FindProperty("folderPath");
        useThresholdProp = serializedObject.FindProperty("useThreshold");
        minThresholdProp = serializedObject.FindProperty("minThreshold");
        maxThresholdProp = serializedObject.FindProperty("maxThreshold");
    }
    
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("【機能説明】", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("指定されたフォルダ内のCSVまたはTXTファイルの平均値を計算し、コンソールに出力します。", MessageType.Info);
        
        EditorGUILayout.Space(10);
        
        EditorGUILayout.PropertyField(fileTypeProp, new GUIContent("処理するファイル形式"));
        EditorGUILayout.PropertyField(folderPathProp, new GUIContent("フォルダのパス"));
        
        if (fileTypeProp.enumValueIndex == (int)FileType.TXT)
        {
            EditorGUILayout.Space(10);
            
            EditorGUILayout.PropertyField(useThresholdProp, new GUIContent("閾値を使用する"));

            if (useThresholdProp.boolValue)
            {
                EditorGUILayout.PropertyField(minThresholdProp, new GUIContent("最小閾値 (X, Y, Z)"));
                EditorGUILayout.PropertyField(maxThresholdProp, new GUIContent("最大閾値 (X, Y, Z)"));
            }
        }
        
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(15);
        GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);

        FolderAverager script = (FolderAverager)target;
        string buttonText = $"{script.fileTypeToProcess} ファイルの平均値を計算";

        if (GUILayout.Button(buttonText))
        {
            foreach (var obj in targets)
            {
                FolderAverager s = (FolderAverager)obj;
                s.CalculateAndLogAverages();
            }
        }
        GUI.backgroundColor = Color.white;
    }
}
#endif