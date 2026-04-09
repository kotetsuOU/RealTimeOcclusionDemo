using Intel.RealSense;
using UnityEngine;

public class RsPointCloudFrameProcessor
{
    private readonly RsPointCloudCompute _compute;
    private readonly RsPerformanceLogger _logger;
    private readonly System.Diagnostics.Stopwatch _stopwatch;

    private Vector3 _estimatedPoint = Vector3.zero; // PCAなどで推定された中心点
    private Vector3 _estimatedDir = Vector3.forward; // PCAなどで推定された方向ベクトル

    public string SourceName { get; set; } = string.Empty; // ログ等を出力する際の対象オブジェクト名

    public Vector3 EstimatedPoint => _estimatedPoint;
    public Vector3 EstimatedDir => _estimatedDir;

    public RsPointCloudFrameProcessor(RsPointCloudCompute compute, RsPerformanceLogger logger, System.Diagnostics.Stopwatch stopwatch)
    {
        _compute = compute;
        _logger = logger;
        _stopwatch = stopwatch;
    }

    // 合成(ダミー)データ用の点群を処理する
    public ComputeBuffer ProcessSyntheticFrame(
        ComputeBuffer rawVerticesBuffer,
        int totalPointCount,
        Vector3 linePoint,
        Vector3 lineDir,
        bool isGlobalRangeFilterEnabled,
        float maxPlaneDistance)
    {
        if (_compute == null || rawVerticesBuffer == null) return null;

        return ProcessWithFilter(rawVerticesBuffer, totalPointCount, linePoint, lineDir, isGlobalRangeFilterEnabled, -1, maxPlaneDistance);
    }

    // 複数カメラから統合された点群バッファを処理する
    public ComputeBuffer ProcessIntegratedFrame(
        ComputeBuffer sourceBuffer,
        int pointCount,
        Vector3 linePoint,
        Vector3 lineDir,
        bool isGlobalRangeFilterEnabled,
        int frameCounter,
        float maxPlaneDistance)
    {
        if (sourceBuffer == null || pointCount == 0) return null;

        return ProcessWithFilter(sourceBuffer, pointCount, linePoint, lineDir, isGlobalRangeFilterEnabled, frameCounter, maxPlaneDistance);
    }

    // RealSenseカメラから取得した新規フレームデータを処理する
    public ComputeBuffer ProcessRealSenseFrame(
        Points points,
        Vector3[] rawVertices,
        ComputeBuffer rawVerticesBuffer,
        Vector3 linePoint,
        Vector3 lineDir,
        bool isGlobalRangeFilterEnabled,
        int frameCounter,
        float maxPlaneDistance)
    {
        if (points.VertexData == System.IntPtr.Zero) return null;

        // C++側のPointsデータから、Unity(CPU)のVector3配列へコピーする
        points.CopyVertices(rawVertices);

        // ComputeShaderへ渡すためのGPUバッファ(ComputeBuffer)に転送する
        if (rawVerticesBuffer != null)
        {
            rawVerticesBuffer.SetData(rawVertices);
        }

        return ProcessWithFilter(rawVerticesBuffer, rawVertices.Length, linePoint, lineDir, isGlobalRangeFilterEnabled, frameCounter, maxPlaneDistance);
    }

    // 全てのフレーム処理の共通パス：フィルタリングやPCAの実行
    private ComputeBuffer ProcessWithFilter(
        ComputeBuffer sourceBuffer,
        int pointCount,
        Vector3 linePoint,
        Vector3 lineDir,
        bool isGlobalRangeFilterEnabled,
        int frameCounter,
        float maxPlaneDistance)
    {
        long discardedCount = 0;
        long totalCount = pointCount;
        ComputeBuffer argsBuffer;

        // グローバルなフィルタ（不要な点の除外）が有効な場合
        if (isGlobalRangeFilterEnabled)
        {
            bool useIntegratedPCA = RsGlobalPointCloudManager.Instance != null &&
                                     RsGlobalPointCloudManager.Instance.IsIntegratedPCAMode;

            // 統合PCAモードの場合は自身の個別のPCAは実行せずにフィルタだけを行なう
            if (useIntegratedPCA)
            {
                var result = _compute.FilterOnly(SourceName, sourceBuffer, linePoint, lineDir, pointCount, maxPlaneDistance);
                discardedCount = result.discardedCount;
                totalCount = result.sampledCount;
            }
            else
            {
                // 個別のPCAモードの場合は自身の点群から基準線の推定も行う
                var result = _compute.FilterAndEstimateLine(SourceName, sourceBuffer, linePoint, lineDir, pointCount, maxPlaneDistance);
                _estimatedPoint = result.point;
                _estimatedDir = result.dir;
                discardedCount = result.discardedCount;
                totalCount = result.sampledCount;
            }

            // DrawProcedural 間接描画用の引数を取り出す
            argsBuffer = _compute.GetArgsBuffer();
        }
        else
        {
            // フィルタを実行せず点群に直接トランスフォーム(Matrix適用など)をかける
            argsBuffer = _compute.TransformIndirect(sourceBuffer, pointCount);
            discardedCount = 0;
        }

        // 計測ログにパフォーマンスデータを記録
        if (frameCounter >= 0 && _logger != null && _logger.IsLogging)
        {
            _stopwatch?.Stop();
            _logger.LogFrame(frameCounter, _stopwatch?.Elapsed.TotalMilliseconds ?? 0, discardedCount, totalCount, isGlobalRangeFilterEnabled);
        }

        return argsBuffer;
    }

    public void UpdateEstimation(Vector3 point, Vector3 dir)
    {
        _estimatedPoint = point;
        _estimatedDir = dir;
    }
}
