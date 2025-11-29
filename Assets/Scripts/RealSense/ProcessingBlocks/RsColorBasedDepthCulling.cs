using Intel.RealSense;
using System;
using UnityEngine;

[ProcessingBlockData(typeof(RsColorBasedDepthCulling))]
[HelpURL("https://github.com/IntelRealSense/librealsense/blob/master/doc/post-processing-filters.md")]
public class RsColorBasedDepthCulling : RsProcessingBlock
{
    [Header("HSV Thresholds (0.0 - 1.0)")]
    [Range(0f, 1f)] public float _minHue = 0.0f;
    [Range(0f, 1f)] public float _maxHue = 0.1f;
    [Range(0f, 1f)] public float _minSaturation = 0.1f;
    [Range(0f, 1f)] public float _maxSaturation = 1.0f;
    [Range(0f, 1f)] public float _minValue = 0.1f;
    [Range(0f, 1f)] public float _maxValue = 1.0f;

    private RsDepthToColorCalibration _calibration;
    private Vector3 hsvCache;

    // キャリブレーション情報を受け取るメソッド
    public void SetCalibration(RsDepthToColorCalibration calib)
    {
        _calibration = calib;
    }

    public override Frame Process(Frame frame, FrameSource frameSource)
    {
        // キャリブレーション未設定時は何もしない
        if (_calibration == null)
        {
            return frame;
        }

        if (frame.IsComposite)
        {
            using (var fs = FrameSet.FromFrame(frame))
            using (var colorFrame = fs.ColorFrame)
            using (var depthFrame = fs.DepthFrame)
            {
                // 両方のフレームが揃っている場合のみ実行
                if (colorFrame != null && depthFrame != null)
                {
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

                byte r = colorPtr[colorIdx];
                byte g = colorPtr[colorIdx + 1];
                byte b = colorPtr[colorIdx + 2];

                RsColorSpaceHelper.RgbToHsv(r, g, b, out hsvCache);

                bool isTarget = (hsvCache.x >= _minHue && hsvCache.x <= _maxHue) &&
                                (hsvCache.y >= _minSaturation && hsvCache.y <= _maxSaturation) &&
                                (hsvCache.z >= _minValue && hsvCache.z <= _maxValue);

                if (!isTarget)
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
}