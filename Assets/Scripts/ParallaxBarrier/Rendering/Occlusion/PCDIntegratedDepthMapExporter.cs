using System;
using System.IO;
using UnityEngine;

// 統合DepthMap（R32_UInt）を可視化画像＋可逆な生データとして保存するユーティリティ
public static class PCDIntegratedDepthMapExporter
{
    private const uint DepthMaxUInt = 0x7FFFFFFFu;

    public static void ExportIntegratedDepthMapFromData(uint[] data, int width, int height, string savePath = "Assets/HandTrackingData/DepthMaps/Integrated", string prefix = "")
    {
        if (data == null || data.Length != width * height)
        {
            Debug.LogWarning("[PCDIntegratedDepthMapExporter] Invalid depth data.");
            return;
        }

#if !UNITY_EDITOR
        savePath = Path.Combine(Application.persistentDataPath, "DepthMaps", "Integrated");
#endif

        Directory.CreateDirectory(savePath);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string baseName = $"IntegratedDepth_{prefix}_{timestamp}";
        string pngPath = Path.Combine(savePath, baseName + ".png");
        string rawPath = Path.Combine(savePath, baseName + ".raw32");
        string metaPath = Path.Combine(savePath, baseName + ".txt");

        uint minDepth = uint.MaxValue;
        uint maxDepth = 0u;
        int validCount = 0;

        for (int i = 0; i < data.Length; i++)
        {
            uint d = data[i];
            if (d >= DepthMaxUInt)
                continue;

            validCount++;
            if (d < minDepth) minDepth = d;
            if (d > maxDepth) maxDepth = d;
        }

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        Color32[] pixels = new Color32[data.Length];

        bool hasRange = validCount > 0 && maxDepth > minDepth;

        for (int i = 0; i < data.Length; i++)
        {
            uint d = data[i];
            if (d >= DepthMaxUInt)
            {
                pixels[i] = new Color32(255, 255, 255, 255); // 背景
                continue;
            }

            float t = hasRange ? (float)(d - minDepth) / (maxDepth - minDepth) : 0f;
            pixels[i] = EvaluateGradient(t);
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        File.WriteAllBytes(pngPath, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(tex);

        byte[] rawBytes = new byte[data.Length * sizeof(uint)];
        Buffer.BlockCopy(data, 0, rawBytes, 0, rawBytes.Length);
        File.WriteAllBytes(rawPath, rawBytes);

        string metadata =
            $"width={width}\n" +
            $"height={height}\n" +
            $"depthMaxUInt={DepthMaxUInt}\n" +
            $"validCount={validCount}\n" +
            $"minDepth={minDepth}\n" +
            $"maxDepth={maxDepth}\n" +
            "format=R32_UInt little-endian raw32\n";
        File.WriteAllText(metaPath, metadata);

        Debug.Log($"[PCDIntegratedDepthMapExporter] Saved integrated depth maps:\nPNG: {pngPath}\nRAW: {rawPath}\nMETA: {metaPath}");
    }

    private static Color32 EvaluateGradient(float t)
    {
        t = Mathf.Clamp01(t);

        if (t < 0.25f)
        {
            return LerpColor(new Color32(0, 0, 255, 255), new Color32(0, 255, 255, 255), t / 0.25f);
        }

        if (t < 0.5f)
        {
            return LerpColor(new Color32(0, 255, 255, 255), new Color32(0, 255, 0, 255), (t - 0.25f) / 0.25f);
        }

        if (t < 0.75f)
        {
            return LerpColor(new Color32(0, 255, 0, 255), new Color32(255, 255, 0, 255), (t - 0.5f) / 0.25f);
        }

        return LerpColor(new Color32(255, 255, 0, 255), new Color32(255, 0, 0, 255), (t - 0.75f) / 0.25f);
    }

    private static Color32 LerpColor(Color32 a, Color32 b, float t)
    {
        return new Color32(
            (byte)Mathf.RoundToInt(Mathf.Lerp(a.r, b.r, t)),
            (byte)Mathf.RoundToInt(Mathf.Lerp(a.g, b.g, t)),
            (byte)Mathf.RoundToInt(Mathf.Lerp(a.b, b.b, t)),
            255);
    }
}
