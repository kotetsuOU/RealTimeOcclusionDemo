using UnityEngine;
using UnityEngine.Rendering;
using System.IO;
using System;

public static class PCDOcclusionDebugExporter
{
    private const float RangeMin = 0.0f;
    private const float RangeMax = 1.0f;
    private const int Steps = 15;
    private const float StepSize = (RangeMax - RangeMin) / Steps;

    private static readonly Color[] Palette16 = new Color[16]
    {
        new Color(0.00f, 0.00f, 0.00f),
        new Color(0.00f, 0.00f, 0.50f), new Color(0.00f, 0.00f, 0.75f),
        new Color(0.00f, 0.00f, 1.00f), new Color(0.00f, 0.25f, 1.00f),
        new Color(0.00f, 0.50f, 1.00f), new Color(0.00f, 0.75f, 1.00f),
        new Color(0.00f, 1.00f, 1.00f), new Color(0.25f, 1.00f, 0.75f),
        new Color(0.50f, 1.00f, 0.50f), new Color(0.75f, 1.00f, 0.25f),
        new Color(1.00f, 1.00f, 0.00f), new Color(1.00f, 0.75f, 0.00f),
        new Color(1.00f, 0.50f, 0.00f), new Color(1.00f, 0.25f, 0.00f),
        new Color(1.00f, 0.00f, 0.00f)
    };


    public static void ExportOcclusionMap16PaletteFromData(float[] data, int width, int height, string savePath = "Assets/HandTrackingData/OcculusionMaps")
    {
        UnityEngine.Debug.Log($"[PCDOcclusionDebugExporter] Exporting Occlusion Map with 16-palette from data (width={width}, height={height})...");
        if (data == null || data.Length != width * height) return;

        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
        }

        string fileName = $"OcclusionMap_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        string fullPath = Path.Combine(savePath, fileName);

        float minV = float.PositiveInfinity;
        float maxV = float.NegativeInfinity;
        int[] hist = new int[16];
        int count = width * height;
        for (int i = 0; i < count; i++)
        {
            float v = data[i];
            if (float.IsNaN(v) || float.IsInfinity(v))
            {
                hist[0]++;
                continue;
            }

            if (v < minV) minV = v;
            if (v > maxV) maxV = v;

            int paletteIndex;
            if (v >= RangeMax)
            {
                paletteIndex = 15;
            }
            else if (v <= RangeMin)
            {
                paletteIndex = 1;
            }
            else
            {
                paletteIndex = 1 + Mathf.Clamp((int)((v - RangeMin) / StepSize), 0, Steps - 1);
            }
            hist[paletteIndex]++;
        }

        Debug.Log($"[PCDOcclusionDebugExporter] occlusion value range: min={minV}, max={maxV} (count={count})");
        Debug.Log($"[PCDOcclusionDebugExporter] hist(0..1.5 step0.1 + overflow, 16bin): [{string.Join(",", hist)}]");

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        Color[] pixels = new Color[width * height];

        for (int i = 0; i < width * height; i++)
        {
            float occlusionValue = data[i];

            int paletteIndex;
            if (float.IsNaN(occlusionValue) || float.IsInfinity(occlusionValue))
            {
                paletteIndex = 0;
            }
            else if (occlusionValue <= 0.0001f) // Treat exactly 0 or near-0 as black
            {
                pixels[i] = Color.white;
                continue;
            }
            else if (occlusionValue >= RangeMax)
            {
                paletteIndex = 15;
            }
            else if (occlusionValue <= RangeMin)
            {
                paletteIndex = 1;
            }
            else
            {
                paletteIndex = 1 + Mathf.Clamp((int)((occlusionValue - RangeMin) / StepSize), 0, Steps - 1);
            }
            pixels[i] = Palette16[paletteIndex];
        }

        tex.SetPixels(pixels);
        tex.Apply();

        byte[] pngBytes = tex.EncodeToPNG();
        File.WriteAllBytes(fullPath, pngBytes);

        UnityEngine.Object.Destroy(tex);
        Debug.Log($"[PCDOcclusionDebugExporter] Saved Occlusion Map with 16-palette to: {fullPath}");
    }
}
