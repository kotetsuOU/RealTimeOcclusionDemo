using UnityEngine;
using System.Collections.Generic;
using System.IO;


public static class PCV_Loader
{
    public static List<Vector3> LoadVerticesFromFile(string path)
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
                float.TryParse(parts[0], out float x) &&
                float.TryParse(parts[1], out float y) &&
                float.TryParse(parts[2], out float z))
            {
                vertices.Add(new Vector3(x, y, z));
            }
        }
        return vertices;
    }
}