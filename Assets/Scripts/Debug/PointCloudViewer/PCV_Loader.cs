using UnityEngine;
using System.Collections.Generic;
using System.IO;

public static class PCV_Loader
{
    public static PCV_Data LoadFromFiles(PointCloudViewer.FileSettings[] settings)
    {
        var allPoints = new List<Vector3>();
        var allColors = new List<Color>();

        if (settings != null)
        {
            foreach (var setting in settings)
            {
                if (setting.useFile && !string.IsNullOrEmpty(setting.filePath))
                {
                    AddPointsWithColor(setting.filePath, setting.color, allPoints, allColors);
                }
            }
        }
        return new PCV_Data(allPoints, allColors);
    }

    private static void AddPointsWithColor(string path, Color color, List<Vector3> positions, List<Color> colors)
    {
        List<Vector3> loadedVerts = LoadVerticesFromFile(path);
        positions.AddRange(loadedVerts);
        for (int i = 0; i < loadedVerts.Count; i++)
        {
            colors.Add(color);
        }
    }

    private static List<Vector3> LoadVerticesFromFile(string path)
    {
        var vertices = new List<Vector3>();
        if (!File.Exists(path))
        {
            UnityEngine.Debug.LogError($"ƒtƒ@ƒCƒ‹‚ªŒ©‚Â‚©‚è‚Ü‚¹‚ñ: {path}");
            return vertices;
        }

        foreach (string line in File.ReadLines(path))
        {
            string[] parts = line.Split(',');
            if (parts.Length == 3 &&
                float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
            {
                vertices.Add(new Vector3(x, y, z));
            }
        }
        return vertices;
    }
}