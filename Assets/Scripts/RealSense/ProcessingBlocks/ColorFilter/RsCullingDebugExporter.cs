using Intel.RealSense;
using System;
using System.IO;
using UnityEngine;
using static System.Net.Mime.MediaTypeNames;

public static class RsCullingDebugExporter
{
    private static readonly byte[][] _palette16 = new byte[][]
    {
        new byte[] {0, 0, 0},
        new byte[] {0, 0, 255},
        new byte[] {0, 128, 255},
        new byte[] {0, 255, 255},
        new byte[] {0, 255, 128},
        new byte[] {0, 255, 0},
        new byte[] {128, 255, 0},
        new byte[] {255, 255, 0},
        new byte[] {255, 128, 0},
        new byte[] {255, 0, 0},
        new byte[] {255, 0, 128},
        new byte[] {255, 0, 255},
        new byte[] {128, 0, 255},
        new byte[] {128, 128, 128},
        new byte[] {192, 192, 192},
        new byte[] {255, 255, 255}
    };

    public static string ResolveAndCreatePath(string rawPath)
    {
        string resolvedPath;

        if (string.IsNullOrEmpty(rawPath))
        {
            resolvedPath = UnityEngine.Application.persistentDataPath;
        }
        else if (rawPath.StartsWith("Assets"))
        {
            string relative = rawPath.Substring("Assets".Length);
            if (relative.StartsWith("/") || relative.StartsWith("\\"))
            {
                relative = relative.Substring(1);
            }
            resolvedPath = Path.Combine(UnityEngine.Application.dataPath, relative);
        }
        else
        {
            resolvedPath = rawPath;
        }

        try
        {
            if (!Directory.Exists(resolvedPath))
            {
                Directory.CreateDirectory(resolvedPath);
                UnityEngine.Debug.Log($"[RsCullingDebugExporter] Created directory: {resolvedPath}");
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[RsCullingDebugExporter] Failed to create directory. Fallback to persistentDataPath. Error: {e.Message}");
            resolvedPath = UnityEngine.Application.persistentDataPath;
        }

        return resolvedPath;
    }

    public static void SaveDebugImages(
        VideoFrame colorFrame,
        RsColorBasedDepthCulling.ConversionMode mode,
        string savePath,
        Func<byte, byte, byte, bool> isTargetPredicate,
        RsColorBasedDepthCulling.ColorVisualizationMode debugMode)
    {
        int width = colorFrame.Width;
        int height = colorFrame.Height;
        int bpp = colorFrame.BitsPerPixel / 8;

        if (bpp != 3)
        {
            UnityEngine.Debug.LogWarning("[RsCullingDebugExporter] Supports only RGB8.");
            return;
        }

        int byteCount = width * height * bpp;

        byte[] rawRgbData = new byte[byteCount];
        colorFrame.CopyTo(rawRgbData);

        byte[] bgrOriginal = new byte[byteCount];
        byte[] debug1 = new byte[byteCount];
        byte[] debug2 = new byte[byteCount];
        byte[] debug3 = new byte[byteCount];
        byte[] filteredBuffer = new byte[byteCount];

        Vector3 hsvCache;
        Vector3Int ycbcrCache;

        for (int i = 0; i < byteCount; i += 3)
        {
            byte r = rawRgbData[i];
            byte g = rawRgbData[i + 1];
            byte b = rawRgbData[i + 2];

            bgrOriginal[i] = b;
            bgrOriginal[i + 1] = g;
            bgrOriginal[i + 2] = r;

            if (mode == RsColorBasedDepthCulling.ConversionMode.HSV)
            {
                RsHsvConverter.RgbToHsv(r, g, b, out hsvCache);

                int idxH = Mathf.Clamp((int)(hsvCache.x * 16f), 0, 15);
                int idxS = Mathf.Clamp((int)(hsvCache.y * 16f), 0, 15);
                int idxV = Mathf.Clamp((int)(hsvCache.z * 16f), 0, 15);

                SetPixelBgr(debug1, i, _palette16[idxH]);
                SetPixelBgr(debug2, i, _palette16[idxS]);
                SetPixelBgr(debug3, i, _palette16[idxV]);
            }
            else
            {
                RsYCbCrConverter.RgbToYCbCr(r, g, b, out ycbcrCache);

                if (debugMode == RsColorBasedDepthCulling.ColorVisualizationMode.Palette16)
                {
                    int idxY = Mathf.Clamp(ycbcrCache.x / 16, 0, 15);
                    int idxCb = Mathf.Clamp(ycbcrCache.y / 16, 0, 15);
                    int idxCr = Mathf.Clamp(ycbcrCache.z / 16, 0, 15);

                    SetPixelBgr(debug1, i, _palette16[idxY]);
                    SetPixelBgr(debug2, i, _palette16[idxCb]);
                    SetPixelBgr(debug3, i, _palette16[idxCr]);
                }
                else
                {
                    SetPixelGrayscale(debug1, i, (byte)ycbcrCache.x);
                    SetPixelGrayscale(debug2, i, (byte)ycbcrCache.y);
                    SetPixelGrayscale(debug3, i, (byte)ycbcrCache.z);
                }
            }

            bool isTarget = isTargetPredicate(r, g, b);

            if (isTarget)
            {
                filteredBuffer[i] = b;
                filteredBuffer[i + 1] = g;
                filteredBuffer[i + 2] = r;
            }
            else
            {
                filteredBuffer[i] = 0;
                filteredBuffer[i + 1] = 0;
                filteredBuffer[i + 2] = 0;
            }
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string modeStr = debugMode == RsColorBasedDepthCulling.ColorVisualizationMode.Palette16 ? "P16" : "GS";

        SaveBitmap(width, height, bgrOriginal, savePath, $"Debug_{timestamp}_1_Original.bmp");

        if (mode == RsColorBasedDepthCulling.ConversionMode.HSV)
        {
            SaveBitmap(width, height, debug1, savePath, $"Debug_{timestamp}_2_Hue_P16.bmp");
            SaveBitmap(width, height, debug2, savePath, $"Debug_{timestamp}_3_Sat_P16.bmp");
            SaveBitmap(width, height, debug3, savePath, $"Debug_{timestamp}_4_Val_P16.bmp");
        }
        else
        {
            SaveBitmap(width, height, debug1, savePath, $"Debug_{timestamp}_2_Y_{modeStr}.bmp");
            SaveBitmap(width, height, debug2, savePath, $"Debug_{timestamp}_3_Cb_{modeStr}.bmp");
            SaveBitmap(width, height, debug3, savePath, $"Debug_{timestamp}_4_Cr_{modeStr}.bmp");
        }

        SaveBitmap(width, height, filteredBuffer, savePath, $"Debug_{timestamp}_5_Filtered_{mode}.bmp");

        UnityEngine.Debug.Log($"[RsCullingDebugExporter] Saved debug images ({mode}, {debugMode}) to: {savePath}");
    }

    private static void SetPixelBgr(byte[] buffer, int index, byte[] colorRgb)
    {
        buffer[index] = colorRgb[2];
        buffer[index + 1] = colorRgb[1];
        buffer[index + 2] = colorRgb[0];
    }

    private static void SetPixelGrayscale(byte[] buffer, int index, byte val)
    {
        buffer[index] = val;
        buffer[index + 1] = val;
        buffer[index + 2] = val;
    }

    private static void SaveBitmap(int width, int height, byte[] imageData, string dirPath, string filename)
    {
        if (string.IsNullOrEmpty(dirPath)) return;

        int dataSize = width * height * 3;
        int fileSize = 54 + dataSize;
        byte[] bmpBytes = new byte[fileSize];

        bmpBytes[0] = 0x42;
        bmpBytes[1] = 0x4D;
        BitConverter.GetBytes(fileSize).CopyTo(bmpBytes, 2);
        BitConverter.GetBytes(0).CopyTo(bmpBytes, 6);
        BitConverter.GetBytes(54).CopyTo(bmpBytes, 10);
        BitConverter.GetBytes(40).CopyTo(bmpBytes, 14);
        BitConverter.GetBytes(width).CopyTo(bmpBytes, 18);
        BitConverter.GetBytes(height).CopyTo(bmpBytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bmpBytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bmpBytes, 28);
        BitConverter.GetBytes(0).CopyTo(bmpBytes, 30);
        BitConverter.GetBytes(dataSize).CopyTo(bmpBytes, 34);

        Array.Copy(imageData, 0, bmpBytes, 54, dataSize);

        string path = Path.Combine(dirPath, filename);
        File.WriteAllBytes(path, bmpBytes);
    }
}