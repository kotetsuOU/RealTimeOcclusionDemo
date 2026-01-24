using System;
using System.Collections.Generic;
using UnityEngine;

public class RsPointCloudCompute : IDisposable
{
    #region Constants

    private const int MAX_SAMPLE_TRANSFER = 2000;

    #endregion

    #region Private Fields

    private readonly RsFilterShaderDispatcher _dispatcher;
    private readonly float _maxPlaneDistance;
    private readonly Vector3 _globalThreshold1;
    private readonly Vector3 _globalThreshold2;

    private ComputeBuffer _filteredVerticesBuffer;
    private ComputeBuffer _countBuffer;
    private ComputeBuffer _samplingBuffer;
    private ComputeBuffer _distanceDiscardBuffer;
    private ComputeBuffer _argsBuffer;

    private readonly int[] _argsData = { 0, 1, 0, 0 };
    private readonly int[] _countCache = new int[1];

    private int _rsLength;
    private Matrix4x4 _localToWorld;
    private uint _frameCounter;
    private RsSamplingResult _lastSamplingResult;

    #endregion

    #region Public Properties

    public RsSamplingResult LastSamplingResult => _lastSamplingResult;

    #endregion

    #region Constructor

    public RsPointCloudCompute(
        ComputeShader filterShader,
        ComputeShader transformShader,
        Vector3 rsScanRange,
        float frameWidth,
        float maxPlaneDistance)
    {
        _dispatcher = new RsFilterShaderDispatcher(filterShader, transformShader);
        _maxPlaneDistance = maxPlaneDistance;
        _globalThreshold1 = new Vector3(frameWidth, frameWidth, frameWidth);
        _globalThreshold2 = rsScanRange - _globalThreshold1;
    }

    #endregion

    #region Buffer Management

    public void InitializeBuffers(int rsLength, Matrix4x4 localToWorld)
    {
        _rsLength = rsLength;
        _localToWorld = localToWorld;

        ReleaseBuffers();

        _filteredVerticesBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3, ComputeBufferType.Append);
        _countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        _samplingBuffer = new ComputeBuffer(MAX_SAMPLE_TRANSFER, sizeof(float) * 3, ComputeBufferType.Append);
        _distanceDiscardBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3, ComputeBufferType.Append);
        _argsBuffer = new ComputeBuffer(1, sizeof(int) * 4, ComputeBufferType.IndirectArguments);
        _argsBuffer.SetData(_argsData);
    }

    public void UpdateLocalToWorldMatrix(Matrix4x4 m) => _localToWorld = m;
    public ComputeBuffer GetFilteredVerticesBuffer() => _filteredVerticesBuffer;
    public ComputeBuffer GetArgsBuffer() => _argsBuffer;

    #endregion

    #region Filter & Estimate

    public (int finalCount, Vector3 point, Vector3 dir, int discardedCount, int sampledCount, float discardPercentage)
        FilterAndEstimateLine(ComputeBuffer rawVerticesBuffer, Vector3 prevPoint, Vector3 prevDir, int vertexCount)
    {
        var counts = ExecuteFilterPass(rawVerticesBuffer, prevPoint, prevDir, vertexCount);

        Vector3 point = prevPoint, dir = prevDir;
        if (counts.sampledCount > 0)
        {
            Vector3[] samples = GetSampledVertices(counts.sampledCount);
            _lastSamplingResult = RsPointCloudPCA.ComputeStatistics(samples, counts.sampledCount);
            (point, dir) = RsPointCloudPCA.EstimateLine(samples, counts.sampledCount);
        }

        return (counts.finalCount, point, dir, counts.discardedCount, counts.sampledCount, counts.discardPercentage);
    }

    public (int finalCount, Vector3 point, Vector3 dir, int discardedCount, int sampledCount, float discardPercentage)
        FilterAndEstimateLine(ComputeBuffer rawVerticesBuffer, Vector3 prevPoint, Vector3 prevDir)
        => FilterAndEstimateLine(rawVerticesBuffer, prevPoint, prevDir, _rsLength);

    public (int finalCount, int discardedCount, int sampledCount, float discardPercentage)
        FilterOnly(ComputeBuffer rawVerticesBuffer, Vector3 prevPoint, Vector3 prevDir, int vertexCount)
    {
        var counts = ExecuteFilterPass(rawVerticesBuffer, prevPoint, prevDir, vertexCount);

        if (counts.sampledCount > 0)
        {
            Vector3[] samples = GetSampledVertices(counts.sampledCount);
            _lastSamplingResult = RsPointCloudPCA.ComputeStatistics(samples, counts.sampledCount);
        }

        return (counts.finalCount, counts.discardedCount, counts.sampledCount, counts.discardPercentage);
    }

    #endregion

    #region Transform

    public ComputeBuffer TransformIndirect(ComputeBuffer rawVerticesBuffer, int vertexCount)
    {
        _filteredVerticesBuffer.SetCounterValue(0);

        _dispatcher.DispatchTransform(
            rawVerticesBuffer, _filteredVerticesBuffer, _localToWorld,
            _globalThreshold1, _globalThreshold2, vertexCount);

        ComputeBuffer.CopyCount(_filteredVerticesBuffer, _argsBuffer, 0);
        return _argsBuffer;
    }

    public ComputeBuffer TransformIndirect(ComputeBuffer rawVerticesBuffer)
        => TransformIndirect(rawVerticesBuffer, _rsLength);

    public int Transform(ComputeBuffer rawVerticesBuffer, int vertexCount)
    {
        TransformIndirect(rawVerticesBuffer, vertexCount);
        return GetBufferCount(_filteredVerticesBuffer);
    }

    public int Transform(ComputeBuffer rawVerticesBuffer) => Transform(rawVerticesBuffer, _rsLength);

    #endregion

    #region Data Access

    public void GetFilteredVerticesData(Vector3[] outVertices, int count)
    {
        if (count > 0 && count <= outVertices.Length)
            _filteredVerticesBuffer.GetData(outVertices, 0, 0, count);
    }

    public int GetLastFilteredCount() => _filteredVerticesBuffer != null ? GetBufferCount(_filteredVerticesBuffer) : 0;

    public static (Vector3 point, Vector3 dir) EstimateLineFromMergedSamples(List<RsSamplingResult> results)
        => RsPointCloudPCA.EstimateLineFromMergedSamples(results);

    #endregion

    #region Private Methods

    private (int finalCount, int discardedCount, int sampledCount, float discardPercentage)
        ExecuteFilterPass(ComputeBuffer rawVerticesBuffer, Vector3 linePoint, Vector3 lineDir, int vertexCount)
    {
        _frameCounter++;
        ResetCounters();

        float samplingRate = RsPointCloudPCA.CalculateSamplingRate(vertexCount, MAX_SAMPLE_TRANSFER);

        _dispatcher.DispatchFilter(
            rawVerticesBuffer, _filteredVerticesBuffer, _samplingBuffer, _distanceDiscardBuffer,
            _localToWorld, _globalThreshold1, _globalThreshold2,
            vertexCount, _maxPlaneDistance, linePoint, lineDir, samplingRate, (int)_frameCounter);

        ComputeBuffer.CopyCount(_filteredVerticesBuffer, _argsBuffer, 0);

        int sampledCount = Mathf.Min(GetBufferCount(_samplingBuffer), MAX_SAMPLE_TRANSFER);
        int discardedCount = GetBufferCount(_distanceDiscardBuffer);
        int finalCount = GetBufferCount(_filteredVerticesBuffer);
        float discardPercentage = sampledCount > 0 ? discardedCount * 100f / sampledCount : 0f;

        _lastSamplingResult = new RsSamplingResult();
        return (finalCount, discardedCount, sampledCount, discardPercentage);
    }

    private void ResetCounters()
    {
        _filteredVerticesBuffer.SetCounterValue(0);
        _samplingBuffer.SetCounterValue(0);
        _distanceDiscardBuffer.SetCounterValue(0);
    }

    private int GetBufferCount(ComputeBuffer buffer)
    {
        ComputeBuffer.CopyCount(buffer, _countBuffer, 0);
        _countBuffer.GetData(_countCache);
        return _countCache[0];
    }

    private Vector3[] GetSampledVertices(int count)
    {
        Vector3[] samples = new Vector3[count];
        _samplingBuffer.GetData(samples, 0, 0, count);
        return samples;
    }

    #endregion

    #region IDisposable

    public void Dispose() => ReleaseBuffers();

    private void ReleaseBuffers()
    {
        _filteredVerticesBuffer?.Release();
        _countBuffer?.Release();
        _samplingBuffer?.Release();
        _distanceDiscardBuffer?.Release();
        _argsBuffer?.Release();
    }

    #endregion
}
