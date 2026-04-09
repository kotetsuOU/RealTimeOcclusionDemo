using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

public class RsFilterPassExecutor
{
    #region Constants

    // PCAのために抽出するサンプリング点の最大数（これ以上は切り捨てるか縮小される）
    public const int MAX_SAMPLE_TRANSFER = 2000;

    #endregion

    #region Private Fields

    private readonly RsFilterShaderDispatcher _dispatcher; // 実際のDispatch命令を投げ持つ
    private readonly Vector3 _globalThreshold1; // 除外判定などの閾値1
    private readonly Vector3 _globalThreshold2; // 除外判定などの閾値2
    private readonly RsPointCloudAsyncReadback _asyncReadback; // GPUからの非同期読み出し管理
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
        Vector3 globalThreshold1,
        Vector3 globalThreshold2,
        RsPointCloudAsyncReadback asyncReadback,
        RsComputeStats stats)
    {
        _dispatcher = dispatcher;
        _globalThreshold1 = globalThreshold1;
        _globalThreshold2 = globalThreshold2;
        _asyncReadback = asyncReadback;
        _stats = stats;
    }

    #endregion

    #region Public Methods

    private static readonly string s_sampleFilterDispatchBase = "RsPointCloud.Filter.DispatchFilter";

    private static readonly ProfilerMarker s_cpuMarkerExecuteFilterPass =
        new ProfilerMarker("RsPointCloud.Filter.ExecuteFilterPass");

    private readonly CommandBuffer _cmd = new CommandBuffer { name = "RsPointCloudCompute" };

    // C#側から引き渡された各種バッファを利用し、ComputeShaderをキックして点のフィルタリングとPCA用サンプリングを行う
    public (int finalCount, int discardedCount, int sampledCount, float discardPercentage)
        ExecuteFilterPass(
            string sourceName,
            ComputeBuffer rawVerticesBuffer,
            ComputeBuffer filteredVerticesBuffer,
            ComputeBuffer samplingBuffer,
            ComputeBuffer distanceDiscardBuffer,
            ComputeBuffer argsBuffer,
            Matrix4x4 localToWorld,
            Vector3 linePoint,
            Vector3 lineDir,
            int vertexCount,
            float maxPlaneDistance)
    {
        using (s_cpuMarkerExecuteFilterPass.Auto())
        {
        _frameCounter++;
        _stats?.RecordFilterCall();

        if (rawVerticesBuffer == null || filteredVerticesBuffer == null ||
            samplingBuffer == null || distanceDiscardBuffer == null || argsBuffer == null)
        {
            return (0, 0, 0, 0f);
        }

        // 追加型バッファ(Append)であるため、毎フレーム最初にカウントを0にリセットする必要がある
        ResetCounters(filteredVerticesBuffer, samplingBuffer, distanceDiscardBuffer);

        // 全点数と最大サンプリング数の比率から、サンプリング間隔(取得率)を計算
        float samplingRate = RsPointCloudPCA.CalculateSamplingRate(vertexCount, MAX_SAMPLE_TRANSFER);

        _cmd.Clear();

        string sampleName = string.IsNullOrWhiteSpace(sourceName)
            ? s_sampleFilterDispatchBase
            : $"{s_sampleFilterDispatchBase}/{sourceName}";

        _cmd.BeginSample(sampleName);
        // シェーダへの引数セットとDispatchをディスパッチャに依頼
        _dispatcher.DispatchFilter(
            _cmd,
            rawVerticesBuffer, filteredVerticesBuffer, samplingBuffer, distanceDiscardBuffer,
            localToWorld, _globalThreshold1, _globalThreshold2,
            vertexCount, maxPlaneDistance, linePoint, lineDir, samplingRate, (int)_frameCounter);
        _cmd.EndSample(sampleName);

        // コマンドバッファを実行
        Graphics.ExecuteCommandBuffer(_cmd);

        // 後段の描画用のArgsバッファへ、出力された点の総数をコピーする
        ComputeBuffer.CopyCount(filteredVerticesBuffer, argsBuffer, 0);

        // 各種バッファに書き込まれた数などの情報をGPUからCPUへ非同期でリードバック要求する
        _asyncReadback.RequestAsyncReadback(filteredVerticesBuffer, samplingBuffer, distanceDiscardBuffer);

        // 過去に返ってきた、非同期リードバックの結果をとりあえずの「最終数」として受け取る（多少の遅延あり）
        int sampledCount = Mathf.Min(_asyncReadback.LastSampledCount, MAX_SAMPLE_TRANSFER);
        int discardedCount = _asyncReadback.LastDiscardedCount;
        int finalCount = _asyncReadback.LastFilteredCount;
        float discardPercentage = sampledCount > 0 ? discardedCount * 100f / sampledCount : 0f;

        _lastSamplingResult = new RsSamplingResult();
        return (finalCount, discardedCount, sampledCount, discardPercentage);
        }
    }

    // 非同期で届いたサンプリングデータを元に、全体の統計(分散など)を再計算する
    public void UpdateSamplingResultFromCache()
    {
        if (_asyncReadback.HasCachedSamples && _asyncReadback.CachedSamplesCount > 0)
        {
            _lastSamplingResult = RsPointCloudPCA.ComputeStatistics(
                _asyncReadback.CachedSamples, 
                _asyncReadback.CachedSamplesCount);
        }
    }

    // キャッシュされたサンプリング点に対してPCA(主成分分析)を実行し、基準線ベクトルを生成する
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