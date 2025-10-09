using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class PCV_VoxelCountExporter
{
    public static void Export(VoxelGrid voxelGrid)
    {
        if (voxelGrid == null)
        {
            UnityEngine.Debug.LogError("VoxelGridがnullです。Voxel数のエクスポートは実行不可能です。");
            return;
        }

        string path = EditorUtility.SaveFilePanel(
            "Voxelごとの点群数をCSVとして保存",
            "",
            "voxel_counts.csv",
            "csv");

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            var csv = new StringBuilder();
            csv.AppendLine("VoxelIndex_X,VoxelIndex_Y,VoxelIndex_Z,PointCount");

            foreach (var kvp in voxelGrid.Grid)
            {
                Vector3Int voxelIndex = kvp.Key;
                int pointCount = kvp.Value.Count;
                csv.AppendLine($"{voxelIndex.x},{voxelIndex.y},{voxelIndex.z},{pointCount}");
            }

            File.WriteAllText(path, csv.ToString());
            UnityEngine.Debug.Log($"Voxelごとの点群数が正常にエクスポートされました: {path}");
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Voxel数のエクスポートに失敗しました: {e.Message}");
        }
    }
}
