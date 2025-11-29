using Intel.RealSense;
using System;
using System.IO;
using UnityEngine;
using static System.Net.Mime.MediaTypeNames;

[ProcessingBlockData(typeof(RsColorBasedDepthCulling))]
[HelpURL("https://github.com/IntelRealSense/librealsense/blob/master/doc/post-processing-filters.md")]
public class RsColorBasedDepthCulling : RsProcessingBlock
{
    [Header("Control")]
    public bool SaveDebugFrames = false;

    [Header("Save Settings")]
    public string DebugSavePath = "Assets/HandTrackingData/Color";

    [Header("HSV Thresholds (0.0 - 1.0)")]
    [Range(0f, 1f)] public float _minHue = 0.0f;
    [Range(0f, 1f)] public float _maxHue = 0.1f;
    [Range(0f, 1f)] public float _minSaturation = 0.1f;
    [Range(0f, 1f)] public float _maxSaturation = 1.0f;
    [Range(0f, 1f)] public float _minValue = 0.1f;
    [Range(0f, 1f)] public float _maxValue = 1.0f;

    private RsDepthToColorCalibration _calibration;
    private string _savePath;
    private Vector3 hsvCache;

    private readonly byte[][] _palette16 = new byte[][]
    {
        new byte[] {0, 0, 0},       // 0: Black
        new byte[] {0, 0, 255},     // 1: Blue
        new byte[] {0, 128, 255},   // 2
        new byte[] {0, 255, 255},   // 3: Cyan
        new byte[] {0, 255, 128},   // 4
        new byte[] {0, 255, 0},     // 5: Green
        new byte[] {128, 255, 0},   // 6
        new byte[] {255, 255, 0},   // 7: Yellow
        new byte[] {255, 128, 0},   // 8: Orange
        new byte[] {255, 0, 0},     // 9: Red
        new byte[] {255, 0, 128},   // 10
        new byte[] {255, 0, 255},   // 11: Magenta
        new byte[] {128, 0, 255},   // 12
        new byte[] {128, 128, 128}, // 13: Grey
        new byte[] {192, 192, 192}, // 14: Light Grey
        new byte[] {255, 255, 255}  // 15: White
    };

    public void SetCalibration(RsDepthToColorCalibration calib)
    {
        _calibration = calib;

        string rawPath = DebugSavePath;
        if (string.IsNullOrEmpty(rawPath))
        {
            rawPath = UnityEngine.Application.persistentDataPath;
        }
        else if (rawPath.StartsWith("Assets"))
        {
            string relative = rawPath.Substring("Assets".Length);
            if (relative.StartsWith("/") || relative.StartsWith("\\"))
            {
                relative = relative.Substring(1);
            }
            _savePath = Path.Combine(UnityEngine.Application.dataPath, relative);
        }
        else
        {
            _savePath = rawPath;
        }

        try
        {
            if (!Directory.Exists(_savePath))
            {
                Directory.CreateDirectory(_savePath);
                UnityEngine.Debug.Log($"[RsColorBasedDepthCulling] Created debug directory: {_savePath}");
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[RsColorBasedDepthCulling] Failed to create directory: {_savePath}. Error: {e.Message}");
            _savePath = UnityEngine.Application.persistentDataPath;
        }
    }

    public override Frame Process(Frame frame, FrameSource frameSource)
    {
        if (_calibration == null) return frame;

        if (frame.IsComposite)
        {
            using (var fs = FrameSet.FromFrame(frame))
            using (var colorFrame = fs.ColorFrame)
            using (var depthFrame = fs.DepthFrame)
            {
                if (colorFrame != null && depthFrame != null)
                {
                    if (SaveDebugFrames)
                    {
                        SaveDebugImages(colorFrame);
                        SaveDebugFrames = false;
                    }
                    CullDepthByColor(colorFrame, depthFrame);
                }
            }
        }
        return frame;
    }

    private void SaveDebugImages(VideoFrame colorFrame)
    {
        int width = colorFrame.Width;
        int height = colorFrame.Height;
        int bpp = colorFrame.BitsPerPixel / 8;

        if (bpp != 3)
        {
            UnityEngine.Debug.LogWarning("[RsColorBasedDepthCulling] Debug save supports only RGB8.");
            SaveDebugFrames = false;
            return;
        }

        int byteCount = width * height * bpp;

        byte[] rawRgbData = new byte[byteCount];
        colorFrame.CopyTo(rawRgbData);

        byte[] bgrOriginal = new byte[byteCount];
        byte[] hBuffer = new byte[byteCount];
        byte[] sBuffer = new byte[byteCount];
        byte[] vBuffer = new byte[byteCount];
        byte[] filteredBuffer = new byte[byteCount];

        Vector3 hsv;

        for (int i = 0; i < byteCount; i += 3)
        {
            byte r = rawRgbData[i];
            byte g = rawRgbData[i + 1];
            byte b = rawRgbData[i + 2];

            bgrOriginal[i] = b; // Blue
            bgrOriginal[i + 1] = g; // Green
            bgrOriginal[i + 2] = r; // Red

            RsColorSpaceHelper.RgbToHsv(r, g, b, out hsv);

            int idxH = Mathf.Clamp((int)(hsv.x * 16f), 0, 15);
            int idxS = Mathf.Clamp((int)(hsv.y * 16f), 0, 15);
            int idxV = Mathf.Clamp((int)(hsv.z * 16f), 0, 15);

            SetPixelBgr(hBuffer, i, _palette16[idxH]);
            SetPixelBgr(sBuffer, i, _palette16[idxS]);
            SetPixelBgr(vBuffer, i, _palette16[idxV]);

            bool isTarget = (hsv.x >= _minHue && hsv.x <= _maxHue) &&
                            (hsv.y >= _minSaturation && hsv.y <= _maxSaturation) &&
                            (hsv.z >= _minValue && hsv.z <= _maxValue);

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

        SaveBitmap(width, height, bgrOriginal, $"Debug_{timestamp}_1_Original.bmp");
        SaveBitmap(width, height, hBuffer, $"Debug_{timestamp}_2_Hue.bmp");
        SaveBitmap(width, height, sBuffer, $"Debug_{timestamp}_3_Sat.bmp");
        SaveBitmap(width, height, vBuffer, $"Debug_{timestamp}_4_Val.bmp");
        SaveBitmap(width, height, filteredBuffer, $"Debug_{timestamp}_5_Filtered.bmp");

        UnityEngine.Debug.Log($"[RsColorBasedDepthCulling] Saved debug images to: {_savePath}");
    }

    private void SetPixelBgr(byte[] buffer, int index, byte[] colorRgb)
    {
        buffer[index] = colorRgb[2]; // Blue
        buffer[index + 1] = colorRgb[1]; // Green
        buffer[index + 2] = colorRgb[0]; // Red
    }

    private void SetPixel(byte[] buffer, int index, byte[] color)
    {
        buffer[index] = color[0];
        buffer[index + 1] = color[1];
        buffer[index + 2] = color[2];
    }

    private void SaveBitmap(int width, int height, byte[] imageData, string filename)
    {
        if (string.IsNullOrEmpty(_savePath))
        {
            UnityEngine.Debug.LogError("[RsColorBasedDepthCulling] Save path is missing!");
            return;
        }

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

        string path = Path.Combine(_savePath, filename);
        File.WriteAllBytes(path, bmpBytes);
    }

    private unsafe void CullDepthByColor(VideoFrame colorFrame, DepthFrame depthFrame)
    {
        int width = colorFrame.Width;
        int height = colorFrame.Height;
        int depthPixels = depthFrame.Width * depthFrame.Height;

        byte* colorPtr = (byte*)colorFrame.Data;
        ushort* depthPtr = (ushort*)depthFrame.Data;

        for (int depthIdx = 0; depthIdx < depthPixels; depthIdx++)
        {
            ushort dVal = depthPtr[depthIdx];
            if (dVal == 0) continue;

            int dx = depthIdx % depthFrame.Width;
            int dy = depthIdx / depthFrame.Width;

            if (_calibration.MapDepthToColor(dx, dy, dVal, out int cx, out int cy))
            {
                int colorIdx = (cy * width + cx) * 3;
                if (colorIdx < 0 || colorIdx >= width * height * 3) continue;

                byte r = colorPtr[colorIdx];
                byte g = colorPtr[colorIdx + 1];
                byte b = colorPtr[colorIdx + 2];

                RsColorSpaceHelper.RgbToHsv(r, g, b, out hsvCache);

                bool isTarget = (hsvCache.x >= _minHue && hsvCache.x <= _maxHue) &&
                                (hsvCache.y >= _minSaturation && hsvCache.y <= _maxSaturation) &&
                                (hsvCache.z >= _minValue && hsvCache.z <= _maxValue);

                if (!isTarget) depthPtr[depthIdx] = 0;
            }
            else
            {
                depthPtr[depthIdx] = 0;
            }
        }
    }
}