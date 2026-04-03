using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Intel.RealSense;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

/// <summary>
/// ComputeShaderを利用してDepthFrameとColorFrameを合成し、
/// 色ベースのカリング（抽出）と座標変換を行いながら点群のComputeBufferを生成・更新する専用プロセッサ。
/// </summary>
public class RsIntegratedPointCloudProcessor : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RsIntrinsics { public int width, height; public float ppx, ppy, fx, fy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RsExtrinsics
    {
        public float r0, r1, r2, r3, r4, r5, r6, r7, r8;
        public float t0, t1, t2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CullingParams
    {
        public int width, height, mode;
        public float minDist, maxDist, minHue, maxHue, minSat, maxSat, minVal, maxVal;
        public int minY, maxY, minCb, maxCb, minCr, maxCr;
        public Matrix4x4 transformMatrix;
        public int applyTransform;
        public int coordinateConversion;
    }

    private ComputeShader _shader;
    private int _kernelIndex;

    private ComputeBuffer _depthIntrinsicsBuffer;
    private ComputeBuffer _colorIntrinsicsBuffer;
    private ComputeBuffer _extrinsicsBuffer;
    private ComputeBuffer _paramsBuffer;
    private ComputeBuffer _inputDepthBuffer;
    private ComputeBuffer _pointCloudBuffer;
    private ComputeBuffer _pointCloudCountBuffer;

    private Texture2D _colorTexture;

    private bool _countReadbackPending = false;
    private int _pendingPointCount = 0;

    private int _latestPointCount = 0;
    private bool _hasNewPointCloud = false;

    private RsIntrinsics _dIntrin;
    private RsIntrinsics _cIntrin;
    private RsExtrinsics _extrin;
    private bool _initialized = false;

    private readonly CullingParams[] _cullingParamsCache = new CullingParams[1];

    private byte[] _colorDataCache;
    private byte[] _depthDataCache;
    private CullingParams _pendingParams;
    private volatile bool _hasPendingFrame = false;
    private readonly object _frameLock = new object();

    public ComputeBuffer PointCloudBuffer => _pointCloudBuffer;
    public int LastPointCount => _latestPointCount;
    public bool HasNewPointCloud => _hasNewPointCloud;

    public RsIntegratedPointCloudProcessor(ComputeShader shader)
    {
        _shader = shader;
        _kernelIndex = _shader.FindKernel("CSMain");
    }

    public void Initialize(RsDepthToColorCalibration calibration)
    {
        var dp = calibration.DepthProfile;
        var cp = calibration.ColorProfile;
        var di = dp.GetIntrinsics();
        var ci = cp.GetIntrinsics();
        var ex = dp.GetExtrinsicsTo(cp);

        _dIntrin = new RsIntrinsics { width = dp.Width, height = dp.Height, ppx = di.ppx, ppy = di.ppy, fx = di.fx, fy = di.fy };
        _cIntrin = new RsIntrinsics { width = cp.Width, height = cp.Height, ppx = ci.ppx, ppy = ci.ppy, fx = ci.fx, fy = ci.fy };
        _extrin = new RsExtrinsics
        {
            r0 = ex.rotation[0],
            r1 = ex.rotation[1],
            r2 = ex.rotation[2],
            r3 = ex.rotation[3],
            r4 = ex.rotation[4],
            r5 = ex.rotation[5],
            r6 = ex.rotation[6],
            r7 = ex.rotation[7],
            r8 = ex.rotation[8],
            t0 = ex.translation[0],
            t1 = ex.translation[1],
            t2 = ex.translation[2]
        };

        RsUnityMainThreadDispatcher.Instance.EnqueueAndWait(AllocateResources);
        _initialized = true;
    }

    private void AllocateResources()
    {
        ReleaseBuffers();

        _depthIntrinsicsBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(RsIntrinsics)));
        _colorIntrinsicsBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(RsIntrinsics)));
        _extrinsicsBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(RsExtrinsics)));
        _paramsBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(CullingParams)));

        _depthIntrinsicsBuffer.SetData(new RsIntrinsics[] { _dIntrin });
        _colorIntrinsicsBuffer.SetData(new RsIntrinsics[] { _cIntrin });
        _extrinsicsBuffer.SetData(new RsExtrinsics[] { _extrin });

        _colorTexture = new Texture2D(_cIntrin.width, _cIntrin.height, TextureFormat.RGB24, false);

        int depthPixelCount = _dIntrin.width * _dIntrin.height;
        int totalBytes = depthPixelCount * sizeof(ushort);
        _inputDepthBuffer = new ComputeBuffer(totalBytes / 4, 4, ComputeBufferType.Raw);

        _pointCloudBuffer = new ComputeBuffer(depthPixelCount, sizeof(float) * 3, ComputeBufferType.Append);
        _pointCloudCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

        _colorDataCache = new byte[_cIntrin.width * _cIntrin.height * 3]; // RGB24
        _depthDataCache = new byte[totalBytes];

        _shader.SetBuffer(_kernelIndex, "_DepthIntrinsics", _depthIntrinsicsBuffer);
        _shader.SetBuffer(_kernelIndex, "_ColorIntrinsics", _colorIntrinsicsBuffer);
        _shader.SetBuffer(_kernelIndex, "_DepthToColorExtrinsics", _extrinsicsBuffer);
        _shader.SetBuffer(_kernelIndex, "_InputDepthBuffer", _inputDepthBuffer);
        _shader.SetBuffer(_kernelIndex, "_OutputPointCloud", _pointCloudBuffer);
    }

    /// <summary>
    /// 各フレームの画像データを受け取り、GPUへ転送するためのキャッシュにコピーします。
    /// 実際のComputeShader呼び出しはUnityのメインスレッドで実行されるようキューに積まれます。
    /// </summary>
    public Vector3[] Process(VideoFrame colorFrame, DepthFrame depthFrame, RsIntegratedPointCloud parent)
    {
        if (!_initialized) return null;

        var dispatcher = RsUnityMainThreadDispatcher.Instance;
        if (dispatcher == null)
        {
            return null;
        }

        int colorBytes = colorFrame.Stride * colorFrame.Height;
        int depthBytes = depthFrame.Stride * depthFrame.Height;

        // 非同期スレッドから呼ばれる可能性を考慮し、キャッシュバッファへのコピースレッドを保護する
        lock (_frameLock)
        {
            // RealSenseのC++側(アンマネージド)メモリからC#のマネージド配列へ高速にコピー
            Marshal.Copy(colorFrame.Data, _colorDataCache, 0, System.Math.Min(colorBytes, _colorDataCache.Length));
            Marshal.Copy(depthFrame.Data, _depthDataCache, 0, System.Math.Min(depthBytes, _depthDataCache.Length));

            // 現在の閾値パラメータや変換行列などの状態を退避させる
            _pendingParams = new CullingParams
            {
                width = _dIntrin.width,
                height = _dIntrin.height,
                mode = (int)parent._mode,
                minDist = parent._minDistance,
                maxDist = parent._maxDistance,
                minHue = parent._minHue,
                maxHue = parent._maxHue,
                minSat = parent._minSaturation,
                maxSat = parent._maxSaturation,
                minVal = parent._minValue,
                maxVal = parent._maxValue,
                minY = parent._minY,
                maxY = parent._maxY,
                minCb = parent._minCb,
                maxCb = parent._maxCb,
                minCr = parent._minCr,
                maxCr = parent._maxCr,
                transformMatrix = parent._transformMatrix,
                applyTransform = parent._applyTransform ? 1 : 0,
                coordinateConversion = (int)parent._coordinateConversion
            };
            _hasPendingFrame = true;
        }

        // Textureの更新やComputeShaderのDispatch等、Unityの一部のAPIはメインスレッドでのみ実行可能なため委譲する
        dispatcher.Enqueue(ProcessPendingFrame);

        return null;
    }

    /// <summary>
    /// メインスレッド上で実行され、キャッシュされたカラー・深度データをGPUへ送り、
    /// 点群生成およびフィルタリングを行うComputeShaderカーネルをディスパッチ(実行)します。
    /// </summary>
    private void ProcessPendingFrame()
    {
        if (!_hasPendingFrame) return;

        lock (_frameLock)
        {
            _hasPendingFrame = false;

            // テクスチャにカラーデータをロードしGPU側に反映させる
            _colorTexture.LoadRawTextureData(_colorDataCache);
            _colorTexture.Apply();
            _shader.SetTexture(_kernelIndex, "_InputColorTexture", _colorTexture);

            // 深度データをバッファにセット
            _inputDepthBuffer.SetData(_depthDataCache);

            // カリング用のパラメータを更新
            _cullingParamsCache[0] = _pendingParams;
            _paramsBuffer.SetData(_cullingParamsCache);
            _shader.SetBuffer(_kernelIndex, "_Params", _paramsBuffer);

            // 出力用Appendバッファ内の要素数(カウンタ)を0にリセットして初期化する
            _pointCloudBuffer.SetCounterValue(0);

            // 深度画像の総ピクセル数に応じたスレッドグループ数を計算してComputeShaderを実行
            int threadGroups = ((_dIntrin.width * _dIntrin.height) + 63) / 64;
            _shader.Dispatch(_kernelIndex, threadGroups, 1, 1);

            // フィルタリングされた点群の数を非同期にCPUへ読み戻すリクエストを発行
            if (!_countReadbackPending) RequestAsyncReadback();
        }
    }

    private void RequestAsyncReadback()
    {
        _countReadbackPending = true;
        ComputeBuffer.CopyCount(_pointCloudBuffer, _pointCloudCountBuffer, 0);
        AsyncGPUReadback.Request(_pointCloudCountBuffer, OnCountReadbackComplete);
    }

    private void OnCountReadbackComplete(AsyncGPUReadbackRequest request)
    {
        _countReadbackPending = false;
        if (request.hasError) return;

        var countData = request.GetData<int>();
        if (countData.Length > 0)
        {
            _pendingPointCount = countData[0];
            _latestPointCount = _pendingPointCount;
            _hasNewPointCloud = true;
        }
    }

    private void UpdateParams(RsIntegratedPointCloud p)
    {
        _cullingParamsCache[0] = new CullingParams
        {
            width = _dIntrin.width,
            height = _dIntrin.height,
            mode = (int)p._mode,
            minDist = p._minDistance,
            maxDist = p._maxDistance,
            minHue = p._minHue,
            maxHue = p._maxHue,
            minSat = p._minSaturation,
            maxSat = p._maxSaturation,
            minVal = p._minValue,
            maxVal = p._maxValue,
            minY = p._minY,
            maxY = p._maxY,
            minCb = p._minCb,
            maxCb = p._maxCb,
            minCr = p._minCr,
            maxCr = p._maxCr,
            transformMatrix = p._transformMatrix,
            applyTransform = p._applyTransform ? 1 : 0,
            coordinateConversion = (int)p._coordinateConversion
        };
        _paramsBuffer.SetData(_cullingParamsCache);
        _shader.SetBuffer(_kernelIndex, "_Params", _paramsBuffer);
    }

    public void UpdateTransformMatrix(Matrix4x4 matrix)
    {
        // This will be picked up in the next UpdateParams call during Process
        // We can also force an update here if needed, but Process is called every frame anyway
    }

    private void ReleaseBuffers()
    {
        _countReadbackPending = false;
        if (_depthIntrinsicsBuffer != null) { _depthIntrinsicsBuffer.Release(); _depthIntrinsicsBuffer = null; }
        if (_colorIntrinsicsBuffer != null) { _colorIntrinsicsBuffer.Release(); _colorIntrinsicsBuffer = null; }
        if (_extrinsicsBuffer != null) { _extrinsicsBuffer.Release(); _extrinsicsBuffer = null; }
        if (_paramsBuffer != null) { _paramsBuffer.Release(); _paramsBuffer = null; }
        if (_inputDepthBuffer != null) { _inputDepthBuffer.Release(); _inputDepthBuffer = null; }
        if (_pointCloudBuffer != null) { _pointCloudBuffer.Release(); _pointCloudBuffer = null; }
        if (_pointCloudCountBuffer != null) { _pointCloudCountBuffer.Release(); _pointCloudCountBuffer = null; }
        if (_colorTexture != null) { UnityEngine.Object.Destroy(_colorTexture); _colorTexture = null; }
    }

    public void Dispose() => ReleaseBuffers();
}