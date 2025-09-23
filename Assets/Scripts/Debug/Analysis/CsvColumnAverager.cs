using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class CsvMultiAverager : MonoBehaviour
{
    [Header("処理したいCSVファイルのリスト")]
    public string[] csvFilePaths;

    void Start()
    {
        if (csvFilePaths == null || csvFilePaths.Length == 0)
        {
            Debug.LogWarning("CSVファイルが指定されていません");
            return;
        }

        foreach (var path in csvFilePaths)
        {
            if (!File.Exists(path))
            {
                Debug.LogError("CSVファイルが見つかりません: " + path);
                continue;
            }

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
                    Debug.LogWarning($"データが存在しません: {path}");
                    continue;
                }

                double avgFrame = rows.Average(r => r[0]);
                double avgProcTime = rows.Average(r => r[1]);
                double avgDiscarded = rows.Average(r => r[2]);
                double avgTotal = rows.Average(r => r[3]);
                double avgRatio = rows.Average(r => r[2] / r[3]);

                Debug.Log($"==== {Path.GetFileName(path)} ====");
                Debug.Log($"Frame Avg            = {avgFrame}");
                Debug.Log($"ProcessingTime Avg   = {avgProcTime}");
                Debug.Log($"DiscardedCount Avg   = {avgDiscarded}");
                Debug.Log($"TotalCount Avg       = {avgTotal}");
                Debug.Log($"Discarded/Total Avg  = {avgRatio}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"CSV処理中にエラー ({path}): {ex.Message}");
            }
        }
    }
}