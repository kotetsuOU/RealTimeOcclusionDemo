using Intel.RealSense;
using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ProcessingBlockData(typeof(RsIntegratedPointCloud))]
/// <summary>
/// 色ベースのフィルタリングを利用して、特定の対象（手など）のみの点群を抽出し、
/// 指定された座標変換行列を適用して統合用の点群バッファを提供する処理ブロック。
/// </summary>
public class RsIntegratedPointCloud : RsProcessingBlock
{
    public enum ConversionMode { HSV = 0, YCbCr = 1 }
    public enum ColorVisualizationMode { Palette16, Grayscale }
    public enum CoordinateConversion { None = 0, FlipY = 1, FlipYAndZ = 2, FlipX = 3, Raw = 4 }

    [Header("Compute Shader")]
    [Tooltip("GPUで点群の計算や座標変換を行うためのComputeShader")]
    public ComputeShader _integratedShader;
    private const string COMPUTE_SHADER_RESOURCES_PATH = "ComputeShaders/RsIntegratedPointCloud";

    [Header("Control")]
    [Tooltip("処理状態のデバッグフレーム画像を保存するかどうか")]
    public bool SaveDebugFrames = false;
    [Tooltip("色による抽出判定に使用するカラーモデル")]
    public ConversionMode _mode = ConversionMode.HSV;

    [Header("Debug Visualization")]
    [Tooltip("デバッグ出力時の可視化モード（パレット表示など）")]
    public ColorVisualizationMode _debugMode = ColorVisualizationMode.Palette16;
    [Tooltip("デバッグ画像の保存先フォルダ")]
    public string DebugSavePath = "Assets/RealSenseDebug";

    [Header("Debug Matrix")]
    [Tooltip("特定の変換行列を点群に適用するかどうか")]
    public bool _applyTransform = false;
    [Tooltip("適用する4x4トランスフォーム行列")]
    public Matrix4x4 _transformMatrix = Matrix4x4.identity;
    [Tooltip("Unityの世界に合わせるための座標系の反転モード")]
    public CoordinateConversion _coordinateConversion = CoordinateConversion.FlipY;

    [Header("Thresholds")]
    [Tooltip("取得する点群の最小距離(メートル)")]
    [Range(0f, 16f)] public float _minDistance = 0.1f;
    [Tooltip("取得する点群の最大距離(メートル)")]
    [Range(0f, 16f)] public float _maxDistance = 4f;

    [Header("HSV Thresholds")]
    [Tooltip("HSV色空間におけるHue(色相)の最小値 (0-1)")]
    [Range(0f, 1f)] public float _minHue = 0.0f;
    [Tooltip("HSV色空間におけるHue(色相)の最大値 (0-1)")]
    [Range(0f, 1f)] public float _maxHue = 0.1f;
    [Tooltip("HSV色空間におけるSaturation(彩度)の最小値 (0-1)")]
    [Range(0f, 1f)] public float _minSaturation = 0.1f;
    [Tooltip("HSV色空間におけるSaturation(彩度)の最大値 (0-1)")]
    [Range(0f, 1f)] public float _maxSaturation = 1.0f;
    [Tooltip("HSV色空間におけるValue(明度)の最小値 (0-1)")]
    [Range(0f, 1f)] public float _minValue = 0.1f;
    [Tooltip("HSV色空間におけるValue(明度)の最大値 (0-1)")]
    [Range(0f, 1f)] public float _maxValue = 1.0f;

    [Header("YCbCr Thresholds")]
    [Tooltip("YCbCr色空間におけるY(輝度)の最小値 (0-255)")]
    [Range(0, 255)] public int _minY = 0;
    [Tooltip("YCbCr色空間におけるY(輝度)の最大値 (0-255)")]
    [Range(0, 255)] public int _maxY = 255;
    [Tooltip("YCbCr色空間におけるCb(青色差)の最小値 (0-255)")]
    [Range(0, 255)] public int _minCb = 77;
    [Tooltip("YCbCr色空間におけるCb(青色差)の最大値 (0-255)")]
    [Range(0, 255)] public int _maxCb = 127;
    [Tooltip("YCbCr色空間におけるCr(赤色差)の最小値 (0-255)")]
    [Range(0, 255)] public int _minCr = 133;
    [Tooltip("YCbCr色空間におけるCr(赤色差)の最大値 (0-255)")]
    [Range(0, 255)] public int _maxCr = 173;

    [NonSerialized] private RsDepthToColorCalibration _calibration;
    [NonSerialized] private RsIntegratedPointCloudProcessor _gpuProcessor;

    /// <summary>
    /// GPUで抽出・変換された点群が格納されているComputeBuffer
    /// </summary>
    public ComputeBuffer PointCloudBuffer => _gpuProcessor?.PointCloudBuffer;

    /// <summary>
    /// 前回のフレームで抽出された有効な点の数
    /// </summary>
    public int LastPointCount => _gpuProcessor?.LastPointCount ?? 0;

    /// <summary>
    /// 点群データが更新された際に発行されるイベント
    /// </summary>
    public event Action OnPointCloudUpdated;

    private void OnEnable()
    {
        if (_integratedShader == null)
        {
            _integratedShader = Resources.Load<ComputeShader>(COMPUTE_SHADER_RESOURCES_PATH);
        }

        if (!Application.isPlaying) return;

        // メインスレッドのディスパッチャーを初期化
        if (RsUnityMainThreadDispatcher.Instance == null)
        {
            var _ = RsUnityMainThreadDispatcher.Instance;
        }
    }

    /// <summary>
    /// デバイス座標からワールド空間等への変換行列を更新する
    /// </summary>
    public void UpdateTransformMatrix(Matrix4x4 matrix)
    {
        _transformMatrix = matrix;
        _applyTransform = true;
        if (_gpuProcessor != null)
        {
            _gpuProcessor.UpdateTransformMatrix(matrix);
        }

        // Debug log to verify matrix update
        if (SaveDebugFrames)
        {
            Debug.Log($"[RsIntegratedPointCloud] Updated Transform Matrix:\n{matrix}");
        }
    }

    /// <summary>
    /// カメラ初期化時に計算された内部・外部パラメータ（キャリブレーション情報）を設定する
    /// </summary>
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

    /// <summary>
    /// パイプラインから毎フレーム呼び出される処理の本体。
    /// カラー画像と深度画像を取り出し、GPUによる抽出処理へ渡します。
    /// </summary>
    public override Frame Process(Frame frame, FrameSource frameSource)
    {
        // キャリブレーションが未完了、もしくはGPUプロセッサが初期化されていない場合は何もしない
        if (_calibration == null || _gpuProcessor == null) return frame;

        // フレームが複数のデータ（ColorやDepthなど）を含むコンポジットフレームであるか確認
        if (frame.IsComposite)
        {
            // usingブロックを使用して、不要になったRealSenseの非マネージドリソース(C++側)を確実に解放する
            using (var fs = FrameSet.FromFrame(frame))
            using (var colorFrame = fs.ColorFrame)
            using (var depthFrame = fs.DepthFrame)
            {
                if (colorFrame != null && depthFrame != null)
                {
                    // 色フィルタリングの閾値設定を調整するためのデバッグ用画像を保存する
                    if (SaveDebugFrames)
                    {
                        SaveDebugImages(colorFrame);
                        SaveDebugFrames = false;
                    }

                    // GPUプロセッサにカラーと深度を渡し、非同期コピーとComputeShaderの実行を依頼する
                    _gpuProcessor.Process(colorFrame, depthFrame, this);

                    // 非同期リードバックによって新しい点群データがGPUから到着していればイベントを発火
                    if (_gpuProcessor.HasNewPointCloud)
                    {
                        OnPointCloudUpdated?.Invoke();
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