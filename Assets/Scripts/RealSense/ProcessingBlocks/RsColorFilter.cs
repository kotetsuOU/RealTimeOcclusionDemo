using Intel.RealSense;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[CreateAssetMenu(menuName = "RealSense/Processing Blocks/Custom/Color Filter")]
[ProcessingBlockData(typeof(RsColorFilter))]
[HelpURL("https://github.com/IntelRealSense/librealsense/blob/master/doc/post-processing-filters.md")]
public class RsColorFilter : RsProcessingBlock
{
    [Header("HSV Thresholds (0.0 - 1.0)")]
    [Range(0f, 1f)]
    public float _minHue = 0.0f;
    [Range(0f, 1f)]
    public float _maxHue = 0.1f;

    [Range(0f, 1f)]
    public float _minSaturation = 0.1f;
    [Range(0f, 1f)]
    public float _maxSaturation = 1.0f;

    [Range(0f, 1f)]
    public float _minValue = 0.1f;
    [Range(0f, 1f)]
    public float _maxValue = 1.0f;

    [Header("Debug")]
    [Tooltip("Check this to save Original, H, S, V, and Filtered images.")]
    public bool SaveDebugFrames = false;

    private byte[] _cpuDataBuffer;

    // 16段階のカラーパレット (R, G, B)
    private byte[][] _palette16;

    void OnEnable()
    {
        // 16色のグラデーションパレットを作成 (青 -> 水色 -> 緑 -> 黄 -> 赤)
        // 数値の低いほうが青、高いほうが赤で見やすくする
        _palette16 = new byte[16][];
        for (int i = 0; i < 16; i++)
        {
            float t = i / 15f; // 0.0 to 1.0
            Color c = Color.HSVToRGB((1f - t) * 0.66f, 1f, 1f); // HSV色空間を使って青(0.66)から赤(0.0)へのグラデーションを作成
            _palette16[i] = new byte[] { (byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255) };
        }
    }

    void OnDisable()
    {
        _cpuDataBuffer = null;
    }

    Frame ApplyFilter(VideoFrame colorFrame, FrameSource frameSource)
    {
        using (var p = colorFrame.Profile.As<VideoStreamProfile>())
        {
            if (p.Stream != Intel.RealSense.Stream.Color) return colorFrame;

            int width = colorFrame.Width;
            int height = colorFrame.Height;
            int bpp = colorFrame.BitsPerPixel / 8;

            if (bpp != 3) return colorFrame;

            int byteCount = width * height * bpp;
            if (_cpuDataBuffer == null || _cpuDataBuffer.Length != byteCount)
            {
                _cpuDataBuffer = new byte[byteCount];
            }

            // 1. Originalデータのコピー
            colorFrame.CopyTo(_cpuDataBuffer);

            // --- デバッグ準備 ---
            bool doSave = SaveDebugFrames;
            byte[] hBuffer = null;
            byte[] sBuffer = null;
            byte[] vBuffer = null;

            if (doSave)
            {
                // Originalを保存
                SaveBitmap(width, height, _cpuDataBuffer, "Debug_1_Original.bmp");

                // H, S, V 用のバッファ確保
                hBuffer = new byte[byteCount];
                sBuffer = new byte[byteCount];
                vBuffer = new byte[byteCount];
            }
            // ------------------

            Vector3 hsv;

            for (int i = 0; i < byteCount; i += 3)
            {
                byte r = _cpuDataBuffer[i];
                byte g = _cpuDataBuffer[i + 1];
                byte b = _cpuDataBuffer[i + 2];

                RsColorSpaceHelper.RgbToHsv(r, g, b, out hsv);

                // --- デバッグ用: 16段階量子化と着色 ---
                if (doSave)
                {
                    // H, S, V それぞれを 0-15 のインデックスに変換
                    int idxH = Mathf.Clamp((int)(hsv.x * 16f), 0, 15);
                    int idxS = Mathf.Clamp((int)(hsv.y * 16f), 0, 15);
                    int idxV = Mathf.Clamp((int)(hsv.z * 16f), 0, 15);

                    // パレットから色を取得してバッファにセット
                    SetPixel(hBuffer, i, _palette16[idxH]);
                    SetPixel(sBuffer, i, _palette16[idxS]);
                    SetPixel(vBuffer, i, _palette16[idxV]);
                }
                // ------------------------------------

                // フィルタリング処理
                bool isSkin = (hsv.x >= _minHue && hsv.x <= _maxHue) &&
                              (hsv.y >= _minSaturation && hsv.y <= _maxSaturation) &&
                              (hsv.z >= _minValue && hsv.z <= _maxValue);

                if (!isSkin)
                {
                    _cpuDataBuffer[i] = 0;
                    _cpuDataBuffer[i + 1] = 0;
                    _cpuDataBuffer[i + 2] = 0;
                }
            }

            // --- デバッグ保存実行 ---
            if (doSave)
            {
                SaveDebugFrames = false;
                SaveBitmap(width, height, hBuffer, "Debug_2_Hue_16Steps.bmp");
                SaveBitmap(width, height, sBuffer, "Debug_3_Sat_16Steps.bmp");
                SaveBitmap(width, height, vBuffer, "Debug_4_Val_16Steps.bmp");
                SaveBitmap(width, height, _cpuDataBuffer, "Debug_5_Filtered.bmp");

                UnityEngine.Debug.Log("[RsColorFilter] Saved 5 debug images (Original, H, S, V, Filtered).");
            }
            // ----------------------

            var newFrame = frameSource.AllocateVideoFrame<VideoFrame>(colorFrame.Profile, colorFrame, bpp * 8, width, height, colorFrame.Stride, Extension.VideoFrame);
            newFrame.CopyFrom(_cpuDataBuffer);

            return newFrame;
        }
    }

    private void SetPixel(byte[] buffer, int offset, byte[] color)
    {
        buffer[offset] = color[0];     // R
        buffer[offset + 1] = color[1]; // G
        buffer[offset + 2] = color[2]; // B
    }

    public override Frame Process(Frame frame, FrameSource frameSource)
    {
        if (frame.IsComposite)
        {
            using (var fs = FrameSet.FromFrame(frame))
            {
                VideoFrame colorFrame = fs.ColorFrame as VideoFrame;
                if (colorFrame == null) return frame;

                using (var filteredColorFrame = ApplyFilter(colorFrame, frameSource))
                {
                    var frames = new List<Frame>();
                    foreach (var f in fs)
                    {
                        using (var p1 = f.Profile)
                        {
                            if (p1.Stream == Intel.RealSense.Stream.Color)
                            {
                                f.Dispose();
                                continue;
                            }
                        }
                        frames.Add(f);
                    }
                    frames.Add(filteredColorFrame);
                    var res = frameSource.AllocateCompositeFrame(frames);
                    frames.ForEach(f => f.Dispose());
                    using (res) return res.AsFrame();
                }
            }
        }
        if (frame is VideoFrame) return ApplyFilter(frame as VideoFrame, frameSource);
        return frame;
    }

    private void SaveBitmap(int width, int height, byte[] rgbData, string fileName)
    {
        try
        {
            string path = Path.Combine("Assets/HandTrakingData/Color", fileName);
            int headerSize = 54;
            int stride = width * 3;
            int paddedStride = (stride + 3) & (~3);
            int fileSize = headerSize + (paddedStride * height);
            byte[] bmpBytes = new byte[fileSize];

            bmpBytes[0] = 0x42; bmpBytes[1] = 0x4D;
            BitConverter.GetBytes(fileSize).CopyTo(bmpBytes, 2);
            BitConverter.GetBytes(54).CopyTo(bmpBytes, 10);
            BitConverter.GetBytes(40).CopyTo(bmpBytes, 14);
            BitConverter.GetBytes(width).CopyTo(bmpBytes, 18);
            BitConverter.GetBytes(height).CopyTo(bmpBytes, 22);
            BitConverter.GetBytes((short)1).CopyTo(bmpBytes, 26);
            BitConverter.GetBytes((short)24).CopyTo(bmpBytes, 28);

            int pixelOffset = 54;
            for (int y = 0; y < height; y++)
            {
                int srcRowIndex = (height - 1 - y);
                int srcOffset = srcRowIndex * stride;
                int dstOffset = pixelOffset + (y * paddedStride);
                for (int x = 0; x < width; x++)
                {
                    int s = srcOffset + (x * 3);
                    int d = dstOffset + (x * 3);
                    bmpBytes[d] = rgbData[s + 2];     // B
                    bmpBytes[d + 1] = rgbData[s + 1]; // G
                    bmpBytes[d + 2] = rgbData[s];     // R
                }
            }
            File.WriteAllBytes(path, bmpBytes);
        }
        catch (Exception e) { UnityEngine.Debug.LogError($"Error saving {fileName}: {e.Message}"); }
    }
}