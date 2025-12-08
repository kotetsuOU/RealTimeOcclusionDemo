using Intel.RealSense;
using System;
using UnityEngine;

[ProcessingBlockData(typeof(RsIntegratedPointCloud))]
public class RsIntegratedPointCloud : RsProcessingBlock
{
    public enum ConversionMode { HSV = 0, YCbCr = 1 }
    public enum ColorVisualizationMode { Palette16, Grayscale }

    [Header("Compute Shader")]
    public ComputeShader _integratedShader;
    private const string COMPUTE_SHADER_RESOURCES_PATH = "ComputeShaders/RsIntegratedPointCloud";

    [Header("Control")]
    public bool SaveDebugFrames = false;
    public ConversionMode _mode = ConversionMode.HSV;

    [Header("Debug Visualization")]
    public ColorVisualizationMode _debugMode = ColorVisualizationMode.Palette16;
    public string DebugSavePath = "Assets/RealSenseDebug";

    [Header("Thresholds")]
    [Range(0f, 16f)] public float _minDistance = 0.1f;
    [Range(0f, 16f)] public float _maxDistance = 4f;

    [Header("HSV Thresholds")]
    [Range(0f, 1f)] public float _minHue = 0.0f;
    [Range(0f, 1f)] public float _maxHue = 0.1f;
    [Range(0f, 1f)] public float _minSaturation = 0.1f;
    [Range(0f, 1f)] public float _maxSaturation = 1.0f;
    [Range(0f, 1f)] public float _minValue = 0.1f;
    [Range(0f, 1f)] public float _maxValue = 1.0f;

    [Header("YCbCr Thresholds")]
    [Range(0, 255)] public int _minY = 0;
    [Range(0, 255)] public int _maxY = 255;
    [Range(0, 255)] public int _minCb = 77;
    [Range(0, 255)] public int _maxCb = 127;
    [Range(0, 255)] public int _minCr = 133;
    [Range(0, 255)] public int _maxCr = 173;

    [NonSerialized] private RsDepthToColorCalibration _calibration;
    [NonSerialized] private RsIntegratedPointCloudProcessor _gpuProcessor;
    [NonSerialized] private Vector3[] _lastPointCloud;

    public int LastPointCount => _gpuProcessor?.LastPointCount ?? 0;
    public event Action<Vector3[]> OnPointCloudUpdated;

    private void OnEnable()
    {
        if (_integratedShader == null)
        {
            _integratedShader = Resources.Load<ComputeShader>(COMPUTE_SHADER_RESOURCES_PATH);
        }
    }

    public void SetCalibration(RsDepthToColorCalibration calib)
    {
        if (calib == null) return;

        _calibration = calib;

        if (_integratedShader == null)
        {
            _integratedShader = Resources.Load<ComputeShader>(COMPUTE_SHADER_RESOURCES_PATH);
        }

        if (_integratedShader != null)
        {
            if (_gpuProcessor == null)
            {
                ComputeShader shaderInstance = Instantiate(_integratedShader);
                shaderInstance.name = $"{_integratedShader.name}_{name}_{GetInstanceID()}";
                _gpuProcessor = new RsIntegratedPointCloudProcessor(shaderInstance);
            }

            _gpuProcessor.Initialize(_calibration);
            Debug.Log($"[RsIntegratedPointCloud] Initialized for {name}");
        }
        else
        {
            Debug.LogError($"[RsIntegratedPointCloud] Compute Shader missing: {COMPUTE_SHADER_RESOURCES_PATH}");
        }
    }

    public override Frame Process(Frame frame, FrameSource frameSource)
    {
        if (_calibration == null || _gpuProcessor == null) return frame;

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

                    _lastPointCloud = _gpuProcessor.Process(colorFrame, depthFrame, this);

                    if (_lastPointCloud != null && _lastPointCloud.Length > 0)
                    {
                        OnPointCloudUpdated?.Invoke(_lastPointCloud);
                    }
                }
            }
        }
        return frame;
    }

    private void SaveDebugImages(VideoFrame colorFrame)
    {
        string path = RsCullingDebugExporter.ResolveAndCreatePath(DebugSavePath);
        RsCullingDebugExporter.SaveDebugImages(
            colorFrame,
            (RsColorBasedDepthCulling.ConversionMode)(int)_mode,
            path,
            (r, g, b) =>
            {
                if (_mode == ConversionMode.HSV)
                {
                    RsHsvConverter.RgbToHsv(r, g, b, out Vector3 hsv);
                    return (hsv.x >= _minHue && hsv.x <= _maxHue) &&
                           (hsv.y >= _minSaturation && hsv.y <= _maxSaturation) &&
                           (hsv.z >= _minValue && hsv.z <= _maxValue);
                }
                else
                {
                    RsYCbCrConverter.RgbToYCbCr(r, g, b, out Vector3Int ycbcr);
                    return (ycbcr.x >= _minY && ycbcr.x <= _maxY) &&
                           (ycbcr.y >= _minCb && ycbcr.y <= _maxCb) &&
                           (ycbcr.z >= _minCr && ycbcr.z <= _maxCr);
                }
            },
            (RsColorBasedDepthCulling.ColorVisualizationMode)(int)_debugMode
        );
    }

    public override void Reset()
    {
        base.Reset();
        DisposeProcessor();
    }

    private void OnDestroy() => DisposeProcessor();
    private void OnDisable() => DisposeProcessor();

    private void DisposeProcessor()
    {
        if (_gpuProcessor != null)
        {
            _gpuProcessor.Dispose();
            _gpuProcessor = null;
        }
    }
}