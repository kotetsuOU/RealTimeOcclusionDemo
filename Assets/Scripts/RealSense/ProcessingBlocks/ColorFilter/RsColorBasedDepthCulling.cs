using Intel.RealSense;
using System;
using UnityEngine;

[ProcessingBlockData(typeof(RsColorBasedDepthCulling))]
[HelpURL("https://github.com/IntelRealSense/librealsense/blob/master/doc/post-processing-filters.md")]
public class RsColorBasedDepthCulling : RsProcessingBlock
{
    public enum ConversionMode
    {
        HSV,
        YCbCr
    }

    [Header("Control")]
    public bool SaveDebugFrames = false;
    public ConversionMode _mode = ConversionMode.HSV;

    [Header("Save Settings")]
    public string DebugSavePath = "Assets/RealSenseDebug";

    // --- HSV Settings ---
    [Header("HSV Thresholds (0.0 - 1.0)")]
    [Range(0f, 1f)] public float _minHue = 0.0f;
    [Range(0f, 1f)] public float _maxHue = 0.1f;
    [Range(0f, 1f)] public float _minSaturation = 0.1f;
    [Range(0f, 1f)] public float _maxSaturation = 1.0f;
    [Range(0f, 1f)] public float _minValue = 0.1f;
    [Range(0f, 1f)] public float _maxValue = 1.0f;

    // --- YCbCr Settings ---
    [Header("YCbCr Thresholds (0 - 255)")]
    [Range(0, 255)] public int _minY = 0;
    [Range(0, 255)] public int _maxY = 255;
    [Range(0, 255)] public int _minCb = 77;
    [Range(0, 255)] public int _maxCb = 127;
    [Range(0, 255)] public int _minCr = 133;
    [Range(0, 255)] public int _maxCr = 173;

    private RsDepthToColorCalibration _calibration;
    private string _savePath;

    private Vector3 hsvCache;
    private Vector3Int ycbcrCache;

    public void SetCalibration(RsDepthToColorCalibration calib)
    {
        _calibration = calib;
        _savePath = RsCullingDebugExporter.ResolveAndCreatePath(DebugSavePath);
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
                        RsCullingDebugExporter.SaveDebugImages(colorFrame, _mode, _savePath, CheckIsTarget);
                        SaveDebugFrames = false;
                    }
                    CullDepthByColor(colorFrame, depthFrame);
                }
            }
        }
        return frame;
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

                if (!CheckIsTarget(r, g, b))
                {
                    depthPtr[depthIdx] = 0;
                }
            }
            else
            {
                depthPtr[depthIdx] = 0;
            }
        }
    }

    private bool CheckIsTarget(byte r, byte g, byte b)
    {
        if (_mode == ConversionMode.HSV)
        {
            RsHsvConverter.RgbToHsv(r, g, b, out hsvCache);
            return (hsvCache.x >= _minHue && hsvCache.x <= _maxHue) &&
                   (hsvCache.y >= _minSaturation && hsvCache.y <= _maxSaturation) &&
                   (hsvCache.z >= _minValue && hsvCache.z <= _maxValue);
        }
        else
        {
            RsYCbCrConverter.RgbToYCbCr(r, g, b, out ycbcrCache);
            return (ycbcrCache.x >= _minY && ycbcrCache.x <= _maxY) &&
                   (ycbcrCache.y >= _minCb && ycbcrCache.y <= _maxCb) &&
                   (ycbcrCache.z >= _minCr && ycbcrCache.z <= _maxCr);
        }
    }
}