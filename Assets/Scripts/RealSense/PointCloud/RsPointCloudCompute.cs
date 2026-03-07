using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;

public class RsPointCloudCompute : IDisposable
{
    #region Private Fields

    private readonly RsFilterShaderDispatcher _dispatcher;
    private readonly float _maxPlaneDistance;
    private readonly Vector3 _globalThreshold1;
    private readonly Vector3 _globalThreshold2;

    private ComputeBuffer _filteredVerticesBuffer;
    private ComputeBuffer _samplingBuffer;
    private ComputeBuffer _distanceDiscardBuffer;
    private ComputeBuffer _argsBuffer;

    private readonly int[] _argsData = { 0, 1, 0, 0 };

    private int _rsLength;
    private Matrix4x4 _localToWorld;
    
    private RsPointCloudAsyncReadback _asyncReadback;
    private RsFilterPassExecutor _filterPassExecutor;

    private readonly RsComputeStats _stats = new RsComputeStats();

    private static readonly string s_sampleTransformDispatch = "RsPointCloud.Transform.DispatchTransform";
    private readonly CommandBuffer _transformCmd = new CommandBuffer { name = "RsPointCloudCompute.Transform" };

    #endregion

    #region Public Properties

    public RsSamplingResult LastSamplingResult => _filterPassExecutor?.LastSamplingResult ?? new RsSamplingResult();
    
    public RsComputeStats Stats => _stats;
    public bool IsFilteredCountReadbackPending => _asyncReadback != null && _asyncReadback.IsCountReadbackPending;

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
        _asyncReadback = new RsPointCloudAsyncReadback(_stats);
        
        _filterPassExecutor = new RsFilterPassExecutor(
            _dispatcher,
            maxPlaneDistance,
            _globalThreshold1,
            _globalThreshold2,
            _asyncReadback,
            _stats);
    }

    #endregion

    #region Buffer Management

    public void InitializeBuffers(int rsLength, Matrix4x4 localToWorld)
    {
        _rsLength = rsLength;
        _localToWorld = localToWorld;

        ReleaseBuffers();

        _filteredVerticesBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3, ComputeBufferType.Append);
        _samplingBuffer = new ComputeBuffer(RsFilterPassExecutor.MAX_SAMPLE_TRANSFER, sizeof(float) * 3, ComputeBufferType.Append);
        _distanceDiscardBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3, ComputeBufferType.Append);
        _argsBuffer = new ComputeBuffer(1, sizeof(int) * 4, ComputeBufferType.IndirectArguments);
        _argsBuffer.SetData(_argsData);
        
        if (_asyncReadback == null)
        {
            _asyncReadback = new RsPointCloudAsyncReadback(_stats);
            _filterPassExecutor = new RsFilterPassExecutor(
                _dispatcher,
                _maxPlaneDistance,
                _globalThreshold1,
                _globalThreshold2,
                _asyncReadback,
                _stats);
        }
    }

    public void UpdateLocalToWorldMatrix(Matrix4x4 m) => _localToWorld = m;
    public ComputeBuffer GetFilteredVerticesBuffer() => _filteredVerticesBuffer;
    public ComputeBuffer GetArgsBuffer() => _argsBuffer;

    #endregion

    #region Filter & Estimate

    public (int finalCount, Vector3 point, Vector3 dir, int discardedCount, int sampledCount, float discardPercentage)
        FilterAndEstimateLine(string sourceName, ComputeBuffer rawVerticesBuffer, Vector3 prevPoint, Vector3 prevDir, int vertexCount)
    {
        var counts = _filterPassExecutor.ExecuteFilterPass(
            sourceName,
            rawVerticesBuffer,
            _filteredVerticesBuffer,
            _samplingBuffer,
            _distanceDiscardBuffer,
            _argsBuffer,
            _localToWorld,
            prevPoint,
            prevDir,
            vertexCount);

        Vector3 point = prevPoint, dir = prevDir;
        
        if (_asyncReadback.HasCachedSamples && _asyncReadback.CachedSamplesCount > 0)
        {
            _filterPassExecutor.UpdateSamplingResultFromCache();
            (point, dir) = _filterPassExecutor.EstimateLineFromCache();
        }

        return (counts.finalCount, point, dir, counts.discardedCount, counts.sampledCount, counts.discardPercentage);
    }

    public (int finalCount, Vector3 point, Vector3 dir, int discardedCount, int sampledCount, float discardPercentage)
        FilterAndEstimateLine(string sourceName, ComputeBuffer rawVerticesBuffer, Vector3 prevPoint, Vector3 prevDir)
        => FilterAndEstimateLine(sourceName, rawVerticesBuffer, prevPoint, prevDir, _rsLength);

    public (int finalCount, Vector3 point, Vector3 dir, int discardedCount, int sampledCount, float discardPercentage)
        FilterAndEstimateLine(ComputeBuffer rawVerticesBuffer, Vector3 prevPoint, Vector3 prevDir, int vertexCount)
        => FilterAndEstimateLine(string.Empty, rawVerticesBuffer, prevPoint, prevDir, vertexCount);

    public (int finalCount, Vector3 point, Vector3 dir, int discardedCount, int sampledCount, float discardPercentage)
        FilterAndEstimateLine(ComputeBuffer rawVerticesBuffer, Vector3 prevPoint, Vector3 prevDir)
        => FilterAndEstimateLine(string.Empty, rawVerticesBuffer, prevPoint, prevDir, _rsLength);

    public (int finalCount, int discardedCount, int sampledCount, float discardPercentage)
        FilterOnly(string sourceName, ComputeBuffer rawVerticesBuffer, Vector3 prevPoint, Vector3 prevDir, int vertexCount)
    {
        var counts = _filterPassExecutor.ExecuteFilterPass(
            sourceName,
            rawVerticesBuffer,
            _filteredVerticesBuffer,
            _samplingBuffer,
            _distanceDiscardBuffer,
            _argsBuffer,
            _localToWorld,
            prevPoint,
            prevDir,
            vertexCount);
            
        if (_asyncReadback.HasCachedSamples && _asyncReadback.CachedSamplesCount > 0)
        {
            _filterPassExecutor.UpdateSamplingResultFromCache();
        }

        return (counts.finalCount, counts.discardedCount, counts.sampledCount, counts.discardPercentage);
    }

    public (int finalCount, int discardedCount, int sampledCount, float discardPercentage)
        FilterOnly(ComputeBuffer rawVerticesBuffer, Vector3 prevPoint, Vector3 prevDir, int vertexCount)
        => FilterOnly(string.Empty, rawVerticesBuffer, prevPoint, prevDir, vertexCount);

    #endregion

    #region Transform

    public ComputeBuffer TransformIndirect(ComputeBuffer rawVerticesBuffer, int vertexCount)
    {
        _filteredVerticesBuffer.SetCounterValue(0);

        _transformCmd.Clear();
        _transformCmd.BeginSample(s_sampleTransformDispatch);
        _dispatcher.DispatchTransform(
            _transformCmd,
            rawVerticesBuffer, _filteredVerticesBuffer, _localToWorld,
            _globalThreshold1, _globalThreshold2, vertexCount);
        _transformCmd.EndSample(s_sampleTransformDispatch);
        Graphics.ExecuteCommandBuffer(_transformCmd);

        ComputeBuffer.CopyCount(_filteredVerticesBuffer, _argsBuffer, 0);
        _asyncReadback.RequestFilteredCountReadback(_filteredVerticesBuffer);
        return _argsBuffer;
    }

    public ComputeBuffer TransformIndirect(ComputeBuffer rawVerticesBuffer)
        => TransformIndirect(rawVerticesBuffer, _rsLength);

    public int Transform(ComputeBuffer rawVerticesBuffer, int vertexCount)
    {
        TransformIndirect(rawVerticesBuffer, vertexCount);
        return _asyncReadback.LastFilteredCount;
    }

    public int Transform(ComputeBuffer rawVerticesBuffer) => Transform(rawVerticesBuffer, _rsLength);

    #endregion

    #region Data Access

    public void GetFilteredVerticesData(Vector3[] outVertices, int count)
    {
        if (count > 0 && count <= outVertices.Length)
            _filteredVerticesBuffer.GetData(outVertices, 0, 0, count);
    }

    public int GetLastFilteredCount() => _asyncReadback.LastFilteredCount;

    public static (Vector3 point, Vector3 dir) EstimateLineFromMergedSamples(List<RsSamplingResult> results)
        => RsPointCloudPCA.EstimateLineFromMergedSamples(results);

    #endregion

    #region IDisposable

    public void Dispose() => ReleaseBuffers();

    private void ReleaseBuffers()
    {
        _asyncReadback?.Dispose();
        _asyncReadback = null;
        _filterPassExecutor = null;
        _filteredVerticesBuffer?.Release();
        _filteredVerticesBuffer = null;
        _samplingBuffer?.Release();
        _samplingBuffer = null;
        _distanceDiscardBuffer?.Release();
        _distanceDiscardBuffer = null;
        _argsBuffer?.Release();
        _argsBuffer = null;
    }

    #endregion
}
