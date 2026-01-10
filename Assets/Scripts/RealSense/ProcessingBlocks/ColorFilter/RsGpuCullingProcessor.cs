using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Intel.RealSense;
using System.Diagnostics;

public class RsGpuCullingProcessor : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RsIntrinsics
    {
        public int width;
        public int height;
        public float ppx;
        public float ppy;
        public float fx;
        public float fy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RsExtrinsics
    {
        public float r0, r1, r2;
        public float r3, r4, r5;
        public float r6, r7, r8;
        public float t0, t1, t2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CullingParams
    {
        public int width;
        public int height;
        public int mode;
        public float minHue, maxHue;
        public float minSat, maxSat;
        public float minVal, maxVal;
        public int minY, maxY;
        public int minCb, maxCb;
        public int minCr, maxCr;
    }

    private ComputeShader _shader;
    private int _kernelIndex;

    private ComputeBuffer _depthIntrinsicsBuffer;
    private ComputeBuffer _colorIntrinsicsBuffer;
    private ComputeBuffer _extrinsicsBuffer;
    private ComputeBuffer _paramsBuffer;
    private ComputeBuffer _depthDataBuffer;

    private Texture2D _colorTexture;

    private int[] _depthDataCache;
    private RsIntrinsics _dIntrin;
    private RsIntrinsics _cIntrin;
    private RsExtrinsics _extrin;

    private bool _initialized = false;

    private Stopwatch _stopwatch = new Stopwatch();
    private long _lastProcessTimeMs = 0;

    public RsGpuCullingProcessor(ComputeShader shader)
    {
        if (shader == null)
        {
            UnityEngine.Debug.LogError("[RsGpuCullingProcessor] Compute Shader is null.");
            return;
        }
        _shader = shader;
        _kernelIndex = _shader.FindKernel("CSMain");
    }

    public void Initialize(RsDepthToColorCalibration calibration)
    {
        if (calibration == null) return;

        var dp = calibration.DepthProfile;
        var cp = calibration.ColorProfile;
        var di = dp.GetIntrinsics();
        var ci = cp.GetIntrinsics();
        var ex = dp.GetExtrinsicsTo(cp);

        _dIntrin = new RsIntrinsics
        {
            width = dp.Width,
            height = dp.Height,
            ppx = di.ppx,
            ppy = di.ppy,
            fx = di.fx,
            fy = di.fy
        };

        _cIntrin = new RsIntrinsics
        {
            width = cp.Width,
            height = cp.Height,
            ppx = ci.ppx,
            ppy = ci.ppy,
            fx = ci.fx,
            fy = ci.fy
        };

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

        if (RsUnityMainThreadDispatcher.Instance != null)
        {
            RsUnityMainThreadDispatcher.Instance.EnqueueAndWait(() =>
            {
                AllocateResources();
            });
        }
        else
        {
            AllocateResources();
        }

        _initialized = true;
        UnityEngine.Debug.Log("[RsGpuCullingProcessor] Initialized GPU resources.");
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
        _depthDataBuffer = new ComputeBuffer(depthPixelCount, sizeof(int));

        _shader.SetBuffer(_kernelIndex, "_DepthIntrinsics", _depthIntrinsicsBuffer);
        _shader.SetBuffer(_kernelIndex, "_ColorIntrinsics", _colorIntrinsicsBuffer);
        _shader.SetBuffer(_kernelIndex, "_DepthToColorExtrinsics", _extrinsicsBuffer);
        _shader.SetBuffer(_kernelIndex, "_OutputDepthBuffer", _depthDataBuffer);
    }

    public void Process(VideoFrame colorFrame, DepthFrame depthFrame, RsColorBasedDepthCulling parent)
    {
        if (!_initialized) return;

        if (RsUnityMainThreadDispatcher.Instance == null)
        {
            UnityEngine.Debug.LogError("RsUnityMainThreadDispatcher instance not found in scene!");
            return;
        }

        _stopwatch.Restart();

        CopyDepthDataToIntArray(depthFrame, _depthDataCache);

        RsUnityMainThreadDispatcher.Instance.EnqueueAndWait(() =>
        {
            _colorTexture.LoadRawTextureData(colorFrame.Data, colorFrame.Stride * colorFrame.Height);
            _colorTexture.Apply();
            _shader.SetTexture(_kernelIndex, "_InputColorTexture", _colorTexture);

            _depthDataBuffer.SetData(_depthDataCache);

            UpdateParams(parent);

            int threadGroups = Mathf.CeilToInt((_dIntrin.width * _dIntrin.height) / 64.0f);
            _shader.Dispatch(_kernelIndex, threadGroups, 1, 1);

            _depthDataBuffer.GetData(_depthDataCache);
        });

        CopyIntArrayToDepthFrame(_depthDataCache, depthFrame);

        _stopwatch.Stop();
        _lastProcessTimeMs = _stopwatch.ElapsedMilliseconds;

        UnityEngine.Debug.Log($"[RsGpuCullingProcessor] Process Time: {_lastProcessTimeMs} ms.");
    }

    private unsafe void CopyDepthDataToIntArray(DepthFrame frame, int[] dest)
    {
        ushort* ptr = (ushort*)frame.Data;
        int count = dest.Length;
        for (int i = 0; i < count; i++)
        {
            dest[i] = ptr[i];
        }
    }

    private unsafe void CopyIntArrayToDepthFrame(int[] source, DepthFrame frame)
    {
        ushort* ptr = (ushort*)frame.Data;
        int count = source.Length;
        for (int i = 0; i < count; i++)
        {
            ptr[i] = (ushort)source[i];
        }
    }

    private void UpdateParams(RsColorBasedDepthCulling p)
    {
        CullingParams cParams = new CullingParams
        {
            width = _dIntrin.width,
            height = _dIntrin.height,
            mode = (int)p._mode,
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
        if (_depthIntrinsicsBuffer != null) { _depthIntrinsicsBuffer.Release(); _depthIntrinsicsBuffer = null; }
        if (_colorIntrinsicsBuffer != null) { _colorIntrinsicsBuffer.Release(); _colorIntrinsicsBuffer = null; }
        if (_extrinsicsBuffer != null) { _extrinsicsBuffer.Release(); _extrinsicsBuffer = null; }
        if (_paramsBuffer != null) { _paramsBuffer.Release(); _paramsBuffer = null; }
        if (_depthDataBuffer != null) { _depthDataBuffer.Release(); _depthDataBuffer = null; }
        if (_colorTexture != null) { UnityEngine.Object.Destroy(_colorTexture); _colorTexture = null; }
    }

    public void Dispose()
    {
        ReleaseBuffers();
    }
}