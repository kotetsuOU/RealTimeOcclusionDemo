using UnityEngine;
using UnityEngine.Rendering;
using System.IO;
using System;

// オクルージョン値（浮動小数点数）のマップを、可視化しやすい16色のパレット形式でPNG画像として書き出すためのユーティリティ
public static class PCDOcclusionDebugExporter
{
    public static void ExportNeighborhoodMapFromData(int[] data, int width, int height, string savePath = "Assets/HandTrackingData/NeighborhoodMaps", string prefix = "", bool isNeighborCount = false)
    {
        UnityEngine.Debug.Log($"[PCDOcclusionDebugExporter] Exporting Neighborhood Map from data (width={width}, height={height})...");
        if (data == null || data.Length != width * height) return;

#if !UNITY_EDITOR
        savePath = Path.Combine(Application.persistentDataPath, "NeighborhoodMaps");
#endif

        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"NeighborhoodMap_{prefix}_{timestamp}.png";
        string csvFileName = $"NeighborhoodData_{prefix}_{timestamp}.csv";
        string fullPath = Path.Combine(savePath, fileName);
        string csvFullPath = Path.Combine(savePath, csvFileName);

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        Color[] pixels = new Color[width * height];

        int count = width * height;
        int maxL = 0;
        int minL = int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (data[i] > maxL) maxL = data[i];
            if (data[i] < minL && data[i] >= 0) minL = data[i];
        }

        for (int i = 0; i < count; i++)
        {
            int val = data[i];
            if (val < 0)
            {
                pixels[i] = Color.black; // 背景等でスキップされた値
            }
            else
            {
                if (isNeighborCount)
                {
                    // log10(val + 1) を用いる
                    float logV = Mathf.Log10(val + 1);
                    float maxLog = Mathf.Log10(Mathf.Max(maxL, 50) + 1); // 分母の最大値（最低50を想定）
                    float t = Mathf.Clamp01(logV / maxLog);

                    // 青(0) -> シアン -> 緑 -> 黄 -> 赤(大)
                    if (val == 0) pixels[i] = Color.blue;
                    else if (t < 0.25f) pixels[i] = Color.Lerp(Color.blue, Color.cyan, t / 0.25f);
                    else if (t < 0.5f) pixels[i] = Color.Lerp(Color.cyan, Color.green, (t - 0.25f) / 0.25f);
                    else if (t < 0.75f) pixels[i] = Color.Lerp(Color.green, Color.yellow, (t - 0.5f) / 0.25f);
                    else pixels[i] = Color.Lerp(Color.yellow, Color.red, (t - 0.75f) / 0.25f);
                }
                else
                {
                    // NeighborhoodSize (level) の場合は 0～5 程度なので離散的な色分け
                    int level = val;
                    if (level == 0) pixels[i] = Color.blue;
                    else if (level == 1) pixels[i] = Color.cyan;
                    else if (level == 2) pixels[i] = Color.green;
                    else if (level == 3) pixels[i] = Color.yellow;
                    else if (level == 4) pixels[i] = new Color(1.0f, 0.5f, 0.0f); // orange
                    else pixels[i] = Color.red; // 5以上
                }
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        byte[] pngBytes = tex.EncodeToPNG();
        File.WriteAllBytes(fullPath, pngBytes);

        try
        {
            using (StreamWriter writer = new StreamWriter(csvFullPath))
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        writer.Write(data[y * width + x].ToString());
                        if (x < width - 1)
                        {
                            writer.Write(",");
                        }
                    }
                    writer.WriteLine();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PCDOcclusionDebugExporter] Failed to save CSV: {ex.Message}");
        }

        UnityEngine.Object.Destroy(tex);
        Debug.Log($"[PCDOcclusionDebugExporter] Saved Neighborhood Map to: {fullPath}");
        Debug.Log($"[PCDOcclusionDebugExporter] Saved Neighborhood Data (CSV) to: {csvFullPath}");
    }

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
    public static void ExportOcclusionMap16PaletteFromData(float[] data, float[] rawData, int width, int height, string savePath = "Assets/HandTrackingData/OcclusionMaps", string prefix = "", bool preferRawValuesInCsv = false)
    {
        UnityEngine.Debug.Log($"[PCDOcclusionDebugExporter] Exporting Occlusion Map with 16-palette from data (width={width}, height={height})...");
        if (data == null || data.Length != width * height) return;

#if !UNITY_EDITOR
        savePath = Path.Combine(Application.persistentDataPath, "OcclusionMaps");
#endif

        // 保存先のディレクトリが存在しない場合は作成
        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"OcclusionMap_{prefix}_{timestamp}.png";
        string csvFileName = $"OcclusionData_{prefix}_{timestamp}.csv";
        string fullPath = Path.Combine(savePath, fileName);
        string csvFullPath = Path.Combine(savePath, csvFileName);

        // 基本的な統計情報（最大値、最小値、ヒストグラム）を計算
        float minV = float.PositiveInfinity;
        float maxV = float.NegativeInfinity;
        int[] hist = new int[16];
        int count = width * height;
        int virtualOcclusionCount = 0;
        int backgroundCount = 0;
        int realPointVisibleCount = 0;
        int cyanClassCount = 0;

        for (int i = 0; i < count; i++)
        {
            float v = data[i];
            float rawV = (rawData != null && rawData.Length == count) ? rawData[i] : v;

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

            if (v <= -2.5f) // 実点群(可視) (-3.0)
            {
                realPointVisibleCount++;
                continue;
            }

            if (v <= -1.5f) // -2.0（Tagスキップ or Tag OFF時の遮蔽された実点群）
            {
                cyanClassCount++;
                continue;
            }

            if (v < -0.5f) // 背景・穴 (-1.0)
            {
                backgroundCount++;
                continue;
            }

            if (rawV < minV) minV = rawV;
            if (rawV > maxV) maxV = rawV;

            int paletteIndex;
            if (rawV >= RangeMax)
            {
                paletteIndex = 15;
            }
            else if (rawV <= RangeMin)
            {
                paletteIndex = 1;
            }
            else
            {
                paletteIndex = 1 + Mathf.Clamp((int)((rawV - RangeMin) / StepSize), 0, Steps - 1);
            }
            hist[paletteIndex]++;
        }

        Debug.Log($"[PCDOcclusionDebugExporter] occlusion value range: min={minV}, max={maxV} (count={count}, virtualObj={virtualOcclusionCount}, bg={backgroundCount}, realPointVisible={realPointVisibleCount}, cyanClassCount={cyanClassCount})");
        Debug.Log($"[PCDOcclusionDebugExporter] hist(0..1.0 step, 16bin): [{string.Join(",", hist)}]");

        // 画像に書き込むためのテクスチャを生成
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        Color[] pixels = new Color[width * height];

        // 各ピクセルに色を割り当てる
        for (int i = 0; i < width * height; i++)
        {
            float occlusionValue = data[i];
            float rawV = (rawData != null && rawData.Length == count) ? rawData[i] : occlusionValue;

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
            else if (occlusionValue <= -2.5f) // 実点群(可視) (-3.0)
            {
                pixels[i] = Color.green; // 実点群(可視)は緑
                continue;
            }
            else if (occlusionValue <= -1.5f) // -2.0（Tagスキップ or Tag OFF時の遮蔽された実点群）
            {
                pixels[i] = Color.cyan; // シアンで識別
                continue;
            }
            else if (occlusionValue < -0.5f) // 背景・穴 (-1.0)
            {
                pixels[i] = Color.white; // 背景・穴は白
                continue;
            }
            else if (rawV <= 0.0001f) // Treat exactly 0 or near-0 as black (グレーで表示して区別)
            {
                pixels[i] = Color.gray;
                continue;
            }

            int paletteIndex;
            if (rawV >= RangeMax)
            {
                paletteIndex = 15;
            }
            else if (rawV <= RangeMin)
            {
                paletteIndex = 1;
            }
            else
            {
                paletteIndex = 1 + Mathf.Clamp((int)((rawV - RangeMin) / StepSize), 0, Steps - 1);
            }
            pixels[i] = Palette16[paletteIndex];
        }

        tex.SetPixels(pixels);
        tex.Apply();

        // PNGとしてエンコードして保存
        byte[] pngBytes = tex.EncodeToPNG();
        File.WriteAllBytes(fullPath, pngBytes);

        // 実際のオクルージョン値をCSVとして保存（Excel等で確認可能）
        try
        {
            using (StreamWriter writer = new StreamWriter(csvFullPath))
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float val;
                        float labelVal = data[y * width + x];
                        float rawVal = (rawData != null && rawData.Length == width * height) ? rawData[y * width + x] : labelVal;

                        if (preferRawValuesInCsv)
                        {
                            val = rawVal;
                        }
                        else
                        {
                            val = labelVal;
                        }
                        writer.Write(val.ToString("F3"));
                        if (x < width - 1)
                        {
                            writer.Write(",");
                        }
                    }
                    writer.WriteLine();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PCDOcclusionDebugExporter] Failed to save CSV: {ex.Message}");
        }

        // テクスチャを破棄してメモリリークを防ぐ
        UnityEngine.Object.Destroy(tex);
        Debug.Log($"[PCDOcclusionDebugExporter] Saved Occlusion Map with 16-palette to: {fullPath}");
        Debug.Log($"[PCDOcclusionDebugExporter] Saved Occlusion Data (CSV) to: {csvFullPath}");
    }
}
