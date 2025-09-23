using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

public class CsvFolderAverager : MonoBehaviour
{
    [Header("検索するフォルダ")]
    public string folderPath = "Assets/HandTrakingData/Filter";

    [Header("CSV拡張子フィルタ")]
    public string searchPattern = "*.csv";

    void Start()
    {
        if (!Directory.Exists(folderPath))
        {
            UnityEngine.Debug.LogError("フォルダが見つかりません: " + folderPath);
            return;
        }

        string[] files = Directory.GetFiles(folderPath, searchPattern, SearchOption.TopDirectoryOnly);

        if (files.Length == 0)
        {
            UnityEngine.Debug.LogWarning("CSVファイルが見つかりません");
            return;
        }

        foreach (var path in files)
        {
            try
            {
                var lines = File.ReadAllLines(path).Skip(1);
                List<double[]> rows = new List<double[]>();

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var values = line.Split(',');

                    if (values.Length < 4) continue;

                    double frame = double.Parse(values[0]);
                    double procTime = double.Parse(values[1]);
                    double discarded = double.Parse(values[2]);
                    double total = double.Parse(values[3]);

                    rows.Add(new double[] { frame, procTime, discarded, total });
                }

                if (rows.Count == 0)
                {
                    UnityEngine.Debug.LogWarning($"データが存在しません: {path}");
                    continue;
                }

                double avgProcTime = rows.Average(r => r[1]);
                double avgDiscarded = rows.Average(r => r[2]);
                double avgTotal = rows.Average(r => r[3]);
                double avgRatio = rows.Average(r => r[2] / r[3]);

                /*
                UnityEngine.Debug.Log($"==== {Path.GetFileName(path)} ====");
                UnityEngine.Debug.Log($"ProcessingTime Avg   = {avgProcTime}");
                UnityEngine.Debug.Log($"DiscardedCount Avg   = {avgDiscarded}");
                UnityEngine.Debug.Log($"TotalCount Avg       = {avgTotal}");
                UnityEngine.Debug.Log($"Discarded/Total Avg  = {avgRatio}");
                */

                UnityEngine.Debug.Log($"==== {Path.GetFileName(path)} ====");
                UnityEngine.Debug.Log($"{avgProcTime}");
                UnityEngine.Debug.Log($"{avgDiscarded}");
                UnityEngine.Debug.Log($"{avgTotal}");
                UnityEngine.Debug.Log($"{avgRatio}");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"CSV処理中にエラー ({path}): {ex.Message}");
            }
        }
    }
}