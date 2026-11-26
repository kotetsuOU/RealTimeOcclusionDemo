using Intel.RealSense;
using UnityEngine;
using Core.Logging;

[RequireComponent(typeof(MeshRenderer))]
[AppLoggable("RealSense (Pipeline)")]
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
#pragma warning disable 0414
    [SerializeField, HideInInspector] private string exportFileName = "currentGlobalVertices.txt";
#pragma warning restore 0414

    #endregion

    #region Private Fields

    private RsPointCloudInitializer _initializer; // 点群処理の初期化と管理を担うヘルパークラス
    private RsPointCloudVisualization _visualization; // `Graphics.DrawProcedural`等で点群を描画するヘルパークラス
    private int _frameCounter = 0; // スクリプトがアクティブになってからの実行フレーム数

    #endregion

    #region Public Properties

    // フィルタリング処理（例えば遠すぎる点の除外）などを有効にするかどうか
    [SerializeField]
    private bool isGlobalRangeFilterEnabled = true;

    public bool IsGlobalRangeFilterEnabled
    {
        get => isGlobalRangeFilterEnabled;
        set => isGlobalRangeFilterEnabled = value;
    }

    // PCA等によって推定された主要軸の基準点と方向ベクトル
    public Vector3 EstimatedPoint =>
        (RsGlobalPointCloudManager.Instance != null && RsGlobalPointCloudManager.Instance.IsIntegratedPCAMode)
            ? RsGlobalPointCloudManager.Instance.GetLineEstimation().point
            : (_initializer?.FrameProcessor?.EstimatedPoint ?? Vector3.zero);

    public Vector3 EstimatedDir =>
        (RsGlobalPointCloudManager.Instance != null && RsGlobalPointCloudManager.Instance.IsIntegratedPCAMode)
            ? RsGlobalPointCloudManager.Instance.GetLineEstimation().dir
            : (_initializer?.FrameProcessor?.EstimatedDir ?? Vector3.forward);

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

    /// <summary>
    /// ComputeShaderによってフィルタリング済みの頂点配列をCPU側に読み戻して取得する（同期実行のため重い処理）
    /// </summary>
    public Vector3[] GetFilteredVertices()
    {
        return _initializer != null ? _initializer.ReadFilteredVertices(this) : System.Array.Empty<Vector3>();
    }

    /// <summary> フィルタリング後の座標位置が格納されたComputeBufferを取得する </summary>
    public virtual ComputeBuffer GetFilteredVerticesBuffer() => _initializer?.Compute?.GetFilteredVerticesBuffer();

    /// <summary> フィルタリング後の点の数を取得する </summary>
    public virtual int GetLastFilteredCount() => _initializer?.Compute?.GetLastFilteredCount() ?? 0;

    /// <summary> 非同期カウントのリードバックが完了待ちかどうか </summary>
    public bool IsFilteredCountReadbackPending => _initializer?.Compute?.IsFilteredCountReadbackPending ?? false;

    public virtual ComputeBuffer GetPCDSourceBuffer() => GetFilteredVerticesBuffer();
    public virtual int GetPCDSourceCount() => GetLastFilteredCount();

    /// <summary>
    /// オクルージョン専用グローバルバッファマージ用のバッファを取得する。
    /// </summary>
    public virtual ComputeBuffer GetOcclusionSourceBuffer() => GetPCDSourceBuffer();
    public virtual int GetOcclusionSourceCount() => GetPCDSourceCount();
    public RsComputeStats GetComputeStats() => _initializer?.Compute?.Stats;

    #endregion

    #region Unity Lifecycle

    private string _cachedSourceName; // gameObject.name の毎フレーム取得によるGC防止

    protected RsPointCloudInitializer Initializer => _initializer;

    private void Reset()
    {
        TryAutoAssignDependencies();
    }

    protected virtual void TryAutoAssignDependencies()
    {
        if (processingPipe == null)
        {
            processingPipe = GetComponent<RsProcessingPipe>() ?? GetComponentInParent<RsProcessingPipe>();
        }

        if (rsDeviceController == null)
        {
            rsDeviceController = FindFirstObjectByType<RsDeviceController>();
        }
    }

    void Start()
    {
        _cachedSourceName = gameObject.name;
        TryAutoAssignDependencies();
        _visualization = new RsPointCloudVisualization(GetComponent<MeshRenderer>());
        OnRendererStart();
    }

    /// <summary>
    /// レンダラー固有の開始処理（標準構成では processingPipe に接続する）
    /// </summary>
    protected virtual void OnRendererStart()
    {
        _initializer = new RsPointCloudInitializer(processingPipe, pointCloudFilterShader, pointCloudTransformerShader);

        if (processingPipe == null)
        {
            AppLogger.LogWarning(this, "processingPipe is not assigned.");
            return;
        }

        // RealSenseからデータを受け取るためストリーミング開始イベントを監視
        processingPipe.OnStart += OnStartStreaming;

        // Start 時点で既にストリーミングが開始していた場合は即座に初期化する
        if (processingPipe.Streaming)
        {
            OnStartStreaming(processingPipe.ActiveProfile);
        }
    }

    /// <summary>
    /// 指定されたフレームソースとプロファイルを用いて点群処理を初期化する
    /// </summary>
    protected void InitializePointCloud(IRsFrameSource frameSource, RsProcessingProfile runtimeProfile, PipelineProfile profile)
    {
        if (rsDeviceController == null)
        {
            AppLogger.LogError(this, "rsDeviceController is not assigned. Cannot initialize point cloud.");
            return;
        }

        if (_initializer != null && _initializer.IsInitialized)
            return;

        _initializer = new RsPointCloudInitializer(frameSource, runtimeProfile, pointCloudFilterShader, pointCloudTransformerShader);
        _initializer.InitializeOnStreaming(profile, rsDeviceController, maxPlaneDistance);

        if (_initializer.UseIntegratedPointCloud && _initializer.IntegratedPointCloud != null)
        {
            _initializer.UpdateIntegratedTransform(transform.localToWorldMatrix);
        }
    }

    void LateUpdate()
    {
        if (_initializer == null || !_initializer.IsInitialized) return;

        // 計算処理側へ現在のゲームオブジェクト名を伝える（主に出力ログ等の識別用）
        if (_initializer.FrameProcessor != null)
        {
            _initializer.FrameProcessor.SourceName = _cachedSourceName;
        }

        // 計測中で有ればストップウォッチをリセット

        _frameCounter++;
        // メインとなる毎フレームの点群処理（フィルタ、サンプリング、PCA等）の実行
        ComputeBuffer argsBuffer = _initializer.ExecuteFrame(
            EstimatedPoint, EstimatedDir, IsGlobalRangeFilterEnabled, _frameCounter, maxPlaneDistance);

        // 処理が成功し、描画用の引数(Args)バッファが返って来た場合、実際の描画プロセスへ進む
        if (argsBuffer != null)
        {
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
        if (_initializer == null || _initializer.IsInitialized)
            return;

        if (rsDeviceController == null)
        {
            AppLogger.LogError(this, "rsDeviceController is not assigned. Cannot initialize point cloud.");
            return;
        }

        _initializer.InitializeOnStreaming(profile, rsDeviceController, maxPlaneDistance);

        // RsGlobalPointCloudManagerなどから統合された点群を扱う設定がされた場合のイベント監視
        if (_initializer.UseIntegratedPointCloud && _initializer.IntegratedPointCloud != null)
        {
            _initializer.UpdateIntegratedTransform(transform.localToWorldMatrix);
        }
    }

    #endregion

    #region Cleanup

    // 関連するリソース（イベント購読、ComputeBufferなど）を破棄する
    protected virtual void OnRendererDispose()
    {
        if (processingPipe != null)
        {
            processingPipe.OnStart -= OnStartStreaming;
        }
        _initializer?.Dispose();
    }

    private void Dispose() => OnRendererDispose();

    #endregion
}
