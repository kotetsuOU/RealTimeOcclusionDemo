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
                    LoadPointsAndColorsFromFile(setting.filePath, setting.color, setting.useFileColor, allPoints, allColors);
                }
            }
        }
        return new PCV_Data(allPoints, allColors);
    }

    private static void LoadPointsAndColorsFromFile(string path, Color defaultColor, bool useFileColor, List<Vector3> positions, List<Color> colors)
    {
        if (!File.Exists(path))
        {
            UnityEngine.Debug.LogError($"ファイルが見つかりません: {path}");
            return;
        }

        if (Path.GetExtension(path).Equals(".ply", StringComparison.OrdinalIgnoreCase))
        {
            LoadPointsAndColorsFromPly(path, defaultColor, useFileColor, positions, colors);
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
                if (useFileColor && parts.Length >= 6 &&
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

    private static int GetPropertySize(string type)
    {
        switch (type)
        {
            case "char": case "uchar": case "int8": case "uint8": return 1;
            case "short": case "ushort": case "int16": case "uint16": return 2;
            case "int": case "uint": case "int32": case "uint32": case "float": case "float32": return 4;
            case "double": case "float64": return 8;
            default: return 0;
        }
    }

    private static void LoadPointsAndColorsFromPly(string path, Color defaultColor, bool useFileColor, List<Vector3> positions, List<Color> colors)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
        using (var reader = new BinaryReader(fs))
        {
            int vertexCount = 0;
            bool isBinary = false;
            bool readingVertex = false;

            int vertexByteSize = 0;
            int xOffset = -1, yOffset = -1, zOffset = -1;
            int rOffset = -1, gOffset = -1, bOffset = -1;

            string line;
            while ((line = ReadLine(fs)) != "end_header")
            {
                if (line.StartsWith("format binary")) isBinary = true;
                else if (line.StartsWith("element "))
                {
                    var parts = line.Split(' ');
                    if (parts.Length >= 3 && parts[1] == "vertex")
                    {
                        readingVertex = true;
                        int.TryParse(parts[2], out vertexCount);
                    }
                    else
                    {
                        readingVertex = false;
                    }
                }
                else if (readingVertex && line.StartsWith("property "))
                {
                    var parts = line.Split(' ');
                    if (parts.Length >= 3 && parts[1] != "list")
                    {
                        string type = parts[1];
                        string name = parts[2];
                        int size = GetPropertySize(type);

                        if (name == "x") xOffset = vertexByteSize;
                        else if (name == "y") yOffset = vertexByteSize;
                        else if (name == "z") zOffset = vertexByteSize;
                        else if (name == "red" || name == "r") rOffset = vertexByteSize;
                        else if (name == "green" || name == "g") gOffset = vertexByteSize;
                        else if (name == "blue" || name == "b") bOffset = vertexByteSize;

                        vertexByteSize += size;
                    }
                }
            }

            if (!isBinary)
            {
                UnityEngine.Debug.LogError($"[PCV_Loader] ASCII形式のPLYファイルには対応していません: {path}");
                return;
            }

            if (vertexCount <= 0 || vertexByteSize <= 0 || xOffset < 0 || yOffset < 0 || zOffset < 0) return;

            byte[] vData = new byte[vertexByteSize];
            for (int i = 0; i < vertexCount; i++)
            {
                int bytesRead = reader.Read(vData, 0, vertexByteSize);
                if (bytesRead < vertexByteSize) break;

                float x = BitConverter.ToSingle(vData, xOffset);
                float y = BitConverter.ToSingle(vData, yOffset);
                float z = BitConverter.ToSingle(vData, zOffset);

                byte r = (useFileColor && rOffset >= 0) ? vData[rOffset] : (byte)(defaultColor.r * 255);
                byte g = (useFileColor && gOffset >= 0) ? vData[gOffset] : (byte)(defaultColor.g * 255);
                byte b = (useFileColor && bOffset >= 0) ? vData[bOffset] : (byte)(defaultColor.b * 255);

                if (float.IsNaN(x) || float.IsInfinity(x) ||
                    float.IsNaN(y) || float.IsInfinity(y) ||
                    float.IsNaN(z) || float.IsInfinity(z))
                {
                    continue;
                }

                positions.Add(new Vector3(x, y, z));
                colors.Add(new Color(r / 255f, g / 255f, b / 255f));
            }
        }
    }

    private static string ReadLine(FileStream fs)
    {
        var chars = new List<char>();
        int b;
        while ((b = fs.ReadByte()) != -1)
        {
            char c = (char)b;
            if (c == '\n') break;
            if (c != '\r') chars.Add(c);
        }
        return new string(chars.ToArray());
    }
}