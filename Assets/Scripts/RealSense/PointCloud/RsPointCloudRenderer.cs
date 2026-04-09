using Intel.RealSense;
using System.Diagnostics;
using UnityEngine;

[RequireComponent(typeof(MeshRenderer))]
public class RsPointCloudRenderer : MonoBehaviour
{
    #region Inspector Fields

    [Header("Dependencies & Settings")]
    [Tooltip("RealSenseデバイスの制御・設定を行うコンポーネント")]
    public RsDeviceController rsDeviceController;
    [Tooltip("RealSenseからストリーミングされるフレームのパイプライン")]
    public RsProcessingPipe processingPipe;
    [Tooltip("点群のフィルタリングやダウンサンプリング、PCAを行うComputeShader")]
    [SerializeField] private ComputeShader pointCloudFilterShader;
    [Tooltip("点群の座標変換（デバイス空間からワールド空間など）を行うComputeShader")]
    [SerializeField] private ComputeShader pointCloudTransformerShader;

    [Header("PointCloud Settings")]
    [Tooltip("点群取得における最大距離の閾値（これより遠い点は破棄される可能性がある）")]
    [SerializeField] public float maxPlaneDistance = 0.1f;
    [Tooltip("点群の描画時の基本色")]
    public Color pointCloudColor = new Color(241f / 255f, 187f / 255f, 147f / 255f, 1f);
    [SerializeField, HideInInspector] private string exportFileName = "currentGlobalVertices.txt";

    [Header("Debug Synthetic")]
    [Tooltip("実機カメラを使わず、合成データ（ダミーの点群）でテストするかどうか")]
    public bool useSyntheticData = false;
    [Tooltip("合成データとして生成する形状の指定")]
    public RsPointCloudSyntheticData.SyntheticShape syntheticShape = RsPointCloudSyntheticData.SyntheticShape.Cylinder;
    [Tooltip("合成データとして生成する点の数")]
    [Range(100, 100000)] public int syntheticPointCount = 10000;
    [Tooltip("合成データの形状のサイズ倍率")]
    public float syntheticScale = 1.0f;

    [Header("Debug Output")]
    [Tooltip("フィルタリングされた点群の一部をコンソールにログ出力するかどうか")]
    public bool debugFilteredPoints = false;
    [Tooltip("ログに出力する点の数")]
    [Range(1, 20)] public int debugPointCount = 5;

    [Header("Performance Logging Settings")]
    [Tooltip("パフォーマンス計測ログの出力ファイル名のプレフィックス")]
    public string logFilePrefix = "PointCloudPerfLog";
    [Tooltip("計測を開始するフレーム数（起動時から数えて）")]
    public long startFrame = 200;
    [Tooltip("計測を終了するフレーム数")]
    public long endFrame = 1400;
    [Tooltip("既存のログファイルに追記するかどうか")]
    public bool appendLog = false;

    [Header("Debug")]
    [Tooltip("現在の変換行列(ローカル>ワールド)を毎フレームログに出力するか")]
    public bool showDebugMatrix = false;

    #endregion

    #region Private Fields

    private RsPointCloudInitializer _initializer; // 点群処理の初期化と管理を担うヘルパークラス
    private RsPerformanceLogger _logger;          // 処理時間などを記録してファイルに書き出すロガー
    private RsPointCloudVisualization _visualization; // `Graphics.DrawProcedural`等で点群を描画するヘルパークラス
    private readonly Stopwatch _stopwatch = new Stopwatch(); // 処理時間計測用
    private int _frameCounter = 0; // スクリプトがアクティブになってからの実行フレーム数

    #endregion

    #region Public Properties

    // フィルタリング処理（例えば遠すぎる点の除外）などを有効にするかどうか
    public bool IsGlobalRangeFilterEnabled { get; set; } = true;

    // PCA等によって推定された主要軸の基準点
    public Vector3 EstimatedPoint
    {
        get
        {
            if (RsGlobalPointCloudManager.Instance != null && RsGlobalPointCloudManager.Instance.IsIntegratedPCAMode)
            {
                return RsGlobalPointCloudManager.Instance.GetLineEstimation().point;
            }
            return _initializer?.FrameProcessor?.EstimatedPoint ?? Vector3.zero;
        }
    }

    // PCA等によって推定された主要軸の方向ベクトル
    public Vector3 EstimatedDir
    {
        get
        {
            if (RsGlobalPointCloudManager.Instance != null && RsGlobalPointCloudManager.Instance.IsIntegratedPCAMode)
            {
                return RsGlobalPointCloudManager.Instance.GetLineEstimation().dir;
            }
            return _initializer?.FrameProcessor?.EstimatedDir ?? Vector3.forward;
        }
    }

    // 現在パフォーマンスログを計測中かどうか
    public bool IsPerformanceLogging => Application.isPlaying && _logger != null && _logger.IsLogging;

    #endregion

    #region Cached Sampling (Non-blocking)

    private RsSamplingResult _cachedSamplingResult;
    private int _cachedSamplingFrame = -1;

    /// <summary>
    /// GPU側から非同期（またはブロックなしで得られた）最新のサンプリング（ダウンサンプルされた点群など）結果の取得を試みる
    /// </summary>
    public bool TryGetLatestSamplingResult(out RsSamplingResult result)
    {
        var current = _initializer?.Compute?.LastSamplingResult ?? new RsSamplingResult();

        // 新しい結果が取得できた場合
        if (current.IsValid)
        {
            _cachedSamplingResult = current;
            _cachedSamplingFrame = _frameCounter;
            result = current;
            return true;
        }

        // 前回取得したキャッシュが有効であればそれを返す
        if (_cachedSamplingResult.IsValid)
        {
            result = _cachedSamplingResult;
            return true;
        }

        result = new RsSamplingResult();
        return false;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 最新のサンプリング結果を取得（非同期で完了したものが含まれる）
    /// </summary>
    public RsSamplingResult GetLastSamplingResult()
    {
        return _initializer?.Compute?.LastSamplingResult ?? new RsSamplingResult();
    }

    public void StartPerformanceLog() => _logger?.StartLogging(logFilePrefix, appendLog, startFrame, endFrame);
    public void StopPerformanceLog() => _logger?.StopLogging();

    /// <summary>
    /// ComputeShaderによってフィルタリング済みの頂点配列をCPU側に読み戻して取得する（同期実行のため重い処理）
    /// </summary>
    public Vector3[] GetFilteredVertices()
    {
        var compute = _initializer?.Compute;
        if (compute == null)
        {
            UnityEngine.Debug.LogWarning("[RsPointCloudRenderer] Compute instance is null.");
            return new Vector3[0];
        }

        int count = compute.GetLastFilteredCount();
        if (count <= 0)
        {
            UnityEngine.Debug.LogWarning($"[RsPointCloudRenderer] Vertex count is {count}.");
            return new Vector3[0];
        }

        Vector3[] result = new Vector3[count];
        // CPU配列に書き出す
        compute.GetFilteredVerticesData(result, count);
        return result;
    }

    /// <summary> フィルタリング後の座標位置が格納されたComputeBufferを取得する </summary>
    public ComputeBuffer GetFilteredVerticesBuffer() => _initializer?.Compute?.GetFilteredVerticesBuffer();

    /// <summary> フィルタリング後の点の数を取得する </summary>
    public int GetLastFilteredCount() => _initializer?.Compute?.GetLastFilteredCount() ?? 0;

    /// <summary> 非同期カウントのリードバックが完了待ちかどうか </summary>
    public bool IsFilteredCountReadbackPending => _initializer?.Compute?.IsFilteredCountReadbackPending ?? false;

    /// <summary> オクルージョン処理など他のシェーダーが参照するための元バッファを取得する </summary>
    public ComputeBuffer GetPCDSourceBuffer()
    {
        return GetFilteredVerticesBuffer();
    }

    /// <summary> オクルージョン処理などで参照する点群の総数を取得する </summary>
    public int GetPCDSourceCount()
    {
        return GetLastFilteredCount();
    }

    public ComputeBuffer GetRawBuffer() => GetFilteredVerticesBuffer();
    public int GetLastVertexCount() => GetLastFilteredCount();
    public RsComputeStats GetComputeStats() => _initializer?.Compute?.Stats;

    #endregion

    #region Unity Lifecycle

    private string _cachedSourceName; // gameObject.name の毎フレーム取得によるGC防止

    void Start()
    {
        _cachedSourceName = gameObject.name;
        _logger = new RsPerformanceLogger();
        _visualization = new RsPointCloudVisualization(GetComponent<MeshRenderer>());
        _initializer = new RsPointCloudInitializer(processingPipe, pointCloudFilterShader, pointCloudTransformerShader);

        if (useSyntheticData)
        {
            // 合成データを使用する場合の初期化
            _initializer.InitializeSynthetic(syntheticShape, syntheticPointCount, syntheticScale, maxPlaneDistance, transform.localToWorldMatrix, _logger, _stopwatch);
        }
        else
        {
            // RealSenseからデータを受け取るためストリーミング開始イベントを監視
            processingPipe.OnStart += OnStartStreaming;
        }
    }

    void LateUpdate()
    {
        if (!_initializer.IsInitialized) return;

        // 計算処理側へ現在のゲームオブジェクト名を伝える（主に出力ログ等の識別用）
        if (_initializer.FrameProcessor != null)
        {
            _initializer.FrameProcessor.SourceName = _cachedSourceName;
        }

        // 計測中で有ればストップウォッチをリセット
        if (_logger.IsLogging) _stopwatch.Restart();

        // オブジェクトのTransform行列のデバッグ出力を設定している場合
        if (showDebugMatrix)
        {
            UnityEngine.Debug.Log($"[RsPointCloudRenderer] Current Transform Matrix:\n{transform.localToWorldMatrix}");
        }

        _frameCounter++;
        // メインとなる毎フレームの点群処理（フィルタ、サンプリング、PCA等）の実行
        ComputeBuffer argsBuffer = ProcessCurrentFrame();

        if (_stopwatch.IsRunning) _stopwatch.Stop();

        // 処理が成功し、描画用の引数(Args)バッファが返って来た場合、実際の描画プロセスへ進む
        if (argsBuffer != null)
        {
            if (debugFilteredPoints)
            {
                RsPointCloudVisualization.DebugLogFilteredPoints(_initializer.Compute, debugPointCount);
            }
            // `Graphics.DrawProcedural` または MeshRenderer を利用して点群を描画する
            _visualization.Draw(_initializer.Compute.GetFilteredVerticesBuffer(), argsBuffer, pointCloudColor, gameObject.layer);
        }
    }

    void OnDestroy()
    {
        Dispose();
    }

    #endregion

    #region Event Handlers

    // パイプライン経由でカメラからプロファイル情報が渡されたときに呼び出される
    private void OnStartStreaming(PipelineProfile profile)
    {
        if (useSyntheticData) return;

        _initializer.InitializeOnStreaming(profile, rsDeviceController, maxPlaneDistance, _logger, _stopwatch);

        // RsGlobalPointCloudManagerなどから統合された点群を扱う設定がされた場合のイベント監視
        if (_initializer.UseIntegratedPointCloud)
        {
            _initializer.IntegratedPointCloud.OnPointCloudUpdated += OnIntegratedPointCloudUpdated;
            _initializer.UpdateIntegratedTransform(transform.localToWorldMatrix);
        }
    }

    private void OnIntegratedPointCloudUpdated() { } // デリゲートの受け口。実装はInitializer等に委譲されている想定

    #endregion

    #region Frame Processing

    /// <summary>
    /// 現在のフレームでComputeShader側に与える各データを取得し、計算を実行させる。
    /// 返り値としてプロシージャル描画に利用するArgsBufferを返す。
    /// </summary>
    private ComputeBuffer ProcessCurrentFrame()
    {
        var processor = _initializer?.FrameProcessor;
        if (processor == null) return null;

        // 他デバイスと結合されたPCA軸が存在する場合は取得する
        (Vector3 linePoint, Vector3 lineDir) = GetLineEstimation();

        if (useSyntheticData)
        {
            return processor.ProcessSyntheticFrame(
                _initializer.RawVerticesBuffer,
                _initializer.RawVertices.Length,
                linePoint, lineDir, IsGlobalRangeFilterEnabled, maxPlaneDistance);
        }

        // ローカル点群ではなくグローバル空間で統合済みの点群を扱うモード
        if (_initializer.UseIntegratedPointCloud)
        {
            return ProcessIntegratedFrame(processor, linePoint, lineDir);
        }

        // それ以外の場合、単純にRealSenseの新規フレームを取得して処理する
        return ProcessRealSenseFrame(processor, linePoint, lineDir);
    }

    // PCAのライン推定結果を自身またはグローバルマネージャから取得する
    private (Vector3 point, Vector3 dir) GetLineEstimation()
    {
        return (EstimatedPoint, EstimatedDir);
    }

    // 複数カメラから統合されたPointCloudバッファを使用する場合のフレーム実行
    private ComputeBuffer ProcessIntegratedFrame(RsPointCloudFrameProcessor processor, Vector3 linePoint, Vector3 lineDir)
    {
        var integrated = _initializer.IntegratedPointCloud;
        if (integrated?.PointCloudBuffer == null) return null;

        int pointCount = integrated.LastPointCount;
        if (pointCount == 0) return null;

        return processor.ProcessIntegratedFrame(
            integrated.PointCloudBuffer, pointCount,
            linePoint, lineDir, IsGlobalRangeFilterEnabled, _frameCounter, maxPlaneDistance);
    }

    // 単体のRealSenseカメラからの新規フレームを取り出して実行する
    private ComputeBuffer ProcessRealSenseFrame(RsPointCloudFrameProcessor processor, Vector3 linePoint, Vector3 lineDir)
    {
        var dataProvider = _initializer.DataProvider;
        // カメラから新しい点群データ（フレーム）が届いているかどうか確認
        if (dataProvider == null || !dataProvider.PollForFrame(out var points)) return null;

        // usingブロックにより取得したデータフレーム（C++側の解放）を適切にDisposeする
        using (points)
        {
            return processor.ProcessRealSenseFrame(
                points, _initializer.RawVertices, _initializer.RawVerticesBuffer,
                linePoint, lineDir, IsGlobalRangeFilterEnabled, _frameCounter, maxPlaneDistance);
        }
    }

    #endregion

    #region Cleanup

    // 関連するリソース（イベント購読、ComputeBufferなど）を破棄する
    private void Dispose()
    {
        if (_initializer?.IntegratedPointCloud != null)
        {
            _initializer.IntegratedPointCloud.OnPointCloudUpdated -= OnIntegratedPointCloudUpdated;
        }

        processingPipe.OnStart -= OnStartStreaming;
        _initializer?.Dispose();
        _logger?.Dispose();
    }

    #endregion
}