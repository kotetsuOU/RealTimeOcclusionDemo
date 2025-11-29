using Intel.RealSense;
using System;
using System.Diagnostics;
using UnityEngine;

public class RsDepthToColorCalibration
{
    public VideoStreamProfile DepthProfile { get; private set; }
    public VideoStreamProfile ColorProfile { get; private set; }
    private Intrinsics _depthIntrinsics;
    private Intrinsics _colorIntrinsics;
    private float[] _depthToColorRotation;
    private float[] _depthToColorTranslation;

    public RsDepthToColorCalibration(PipelineProfile profile)
    {
        try
        {
            DepthProfile = profile.GetStream(Intel.RealSense.Stream.Depth).As<VideoStreamProfile>();
            ColorProfile = profile.GetStream(Intel.RealSense.Stream.Color).As<VideoStreamProfile>();

            _depthIntrinsics = DepthProfile.GetIntrinsics();
            _colorIntrinsics = ColorProfile.GetIntrinsics();

            var extrinsics = DepthProfile.GetExtrinsicsTo(ColorProfile);

            _depthToColorRotation = extrinsics.rotation;
            _depthToColorTranslation = extrinsics.translation;

            UnityEngine.Debug.Log("[RsDepthToColorCalibration] Calibration initialized successfully");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[RsDepthToColorCalibration] Initialization failed: {e.Message}");
        }
    }

    public bool MapDepthToColor(int depthX, int depthY, ushort depthValue, out int colorX, out int colorY)
    {
        colorX = 0;
        colorY = 0;

        if (depthValue == 0) return false;

        try
        {
            float[] depthPoint3d = DeprojectPixelToPoint(_depthIntrinsics, depthX, depthY, depthValue);

            float[] colorPoint3d = Transform3DPoint(depthPoint3d);

            ProjectPointToPixel(_colorIntrinsics, colorPoint3d, out colorX, out colorY);

            return colorX >= 0 && colorX < ColorProfile.Width &&
                   colorY >= 0 && colorY < ColorProfile.Height;
        }
        catch
        {
            return false;
        }
    }

    private float[] DeprojectPixelToPoint(Intrinsics intrinsics, int depthX, int depthY, ushort depthValue)
    {
        float depth = depthValue / 1000f;  // ミリメートル → メートル

        float x = (depthX - intrinsics.ppx) / intrinsics.fx;
        float y = (depthY - intrinsics.ppy) / intrinsics.fy;

        float[] point3d = new float[3];
        point3d[0] = x * depth;
        point3d[1] = y * depth;
        point3d[2] = depth;

        return point3d;
    }

    private void ProjectPointToPixel(Intrinsics intrinsics, float[] point3d, out int pixelX, out int pixelY)
    {
        if (point3d[2] <= 0)
        {
            pixelX = 0;
            pixelY = 0;
            return;
        }

        float x = point3d[0] / point3d[2];
        float y = point3d[1] / point3d[2];

        pixelX = (int)(x * intrinsics.fx + intrinsics.ppx);
        pixelY = (int)(y * intrinsics.fy + intrinsics.ppy);
    }

    private float[] Transform3DPoint(float[] point)
    {
        float[] rotated = new float[3];
        for (int i = 0; i < 3; i++)
        {
            rotated[i] = _depthToColorRotation[i * 3] * point[0] +
                         _depthToColorRotation[i * 3 + 1] * point[1] +
                         _depthToColorRotation[i * 3 + 2] * point[2];
        }

        rotated[0] += _depthToColorTranslation[0];
        rotated[1] += _depthToColorTranslation[1];
        rotated[2] += _depthToColorTranslation[2];

        return rotated;
    }
}