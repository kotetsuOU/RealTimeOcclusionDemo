using Intel.RealSense;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Core.Logging;

public class RsPointCloudInitializer
{
    private readonly IRsFrameSource _frameSource;
    private readonly RsProcessingProfile _processingProfile;
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
        : this(processingPipe, processingPipe != null ? processingPipe.profile : null, filterShader, transformShader)
    {
    }

    public RsPointCloudInitializer(
        IRsFrameSource frameSource,
        RsProcessingProfile processingProfile,
        ComputeShader filterShader,
        ComputeShader transformShader)
    {
        _frameSource = frameSource;
        _processingProfile = processingProfile;
        _filterShader = filterShader;
        _transformShader = transformShader;
    }

    // パイプライン(ストリーミング)に接続して各リソースを初期化する
    public void InitializeOnStreaming(
        PipelineProfile profile,
        RsDeviceController deviceController,
        float maxPlaneDistance)
    {
        int width = 0;
        int height = 0;

        // すでに統合用のPCモジュールが紐づいているかどうかの確認
        TryConnectIntegratedPointCloud();

        if (_useIntegratedPointCloud)
        {
            // GPUダイレクトモードなど、他で結合されたバッファを利用する場合のサイズ取得
            (width, height) = GetDepthDimensionsFromProfile(profile);
            AppLogger.Log("RsPointCloudInitializer", "Using RsIntegratedPointCloud (GPU Direct Mode)");
        }
        else
        {
            // 個別のRealSenseカメラからデータプロバイダーを経由して随時コピーするモード
            _dataProvider = new RsDataProvider(_frameSource);
            _dataProvider.Start();
            width = _dataProvider.FrameWidth;
            height = _dataProvider.FrameHeight;
            AppLogger.Log("RsPointCloudInitializer", "Using RsPointCloud via RsDataProvider");
        }

        int rsLength = width * height;
        if (rsLength == 0)
        {
            AppLogger.LogError("RsPointCloudInitializer", "Failed to get depth stream dimensions");
            return;
        }

        // RsPointCloudCompute のインスタンス作成し、スキャン領域とバッファサイズを設定
        _compute = new RsPointCloudCompute(
            _filterShader,
            _transformShader,
            deviceController.ScanMin,
            deviceController.ScanMax);

        // 処理に必要な構造体や各ComputeBufferを作成
        _compute.InitializeBuffers(rsLength, Matrix4x4.identity);

        _frameProcessor = new RsPointCloudFrameProcessor(_compute);

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
        if (_processingProfile == null || _processingProfile._processingBlocks == null) return;

        foreach (var block in _processingProfile._processingBlocks)
        {
            if (block is RsIntegratedPointCloud integrated)
            {
                _integratedPointCloud = integrated;
                _useIntegratedPointCloud = true; // 設定が存在すれば、GPUメモリ上で結合されたバッファを適用するモードになる
                AppLogger.Log("RsPointCloudInitializer", "Connected to RsIntegratedPointCloud");
                return;
            }
        }
    }

    // 最新の変換行列をIntegratedPointCloudに渡す
    public void UpdateIntegratedTransform(Matrix4x4 matrix)
    {
        _integratedPointCloud?.UpdateTransformMatrix(matrix);
    }

    /// <summary>
    /// 現在のフレームの点群処理（統合点群モードまたは単体RealSenseモード）を実行し、描画用ArgsBufferを返す
    /// </summary>
    public ComputeBuffer ExecuteFrame(
        Vector3 linePoint,
        Vector3 lineDir,
        bool isGlobalRangeFilterEnabled,
        int frameCounter,
        float maxPlaneDistance)
    {
        if (_frameProcessor == null)
            return null;

        if (_useIntegratedPointCloud)
        {
            if (_integratedPointCloud?.PointCloudBuffer == null)
                return null;

            int pointCount = _integratedPointCloud.LastPointCount;
            if (pointCount == 0)
                return null;

            return _frameProcessor.ProcessIntegratedFrame(
                _integratedPointCloud.PointCloudBuffer, pointCount,
                linePoint, lineDir, isGlobalRangeFilterEnabled, frameCounter, maxPlaneDistance);
        }

        if (_dataProvider == null || !_dataProvider.PollForFrame(out var points))
            return null;

        using (points)
        {
            return _frameProcessor.ProcessRealSenseFrame(
                points, _rawVertices, _rawVerticesBuffer,
                linePoint, lineDir, isGlobalRangeFilterEnabled, frameCounter, maxPlaneDistance);
        }
    }

    /// <summary>
    /// フィルタリング済みの頂点配列をCPU側に読み戻す（同期実行）
    /// </summary>
    public Vector3[] ReadFilteredVertices(UnityEngine.Object logContext)
    {
        if (_compute == null)
        {
            AppLogger.LogWarning(logContext, "Compute instance is null.");
            return System.Array.Empty<Vector3>();
        }

        int count = _compute.GetLastFilteredCount();
        if (count <= 0)
        {
            AppLogger.LogWarning(logContext, $"Vertex count is {count}.");
            return System.Array.Empty<Vector3>();
        }

        Vector3[] result = new Vector3[count];
        _compute.GetFilteredVerticesData(result, count);
        return result;
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
