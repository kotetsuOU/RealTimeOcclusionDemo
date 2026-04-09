using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;

public class RsPointCloudCompute : IDisposable
{
    #region Private Fields

    private readonly RsFilterShaderDispatcher _dispatcher; // ComputeShaderのカーネルをスケジュールする役割を担う
    private readonly Vector3 _globalThreshold1; // サンプリング用の閾値１
    private readonly Vector3 _globalThreshold2; // サンプリング用の閾値２

    private ComputeBuffer _filteredVerticesBuffer; // 最終的に有効な点だけを保持する描画用バッファ
    private ComputeBuffer _samplingBuffer;         // PCA処理用のサンプリング点を保持するバッファ
    private ComputeBuffer _distanceDiscardBuffer;  // (デバッグ・拡張用)距離超過などで棄却された点を入れるバッファ
    private ComputeBuffer _argsBuffer;             // DrawProceduralIndirectで使用するための引数配列バッファ(4 int)

    // Graphics.DrawProceduralIndirect に渡すための初期引数 (vertexCountPerInstance, instanceCount, startVertexLocation, startInstanceLocation)
    // C#側からは0番目に頂点数(PointCount)が書き込まれるよう更新される
    private readonly int[] _argsData = { 0, 1, 0, 0 };

    private int _rsLength;             // 元となるカメラ深度データ(全ピクセル)の数
    private Matrix4x4 _localToWorld;   // RealSenseのローカル座標からワールド座標へ変換する行列

    private RsPointCloudAsyncReadback _asyncReadback; // GPUからの情報の非同期読み出し(リードバック)を処理
    private RsFilterPassExecutor _filterPassExecutor; // 実際のフィルタリングパスの組み立てと実行を管理

    private readonly RsComputeStats _stats = new RsComputeStats(); // C#側に公開する計測・パフォーマンス結果の格納プロパティ

    private static readonly string s_sampleTransformDispatch = "RsPointCloud.Transform.DispatchTransform";
    private readonly CommandBuffer _transformCmd = new CommandBuffer { name = "RsPointCloudCompute.Transform" };

    #endregion

    #region Public Properties

    // フィルタパス中で処理されたダウンサンプリングや分布推定の結果
    public RsSamplingResult LastSamplingResult => _filterPassExecutor?.LastSamplingResult ?? new RsSamplingResult();

    public RsComputeStats Stats => _stats;
    public bool IsFilteredCountReadbackPending => _asyncReadback != null && _asyncReadback.IsCountReadbackPending;

    #endregion

    #region Constructor

    // コンストラクタ：各種リソースの初期化、ヘルパークラスの生成
    public RsPointCloudCompute(
        ComputeShader filterShader,
        ComputeShader transformShader,
        Vector3 rsScanRange,
        float frameWidth)
    {
        _dispatcher = new RsFilterShaderDispatcher(filterShader, transformShader);
        _globalThreshold1 = new Vector3(frameWidth, frameWidth, frameWidth);
        _globalThreshold2 = rsScanRange - _globalThreshold1;
        _asyncReadback = new RsPointCloudAsyncReadback(_stats);

        _filterPassExecutor = new RsFilterPassExecutor(
            _dispatcher,
            _globalThreshold1,
            _globalThreshold2,
            _asyncReadback,
            _stats);
    }

    #endregion

    #region Buffer Management

    // フィルタパスなどでGPU上に受け渡すための各ComputeBuffer領域を確保する
    public void InitializeBuffers(int rsLength, Matrix4x4 localToWorld)
    {
        _rsLength = rsLength;
        _localToWorld = localToWorld;

        // 既存のバッファがある場合は一度解放しておく
        ReleaseBuffers();

        // ComputeBufferType.Append を指定し、要不要を判定して有効な点だけをAppend()で追加する
        _filteredVerticesBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3, ComputeBufferType.Append);

        // PCA処理用等のために、条件を満たした少数の点群だけをサンプリングするバッファ
        _samplingBuffer = new ComputeBuffer(RsFilterPassExecutor.MAX_SAMPLE_TRANSFER, sizeof(float) * 3, ComputeBufferType.Append);

        _distanceDiscardBuffer = new ComputeBuffer(rsLength, sizeof(float) * 3, ComputeBufferType.Append);

        // プロシージャル描画を行う際、何点の頂点を描画するのかGPU内部で参照するためのバッファ
        _argsBuffer = new ComputeBuffer(1, sizeof(int) * 4, ComputeBufferType.IndirectArguments);
        _argsBuffer.SetData(_argsData);

        if (_asyncReadback == null)
        {
            _asyncReadback = new RsPointCloudAsyncReadback(_stats);
            _filterPassExecutor = new RsFilterPassExecutor(
                _dispatcher,
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

    // 生の頂点を受け取り、フィルタリングを行うと共に(必要なら)独自のPCA推定処理を実行する
    public (int finalCount, Vector3 point, Vector3 dir, int discardedCount, int sampledCount, float discardPercentage)
        FilterAndEstimateLine(string sourceName, ComputeBuffer rawVerticesBuffer, Vector3 prevPoint, Vector3 prevDir, int vertexCount, float maxPlaneDistance)
    {
        // 実際のComputeShader(ディスパッチャ)の呼び出しと各種バッファへの受け渡し
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
            vertexCount,
            maxPlaneDistance);

        Vector3 point = prevPoint, dir = prevDir;

        // 過去の非同期サンプリング結果が取得完了していればそのデータで主成分分析(PCA)を実行
        if (_asyncReadback.HasCachedSamples && _asyncReadback.CachedSamplesCount > 0)
        {
            _filterPassExecutor.UpdateSamplingResultFromCache();
            (point, dir) = _filterPassExecutor.EstimateLineFromCache();
        }

        // 最終的な頂点数や、サンプリング・破棄された数、計算されたライン結果などをタプルで返却
        return (counts.finalCount, point, dir, counts.discardedCount, counts.sampledCount, counts.discardPercentage);
    }

    public (int finalCount, Vector3 point, Vector3 dir, int discardedCount, int sampledCount, float discardPercentage)
        FilterAndEstimateLine(string sourceName, ComputeBuffer rawVerticesBuffer, Vector3 prevPoint, Vector3 prevDir, float maxPlaneDistance)
        => FilterAndEstimateLine(sourceName, rawVerticesBuffer, prevPoint, prevDir, _rsLength, maxPlaneDistance);

    public (int finalCount, Vector3 point, Vector3 dir, int discardedCount, int sampledCount, float discardPercentage)
        FilterAndEstimateLine(ComputeBuffer rawVerticesBuffer, Vector3 prevPoint, Vector3 prevDir, int vertexCount, float maxPlaneDistance)
        => FilterAndEstimateLine(string.Empty, rawVerticesBuffer, prevPoint, prevDir, vertexCount, maxPlaneDistance);

    public (int finalCount, Vector3 point, Vector3 dir, int discardedCount, int sampledCount, float discardPercentage)
        FilterAndEstimateLine(ComputeBuffer rawVerticesBuffer, Vector3 prevPoint, Vector3 prevDir, float maxPlaneDistance)
        => FilterAndEstimateLine(string.Empty, rawVerticesBuffer, prevPoint, prevDir, _rsLength, maxPlaneDistance);

    // FilterAndEstimateLine() と類似するが、PCAによる推論計算を省略し間引くだけの処理を行う
    public (int finalCount, int discardedCount, int sampledCount, float discardPercentage)
        FilterOnly(string sourceName, ComputeBuffer rawVerticesBuffer, Vector3 prevPoint, Vector3 prevDir, int vertexCount, float maxPlaneDistance)
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
            vertexCount,
            maxPlaneDistance);

        // キャッシュにデータが届いていれば保持しておく(サンプリング結果としての更新)
        if (_asyncReadback.HasCachedSamples && _asyncReadback.CachedSamplesCount > 0)
        {
            _filterPassExecutor.UpdateSamplingResultFromCache();
        }

        return (counts.finalCount, counts.discardedCount, counts.sampledCount, counts.discardPercentage);
    }

    public (int finalCount, int discardedCount, int sampledCount, float discardPercentage)
        FilterOnly(ComputeBuffer rawVerticesBuffer, Vector3 prevPoint, Vector3 prevDir, int vertexCount, float maxPlaneDistance)
        => FilterOnly(string.Empty, rawVerticesBuffer, prevPoint, prevDir, vertexCount, maxPlaneDistance);

    #endregion

    #region Transform

    // フィルタなどは挟まず、純粋に行列による座標変換だけを点群全体に適用し、描画用引数バッファを返す
    public ComputeBuffer TransformIndirect(ComputeBuffer rawVerticesBuffer, int vertexCount)
    {
        // 追記型バッファのカウンターを0にリセット
        _filteredVerticesBuffer.SetCounterValue(0);

        _transformCmd.Clear();
        _transformCmd.BeginSample(s_sampleTransformDispatch);
        _dispatcher.DispatchTransform(
            _transformCmd,
            rawVerticesBuffer, _filteredVerticesBuffer, _localToWorld,
            _globalThreshold1, _globalThreshold2, vertexCount);
        _transformCmd.EndSample(s_sampleTransformDispatch);

        // TransformCmdの実行指示
        Graphics.ExecuteCommandBuffer(_transformCmd);

        // バッファ内で実際に出力された点の数を _argsBuffer へコピーする (DrawIndirect用)
        ComputeBuffer.CopyCount(_filteredVerticesBuffer, _argsBuffer, 0);

        // 非同期で頂点数を確定させる読み出し要求をスケジュールする
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
