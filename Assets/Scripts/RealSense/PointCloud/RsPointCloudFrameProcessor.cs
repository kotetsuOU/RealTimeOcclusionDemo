using Intel.RealSense;
using UnityEngine;

public class RsPointCloudFrameProcessor
{
    private readonly RsPointCloudCompute _compute;
    private readonly RsPerformanceLogger _logger;
    private readonly System.Diagnostics.Stopwatch _stopwatch;

    private Vector3 _estimatedPoint = Vector3.zero;
    private Vector3 _estimatedDir = Vector3.forward;

    public string SourceName { get; set; } = string.Empty;

    public Vector3 EstimatedPoint => _estimatedPoint;
    public Vector3 EstimatedDir => _estimatedDir;

    public RsPointCloudFrameProcessor(RsPointCloudCompute compute, RsPerformanceLogger logger, System.Diagnostics.Stopwatch stopwatch)
    {
        _compute = compute;
        _logger = logger;
        _stopwatch = stopwatch;
    }

    public ComputeBuffer ProcessSyntheticFrame(
        ComputeBuffer rawVerticesBuffer,
        int totalPointCount,
        Vector3 linePoint,
        Vector3 lineDir,
        bool isGlobalRangeFilterEnabled)
    {
        if (_compute == null || rawVerticesBuffer == null) return null;

        return ProcessWithFilter(rawVerticesBuffer, totalPointCount, linePoint, lineDir, isGlobalRangeFilterEnabled, -1);
    }

    public ComputeBuffer ProcessIntegratedFrame(
        ComputeBuffer sourceBuffer,
        int pointCount,
        Vector3 linePoint,
        Vector3 lineDir,
        bool isGlobalRangeFilterEnabled,
        int frameCounter)
    {
        if (sourceBuffer == null || pointCount == 0) return null;

        return ProcessWithFilter(sourceBuffer, pointCount, linePoint, lineDir, isGlobalRangeFilterEnabled, frameCounter);
    }

    public ComputeBuffer ProcessRealSenseFrame(
        Points points,
        Vector3[] rawVertices,
        ComputeBuffer rawVerticesBuffer,
        Vector3 linePoint,
        Vector3 lineDir,
        bool isGlobalRangeFilterEnabled,
        int frameCounter)
    {
        if (points.VertexData == System.IntPtr.Zero) return null;

        points.CopyVertices(rawVertices);

        if (rawVerticesBuffer != null)
        {
            rawVerticesBuffer.SetData(rawVertices);
        }

        return ProcessWithFilter(rawVerticesBuffer, rawVertices.Length, linePoint, lineDir, isGlobalRangeFilterEnabled, frameCounter);
    }

    private ComputeBuffer ProcessWithFilter(
        ComputeBuffer sourceBuffer,
        int pointCount,
        Vector3 linePoint,
        Vector3 lineDir,
        bool isGlobalRangeFilterEnabled,
        int frameCounter)
    {
        long discardedCount = 0;
        long totalCount = pointCount;
        ComputeBuffer argsBuffer;

        if (isGlobalRangeFilterEnabled)
        {
            bool useIntegratedPCA = RsGlobalPointCloudManager.Instance != null &&
                                     RsGlobalPointCloudManager.Instance.IsIntegratedPCAMode;

            if (useIntegratedPCA)
            {
                var result = _compute.FilterOnly(SourceName, sourceBuffer, linePoint, lineDir, pointCount);
                discardedCount = result.discardedCount;
                totalCount = result.sampledCount;
            }
            else
            {
                var result = _compute.FilterAndEstimateLine(SourceName, sourceBuffer, linePoint, lineDir, pointCount);
                _estimatedPoint = result.point;
                _estimatedDir = result.dir;
                discardedCount = result.discardedCount;
                totalCount = result.sampledCount;
            }

            argsBuffer = _compute.GetArgsBuffer();
        }
        else
        {
            argsBuffer = _compute.TransformIndirect(sourceBuffer, pointCount);
            discardedCount = 0;
        }

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
