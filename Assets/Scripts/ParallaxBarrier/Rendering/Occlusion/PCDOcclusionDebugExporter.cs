using UnityEngine;
using UnityEngine.Rendering;
using System.IO;
using System;

// オクルージョン値（浮動小数点数）のマップを、可視化しやすい16色のパレット形式でPNG画像として書き出すためのユーティリティ
public static class PCDOcclusionDebugExporter
{
    private const float RangeMin = 0.0f;
    private const float RangeMax = 1.0f;
    private const int Steps = 15;
    private const float StepSize = (RangeMax - RangeMin) / Steps;

    // オクルージョン値をマッピングするための16色パレット（値が大きくなるほど赤系へ変化）
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


    // CPU側に読み戻されたオクルージョン値の配列（data）を画像としてディスクに保存する
    public static void ExportOcclusionMap16PaletteFromData(float[] data, int width, int height, string savePath = "Assets/HandTrackingData/OcculusionMaps")
    {
        UnityEngine.Debug.Log($"[PCDOcclusionDebugExporter] Exporting Occlusion Map with 16-palette from data (width={width}, height={height})...");
        if (data == null || data.Length != width * height) return;

        // 保存先のディレクトリが存在しない場合は作成
        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
        }

        string fileName = $"OcclusionMap_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        string fullPath = Path.Combine(savePath, fileName);

        // 基本的な統計情報（最大値、最小値、ヒストグラム）を計算
        float minV = float.PositiveInfinity;
        float maxV = float.NegativeInfinity;
        int[] hist = new int[16];
        int count = width * height;
        int virtualOcclusionCount = 0;
        int backgroundCount = 0;

        for (int i = 0; i < count; i++)
        {
            float v = data[i];
            if (float.IsNaN(v) || float.IsInfinity(v))
            {
                hist[0]++;
                continue;
            }

            if (v >= 1.9f) // 仮想オブジェクト (2.0)
            {
                virtualOcclusionCount++;
                continue;
            }

            if (v < -0.5f) // 背景・穴 (-1.0)
            {
                backgroundCount++;
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

        Debug.Log($"[PCDOcclusionDebugExporter] occlusion value range: min={minV}, max={maxV} (count={count}, virtualObj={virtualOcclusionCount}, bg={backgroundCount})");
        Debug.Log($"[PCDOcclusionDebugExporter] hist(0..1.0 step, 16bin): [{string.Join(",", hist)}]");

        // 画像に書き込むためのテクスチャを生成
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        Color[] pixels = new Color[width * height];

        // 各ピクセルに色を割り当てる
        for (int i = 0; i < width * height; i++)
        {
            float occlusionValue = data[i];

            if (float.IsNaN(occlusionValue) || float.IsInfinity(occlusionValue))
            {
                pixels[i] = Palette16[0];
                continue;
            }
            else if (occlusionValue >= 1.9f) // 仮想オブジェクトによる隠蔽 (2.0)
            {
                pixels[i] = Color.magenta; // 仮想オブジェクトはマゼンタ(ピンク)で識別
                continue;
            }
            else if (occlusionValue < -0.5f) // 背景・穴 (-1.0)
            {
                pixels[i] = Color.white; // 背景・穴は白
                continue;
            }
            else if (occlusionValue <= 0.0001f) // Treat exactly 0 or near-0 as black (グレーで表示して区別)
            {
                pixels[i] = Color.gray;
                continue;
            }

            int paletteIndex;
            if (occlusionValue >= RangeMax)
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

        // PNGとしてエンコードして保存
        byte[] pngBytes = tex.EncodeToPNG();
        File.WriteAllBytes(fullPath, pngBytes);

        // テクスチャを破棄してメモリリークを防ぐ
        UnityEngine.Object.Destroy(tex);
        Debug.Log($"[PCDOcclusionDebugExporter] Saved Occlusion Map with 16-palette to: {fullPath}");
    }
}
