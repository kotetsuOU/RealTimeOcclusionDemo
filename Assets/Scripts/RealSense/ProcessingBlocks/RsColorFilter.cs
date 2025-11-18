using Intel.RealSense;
using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "RealSense/Processing Blocks/Custom/Color Filter")]
[ProcessingBlockData(typeof(RsColorFilter))]
[HelpURL("https" + "://github.com/IntelRealSense/librealsense/blob/master/doc/post-processing-filters.md")]
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

    private byte[] _cpuDataBuffer;

    void OnDisable()
    {
        _cpuDataBuffer = null;
    }

    Frame ApplyFilter(VideoFrame colorFrame, FrameSource frameSource)
    {
        using (var p = colorFrame.Profile.As<VideoStreamProfile>())
        {
            if (p.Stream != Intel.RealSense.Stream.Color)
            {
                return colorFrame;
            }

            int width = colorFrame.Width;
            int height = colorFrame.Height;
            int bpp = colorFrame.BitsPerPixel / 8;

            if (bpp != 3)
            {
                return colorFrame;
            }

            int byteCount = width * height * bpp;
            if (_cpuDataBuffer == null || _cpuDataBuffer.Length != byteCount)
            {
                _cpuDataBuffer = new byte[byteCount];
            }

            colorFrame.CopyTo(_cpuDataBuffer);

            Vector3 hsv;

            for (int i = 0; i < byteCount; i += 3)
            {
                byte r_byte = _cpuDataBuffer[i];
                byte g_byte = _cpuDataBuffer[i + 1];
                byte b_byte = _cpuDataBuffer[i + 2];

                RsColorSpaceHelper.RgbToHsv(r_byte, g_byte, b_byte, out hsv);

                bool isSkin = (hsv.x >= _minHue && hsv.x <= _maxHue) &&
                              (hsv.y >= _minSaturation && hsv.y <= _maxSaturation) &&
                              (hsv.z >= _minValue && hsv.z <= _maxValue);

                if (!isSkin)
                {
                    _cpuDataBuffer[i] = 0;     // R
                    _cpuDataBuffer[i + 1] = 0; // G
                    _cpuDataBuffer[i + 2] = 0; // B
                }
            }

            var newFrame = frameSource.AllocateVideoFrame<VideoFrame>(colorFrame.Profile, colorFrame, bpp * 8, width, height, colorFrame.Stride, Extension.VideoFrame);
            newFrame.CopyFrom(_cpuDataBuffer);

            return newFrame;
        }
    }

    public override Frame Process(Frame frame, FrameSource frameSource)
    {
        if (frame.IsComposite)
        {
            using (var fs = FrameSet.FromFrame(frame))
            {
                VideoFrame colorFrame = fs.ColorFrame as VideoFrame;
                if (colorFrame == null)
                {
                    return frame;
                }

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
                    using (res)
                        return res.AsFrame();
                }
            }
        }

        if (frame is VideoFrame)
        {
            return ApplyFilter(frame as VideoFrame, frameSource);
        }

        return frame;
    }
}