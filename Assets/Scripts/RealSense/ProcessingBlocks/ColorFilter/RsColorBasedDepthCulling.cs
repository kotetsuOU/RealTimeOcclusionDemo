using Intel.RealSense;
using System;
using UnityEngine;

[ProcessingBlockData(typeof(RsColorBasedDepthCulling))]
[HelpURL("https://github.com/IntelRealSense/librealsense/blob/master/doc/post-processing-filters.md")]
/// <summary>
/// 特定の色空間（HSVまたはYCbCr）閾値に基づいて、指定範囲外の色を持つ深度ピクセルを
/// 除外（カリング）する画像処理ブロック。色に対応する深度を0に設定する。
/// </summary>
public class RsColorBasedDepthCulling : RsProcessingBlock
{
    public enum ConversionMode
    {
        HSV,
        YCbCr
    }

    public enum ColorVisualizationMode
    {
        Palette16,
        Grayscale
    }

    [Header("Compute Shader")]
    public ComputeShader _cullingShader;

    [Header("Control")]
    public bool SaveDebugFrames = false;
    public ConversionMode _mode = ConversionMode.HSV;

    [Header("Debug Visualization")]
    public ColorVisualizationMode _debugMode = ColorVisualizationMode.Palette16;

    [Header("Save Settings")]
    public string DebugSavePath = "Assets/RealSenseDebug";

    [Header("HSV Thresholds (0.0 - 1.0)")]
    [Range(0f, 1f)] public float _minHue = 0.0f;
    [Range(0f, 1f)] public float _maxHue = 0.1f;
    [Range(0f, 1f)] public float _minSaturation = 0.1f;
    [Range(0f, 1f)] public float _maxSaturation = 1.0f;
    [Range(0f, 1f)] public float _minValue = 0.1f;
    [Range(0f, 1f)] public float _maxValue = 1.0f;

    [Header("YCbCr Thresholds (0 - 255)")]
    [Range(0, 255)] public int _minY = 0;
    [Range(0, 255)] public int _maxY = 255;
    [Range(0, 255)] public int _minCb = 77;
    [Range(0, 255)] public int _maxCb = 127;
    [Range(0, 255)] public int _minCr = 133;
    [Range(0, 255)] public int _maxCr = 173;

    private RsDepthToColorCalibration _calibration;
    private string _savePath;

    private RsGpuCullingProcessor _gpuProcessor;

    public void SetCalibration(RsDepthToColorCalibration calib)
    {
        _calibration = calib;
        _savePath = RsCullingDebugExporter.ResolveAndCreatePath(DebugSavePath);

        if (_cullingShader != null)
        {
            if (_gpuProcessor == null)
            {
                _gpuProcessor = new RsGpuCullingProcessor(_cullingShader);
            }
            _gpuProcessor.Initialize(_calibration);
        }
        else
        {
            UnityEngine.Debug.LogWarning("[RsColorBasedDepthCulling] Compute Shader is not assigned. Fallback to CPU mode is not implemented for performance reasons.");
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
                        RsCullingDebugExporter.SaveDebugImages(
                            colorFrame,
                            _mode,
                            _savePath,
                            (r, g, b) =>
                            {
                                if (_mode == ConversionMode.HSV)
                                {
                                    Vector3 hsv;
                                    RsHsvConverter.RgbToHsv(r, g, b, out hsv);
                                    return (hsv.x >= _minHue && hsv.x <= _maxHue) &&
                                           (hsv.y >= _minSaturation && hsv.y <= _maxSaturation) &&
                                           (hsv.z >= _minValue && hsv.z <= _maxValue);
                                }
                                else
                                {
                                    Vector3Int ycbcr;
                                    RsYCbCrConverter.RgbToYCbCr(r, g, b, out ycbcr);
                                    return (ycbcr.x >= _minY && ycbcr.x <= _maxY) &&
                                           (ycbcr.y >= _minCb && ycbcr.y <= _maxCb) &&
                                           (ycbcr.z >= _minCr && ycbcr.z <= _maxCr);
                                }
                            },
                            _debugMode
                        );
                        SaveDebugFrames = false;
                    }

                    if (_gpuProcessor != null)
                    {
                        _gpuProcessor.Process(colorFrame, depthFrame, this);
                    }
                }
            }
        }
        return frame;
    }

    public override void Reset()
    {
        base.Reset();
        if (_gpuProcessor != null)
        {
            _gpuProcessor.Dispose();
            _gpuProcessor = null;
        }
    }

    private void OnDestroy()
    {
        if (_gpuProcessor != null)
        {
            _gpuProcessor.Dispose();
            _gpuProcessor = null;
        }
    }
}