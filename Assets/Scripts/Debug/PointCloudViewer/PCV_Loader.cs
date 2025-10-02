using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;
using System.Globalization;

public static class PCV_Loader
{
    public static PCV_Data LoadFromFiles(FileSettings[] settings)
    {
        var allPoints = new List<Vector3>();
        var allColors = new List<Color>();

        if (settings != null)
        {
            foreach (var setting in settings)
            {
                if (setting.useFile && !string.IsNullOrEmpty(setting.filePath))
                {
                    LoadPointsAndColorsFromFile(setting.filePath, setting.color, allPoints, allColors);
                }
            }
        }
        return new PCV_Data(allPoints, allColors);
    }

    private static void LoadPointsAndColorsFromFile(string path, Color defaultColor, List<Vector3> positions, List<Color> colors)
    {
        if (!File.Exists(path))
        {
            UnityEngine.Debug.LogError($"ƒtƒ@ƒCƒ‹‚ªŒ©‚Â‚©‚è‚Ü‚¹‚ñ: {path}");
            return;
        }

        foreach (string line in File.ReadLines(path))
        {
            string[] parts = line.Split(',');

            float x, y, z;
            Color pointColor = defaultColor;

            if (parts.Length >= 3 &&
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) &&
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
            {
                if (parts.Length >= 6 &&
                    float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) &&
                    float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float g) &&
                    float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float b))
                {
                    pointColor = new Color(r / 255f, g / 255f, b / 255f);
                }

                positions.Add(new Vector3(x, y, z));
                colors.Add(pointColor);
            }
        }
    }
}