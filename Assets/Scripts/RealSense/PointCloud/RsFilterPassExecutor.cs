using UnityEngine;

public class RsFilterPassExecutor
{
    #region Constants

    public const int MAX_SAMPLE_TRANSFER = 2000;

    #endregion

    #region Private Fields

    private readonly RsFilterShaderDispatcher _dispatcher;
    private readonly float _maxPlaneDistance;
    private readonly Vector3 _globalThreshold1;
    private readonly Vector3 _globalThreshold2;
    private readonly RsPointCloudAsyncReadback _asyncReadback;
    private readonly RsComputeStats _stats;

    private uint _frameCounter;
    private RsSamplingResult _lastSamplingResult;

    #endregion

    #region Public Properties

    public RsSamplingResult LastSamplingResult => _lastSamplingResult;

    #endregion

    #region Constructor

    public RsFilterPassExecutor(
        RsFilterShaderDispatcher dispatcher,
        float maxPlaneDistance,
        Vector3 globalThreshold1,
        Vector3 globalThreshold2,
        RsPointCloudAsyncReadback asyncReadback,
        RsComputeStats stats)
    {
        _dispatcher = dispatcher;
        _maxPlaneDistance = maxPlaneDistance;
        _globalThreshold1 = globalThreshold1;
        _globalThreshold2 = globalThreshold2;
        _asyncReadback = asyncReadback;
        _stats = stats;
    }

    #endregion

    #region Public Methods

    public (int finalCount, int discardedCount, int sampledCount, float discardPercentage)
        ExecuteFilterPass(
            ComputeBuffer rawVerticesBuffer,
            ComputeBuffer filteredVerticesBuffer,
            ComputeBuffer samplingBuffer,
            ComputeBuffer distanceDiscardBuffer,
            ComputeBuffer argsBuffer,
            Matrix4x4 localToWorld,
            Vector3 linePoint,
            Vector3 lineDir,
            int vertexCount)
    {
        _frameCounter++;
        _stats?.RecordFilterCall();

        if (rawVerticesBuffer == null || filteredVerticesBuffer == null || 
            samplingBuffer == null || distanceDiscardBuffer == null || argsBuffer == null)
        {
            return (0, 0, 0, 0f);
        }

        ResetCounters(filteredVerticesBuffer, samplingBuffer, distanceDiscardBuffer);

        float samplingRate = RsPointCloudPCA.CalculateSamplingRate(vertexCount, MAX_SAMPLE_TRANSFER);

        _dispatcher.DispatchFilter(
            rawVerticesBuffer, filteredVerticesBuffer, samplingBuffer, distanceDiscardBuffer,
            localToWorld, _globalThreshold1, _globalThreshold2,
            vertexCount, _maxPlaneDistance, linePoint, lineDir, samplingRate, (int)_frameCounter);

        ComputeBuffer.CopyCount(filteredVerticesBuffer, argsBuffer, 0);

        _asyncReadback.RequestAsyncReadback(filteredVerticesBuffer, samplingBuffer, distanceDiscardBuffer);

        int sampledCount = Mathf.Min(_asyncReadback.LastSampledCount, MAX_SAMPLE_TRANSFER);
        int discardedCount = _asyncReadback.LastDiscardedCount;
        int finalCount = _asyncReadback.LastFilteredCount;
        float discardPercentage = sampledCount > 0 ? discardedCount * 100f / sampledCount : 0f;

        _lastSamplingResult = new RsSamplingResult();
        return (finalCount, discardedCount, sampledCount, discardPercentage);
    }

    public void UpdateSamplingResultFromCache()
    {
        if (_asyncReadback.HasCachedSamples && _asyncReadback.CachedSamplesCount > 0)
        {
            _lastSamplingResult = RsPointCloudPCA.ComputeStatistics(
                _asyncReadback.CachedSamples, 
                _asyncReadback.CachedSamplesCount);
        }
    }

    public (Vector3 point, Vector3 dir) EstimateLineFromCache()
    {
        if (_asyncReadback.HasCachedSamples && _asyncReadback.CachedSamplesCount > 0)
        {
            return RsPointCloudPCA.EstimateLine(
                _asyncReadback.CachedSamples, 
                _asyncReadback.CachedSamplesCount);
        }
        return (Vector3.zero, Vector3.forward);
    }

    #endregion

    #region Private Methods

    private void ResetCounters(
        ComputeBuffer filteredVerticesBuffer,
        ComputeBuffer samplingBuffer,
        ComputeBuffer distanceDiscardBuffer)
    {
        filteredVerticesBuffer.SetCounterValue(0);
        samplingBuffer.SetCounterValue(0);
        distanceDiscardBuffer.SetCounterValue(0);
    }

    #endregion
}