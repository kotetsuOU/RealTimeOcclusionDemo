using Intel.RealSense;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

public class RsPointCloudInitializer
{
    private readonly RsProcessingPipe _processingPipe;
    private readonly ComputeShader _filterShader;
    private readonly ComputeShader _transformShader;

    // RealSenseからの点群データを取得するためのプロバイダー
    private RsDataProvider _dataProvider;
    // GPUで点群の計算（処理、フィルタ、PCA等）を実行するヘルパー
    private RsPointCloudCompute _compute;
    // 毎フレームの更新やフィルタロジックの呼び出しを管理するプロセッサ
    private RsPointCloudFrameProcessor _frameProcessor;
    // 複数の点群をGPU上で1つに結合して直接扱う場合に使用するクラス
    private RsIntegratedPointCloud _integratedPointCloud;

    private Vector3[] _rawVertices;
    private ComputeBuffer _rawVerticesBuffer;

    private bool _useIntegratedPointCloud = false;
    private bool _isInitialized = false;

    public RsDataProvider DataProvider => _dataProvider;
    public RsPointCloudCompute Compute => _compute;
    public RsPointCloudFrameProcessor FrameProcessor => _frameProcessor;
    public RsIntegratedPointCloud IntegratedPointCloud => _integratedPointCloud;
    public Vector3[] RawVertices => _rawVertices;
    public ComputeBuffer RawVerticesBuffer => _rawVerticesBuffer;
    public bool UseIntegratedPointCloud => _useIntegratedPointCloud;
    public bool IsInitialized => _isInitialized;

    public RsPointCloudInitializer(
        RsProcessingPipe processingPipe,
        ComputeShader filterShader,
        ComputeShader transformShader)
    {
        _processingPipe = processingPipe;
        _filterShader = filterShader;
        _transformShader = transformShader;
    }

    // 実機の代わりにダミーの合成データ（円柱など）で点群を初期化する
    public void InitializeSynthetic(
        RsPointCloudSyntheticData.SyntheticShape shape,
        int pointCount,
        float scale,
        float maxPlaneDistance,
        Matrix4x4 localToWorldMatrix,
        RsPerformanceLogger logger,
        Stopwatch stopwatch)
    {
        UnityEngine.Debug.Log("[RsPointCloudInitializer] Initializing Synthetic Data...");

        // ダミーデータ用のスキャン範囲（適当なサイズ）
        Vector3 scanRange = new Vector3(10f, 10f, 10f);
        _compute = new RsPointCloudCompute(_filterShader, _transformShader, scanRange, 640, maxPlaneDistance);
        _compute.InitializeBuffers(pointCount, localToWorldMatrix);

        _frameProcessor = new RsPointCloudFrameProcessor(_compute, logger, stopwatch);

        _rawVertices = new Vector3[pointCount];
        _rawVerticesBuffer = new ComputeBuffer(pointCount, sizeof(float) * 3);

        // RsPointCloudSyntheticData を用いてダミー点群の頂点配列を生成する
        var syntheticGenerator = new RsPointCloudSyntheticData(shape, pointCount, scale);
        syntheticGenerator.GenerateInto(_rawVertices);
        // 生成した配列をGPUのComputeBufferに送る
        _rawVerticesBuffer.SetData(_rawVertices);

        _isInitialized = true;
        UnityEngine.Debug.Log($"[RsPointCloudInitializer] Synthetic Data Initialized with {pointCount} points.");
    }

    // パイプライン(ストリーミング)に接続して各リソースを初期化する
    public void InitializeOnStreaming(
        PipelineProfile profile,
        RsDeviceController deviceController,
        float maxPlaneDistance,
        RsPerformanceLogger logger,
        Stopwatch stopwatch)
    {
        int width = 0;
        int height = 0;

        // すでに統合用のPCモジュールが紐づいているかどうかの確認
        TryConnectIntegratedPointCloud();

        if (_useIntegratedPointCloud)
        {
            // GPUダイレクトモードなど、他で結合されたバッファを利用する場合のサイズ取得
            (width, height) = GetDepthDimensionsFromProfile(profile);
            UnityEngine.Debug.Log("[RsPointCloudInitializer] Using RsIntegratedPointCloud (GPU Direct Mode)");
        }
        else
        {
            // 個別のRealSenseカメラからデータプロバイダーを経由して随時コピーするモード
            _dataProvider = new RsDataProvider(_processingPipe);
            _dataProvider.Start();
            width = _dataProvider.FrameWidth;
            height = _dataProvider.FrameHeight;
            UnityEngine.Debug.Log("[RsPointCloudInitializer] Using RsPointCloud via RsDataProvider");
        }

        int rsLength = width * height;
        if (rsLength == 0)
        {
            UnityEngine.Debug.LogError("[RsPointCloudInitializer] Failed to get depth stream dimensions");
            return;
        }

        // RsPointCloudCompute のインスタンス作成し、スキャン領域とバッファサイズを設定
        _compute = new RsPointCloudCompute(
            _filterShader,
            _transformShader,
            deviceController.RealSenseScanRange,
            deviceController.FrameWidth,
            maxPlaneDistance);

        // 処理に必要な構造体や各ComputeBufferを作成
        _compute.InitializeBuffers(rsLength, Matrix4x4.identity);

        _frameProcessor = new RsPointCloudFrameProcessor(_compute, logger, stopwatch);

        // 統合点群モード以外の場合は、自前で点群情報を受け取るための配列・バッファを生成する
        if (!_useIntegratedPointCloud)
        {
            _rawVertices = new Vector3[rsLength];
            _rawVerticesBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3);
        }

        _isInitialized = true;
    }

    // RealSenseプロファイル情報から、使用しているDepthストリームの解像度を取り出す
    private (int width, int height) GetDepthDimensionsFromProfile(PipelineProfile profile)
    {
        using (var depth = profile.Streams
            .FirstOrDefault(s => s.Stream == Intel.RealSense.Stream.Depth && s.Format == Intel.RealSense.Format.Z16)
            ?.As<VideoStreamProfile>())
        {
            if (depth != null)
            {
                return (depth.Width, depth.Height);
            }
        }
        return (0, 0); // 失敗時
    }

    // パイプラインのブロック内で、統合点群マネージャ(RsIntegratedPointCloud)が存在するかを探す
    private void TryConnectIntegratedPointCloud()
    {
        if (_processingPipe == null || _processingPipe.profile == null) return;

        foreach (var block in _processingPipe.profile._processingBlocks)
        {
            if (block is RsIntegratedPointCloud integrated)
            {
                _integratedPointCloud = integrated;
                _useIntegratedPointCloud = true; // 設定が存在すれば、GPUメモリ上で結合されたバッファを適用するモードになる
                UnityEngine.Debug.Log("[RsPointCloudInitializer] Connected to RsIntegratedPointCloud");
                return;
            }
        }
    }

    // 最新の変換行列をIntegratedPointCloudに渡す
    public void UpdateIntegratedTransform(Matrix4x4 matrix)
    {
        _integratedPointCloud?.UpdateTransformMatrix(matrix);
    }

    // リソースを安全に解放し、ComputeBufferからのメモリリークを防ぐ
    public void Dispose()
    {
        _dataProvider?.Dispose();
        _compute?.Dispose();

        if (_rawVerticesBuffer != null)
        {
            _rawVerticesBuffer.Release();
            _rawVerticesBuffer = null;
        }

        _isInitialized = false;
    }
}
