using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Intel.RealSense;

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
    }

    private ComputeShader _shader;
    private int _kernelIndex;

    // Buffers
    private ComputeBuffer _depthIntrinsicsBuffer;
    private ComputeBuffer _colorIntrinsicsBuffer;
    private ComputeBuffer _extrinsicsBuffer;
    private ComputeBuffer _paramsBuffer;
    private ComputeBuffer _inputDepthBuffer;
    private ComputeBuffer _pointCloudBuffer;
    private ComputeBuffer _pointCloudCountBuffer;

    private Texture2D _colorTexture;
    private int[] _depthDataCache;

    private Vector3[] _latestPointCloud;
    private int _latestPointCount = 0;
    private bool _hasNewPointCloud = false;

    // Async Readback State
    private bool _countReadbackPending = false;
    private bool _pointCloudReadbackPending = false;
    private int _pendingPointCount = 0;

    private RsIntrinsics _dIntrin;
    private RsIntrinsics _cIntrin;
    private RsExtrinsics _extrin;
    private bool _initialized = false;

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
        _depthDataCache = new int[depthPixelCount];
        _inputDepthBuffer = new ComputeBuffer(depthPixelCount, sizeof(int));

        _pointCloudBuffer = new ComputeBuffer(depthPixelCount, sizeof(float) * 3, ComputeBufferType.Append);
        _pointCloudCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);

        _latestPointCloud = new Vector3[depthPixelCount];

        _shader.SetBuffer(_kernelIndex, "_DepthIntrinsics", _depthIntrinsicsBuffer);
        _shader.SetBuffer(_kernelIndex, "_ColorIntrinsics", _colorIntrinsicsBuffer);
        _shader.SetBuffer(_kernelIndex, "_DepthToColorExtrinsics", _extrinsicsBuffer);
        _shader.SetBuffer(_kernelIndex, "_InputDepthBuffer", _inputDepthBuffer);
        _shader.SetBuffer(_kernelIndex, "_OutputPointCloud", _pointCloudBuffer);
    }

    public Vector3[] Process(VideoFrame colorFrame, DepthFrame depthFrame, RsIntegratedPointCloud parent)
    {
        if (!_initialized) return null;

        unsafe
        {
            ushort* ptr = (ushort*)depthFrame.Data;
            int count = _depthDataCache.Length;
            for (int i = 0; i < count; i++) _depthDataCache[i] = ptr[i];
        }

        Vector3[] resultPointCloud = null;

        RsUnityMainThreadDispatcher.Instance.EnqueueAndWait(() =>
        {
            _colorTexture.LoadRawTextureData(colorFrame.Data, colorFrame.Stride * colorFrame.Height);
            _colorTexture.Apply();
            _shader.SetTexture(_kernelIndex, "_InputColorTexture", _colorTexture);

            _inputDepthBuffer.SetData(_depthDataCache);
            UpdateParams(parent);

            _pointCloudBuffer.SetCounterValue(0);
            int threadGroups = Mathf.CeilToInt((_dIntrin.width * _dIntrin.height) / 64.0f);
            _shader.Dispatch(_kernelIndex, threadGroups, 1, 1);

            if (_hasNewPointCloud && _latestPointCount > 0)
            {
                resultPointCloud = new Vector3[_latestPointCount];
                Array.Copy(_latestPointCloud, resultPointCloud, _latestPointCount);
                _hasNewPointCloud = false;
            }

            if (!_countReadbackPending) RequestAsyncReadback();
        });

        return resultPointCloud;
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
            if (_pendingPointCount > 0 && !_pointCloudReadbackPending)
            {
                _pointCloudReadbackPending = true;
                int bytesToRead = _pendingPointCount * sizeof(float) * 3;
                AsyncGPUReadback.Request(_pointCloudBuffer, bytesToRead, 0, OnPointCloudReadbackComplete);
            }
            else if (_pendingPointCount == 0)
            {
                _latestPointCount = 0;
                _hasNewPointCloud = true;
            }
        }
    }

    private void OnPointCloudReadbackComplete(AsyncGPUReadbackRequest request)
    {
        _pointCloudReadbackPending = false;
        if (request.hasError) return;

        var pointData = request.GetData<Vector3>();
        int count = Mathf.Min(pointData.Length, _pendingPointCount);

        if (count > 0 && _latestPointCloud != null)
        {
            for (int i = 0; i < count; i++) _latestPointCloud[i] = pointData[i];
            _latestPointCount = count;
            _hasNewPointCloud = true;
        }
    }

    private void UpdateParams(RsIntegratedPointCloud p)
    {
        CullingParams cParams = new CullingParams
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
            maxCr = p._maxCr
        };
        _paramsBuffer.SetData(new CullingParams[] { cParams });
        _shader.SetBuffer(_kernelIndex, "_Params", _paramsBuffer);
    }

    private void ReleaseBuffers()
    {
        _countReadbackPending = false;
        _pointCloudReadbackPending = false;
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