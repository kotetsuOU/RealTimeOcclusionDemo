using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering; // For CommandBuffer, RenderPipelineManager
using Intel.RealSense;

[ProcessingBlockData(typeof(RsHandMeshBlock))]
public class RsHandMeshBlock : RsProcessingBlock
{
    public enum ConversionMode { HSV = 0, YCbCr = 1 }

    [Header("Compute Shader")]
    public ComputeShader _computeShader;
    // We expect the user to assign "RsHandMeshGen" here or we load it.
    private const string COMPUTE_SHADER_NAME = "ComputeShaders/RsHandMeshGen";

    [Header("Output")]
    public bool _enableCpuOutput = true;

    [Header("Depth Map Debug")]
    public bool _enableDepthMapOutput = false;
    public bool _enableRawDepthOutput = false;
    public int _depthMapCaptureLimit = 5;
    private int _depthMapCapturedCount = 0;
    public string _depthMapSaveFolder = "Assets/HandTrackingData/DepthMaps";
    private int _depthMapFrameCount = 0;

    [Serializable]
    public struct DepthMetaData
    {
        public int width;
        public int height;
        public float fx;
        public float fy;
        public float ppx;
        public float ppy;
        public float depthScale;
        public Matrix4x4 transformMatrix;
    }

    [Header("Mesh Settings")]
    [Tooltip("Edges longer than this (meters) are discarded.")]
    [Range(0f, 0.1f)] public float _edgeThreshold = 0.02f; 
    public Matrix4x4 _transformMatrix = Matrix4x4.identity;

    [Header("Control")]
    public ConversionMode _mode = ConversionMode.HSV;

    [Header("Thresholds")]
    [Range(0f, 10f)] public float _minDistance = 0.1f;
    [Range(0f, 10f)] public float _maxDistance = 1.0f; // Hand is usually close

    [Header("HSV Thresholds")]
    [Range(0f, 1f)] public float _minHue = 0.0f;
    [Range(0f, 1f)] public float _maxHue = 1.0f;
    [Range(0f, 1f)] public float _minSaturation = 0.0f;
    [Range(0f, 1f)] public float _maxSaturation = 1.0f;
    [Range(0f, 1f)] public float _minValue = 0.0f;
    [Range(0f, 1f)] public float _maxValue = 1.0f;

    [Header("YCbCr Thresholds")]
    [Range(0, 255)] public int _minY = 0;
    [Range(0, 255)] public int _maxY = 255;
    [Range(0, 255)] public int _minCb = 0;
    [Range(0, 255)] public int _maxCb = 255;
    [Range(0, 255)] public int _minCr = 0;
    [Range(0, 255)] public int _maxCr = 255;

    // --- Internal Data ---
    private int _kernelVertices;
    private int _kernelIndices;
    
    private ComputeBuffer _vertexBuffer;
    private ComputeBuffer _indexBuffer;
    private ComputeBuffer _argsBuffer;
    private ComputeBuffer _depthBuffer; // Packed 16-bit depth
    private Texture2D _colorTexture;

    private static readonly uint[] _indirectArgsInit = new uint[] { 0, 1, 0, 0 };
    private readonly uint[] _indirectArgsStaging = new uint[4];

    private byte[] _colorDataCache;
    private byte[] _depthDataCache;

    private int _width = 0;
    private int _height = 0;
    private bool _gpuReady = false;
    private bool _needsReinit = true;
    private bool _hasNewFrame = false;
    private object _lock = new object();

    private Vector4 _intrinsics; // fx, fy, ppx, ppy

    [Header("Debug")]
    public bool _logOnceWhenDrawing = false;
    public bool _logIndirectArgsEverySecond = false;
    public bool _logLifecycle = false;
    private float _nextArgsLogTime;
    private float _nextLifecycleLogTime;
    private bool _loggedDrawOnce;
    private readonly uint[] _argsReadback = new uint[4];

    public Vector3[] LatestPositions { get; private set; }
    public Color[] LatestColors { get; private set; }
    public int[] LatestIndices { get; private set; }
    public int LatestIndexCount { get; private set; }

    private Vertex[] _vertexReadback;
    private int[] _indexReadback;

    private bool _processCalled;
    private bool _updateSawFrame;
    private bool _loggedFirstComposite;
    private bool _loggedFirstFrameCopied;
    private bool _loggedBuffersInitialized;
    private bool _loggedFirstDispatch;
    private bool _loggedFirstArgs;
    private bool _loggedFirstArgsForced;

    private int _mainThreadInstanceId;

    [Header("Runtime Helper")]
    public bool _useRuntimeDriver = true;
    private DriverBehaviour _driver;
    private static bool _driverLogged;

    private class DriverBehaviour : MonoBehaviour
    {
        public RsHandMeshBlock Owner;
        private void Update() => Owner?.Tick();
    }

    struct Vertex
    {
        public Vector3 pos;
        public Vector3 col;
        public Vector2 uv;
    }

    private void OnEnable()
    {
        _mainThreadInstanceId = GetInstanceID();
        if (_logLifecycle) Debug.Log($"[RsHandMeshBlock] OnEnable ({name})");

        if (_useRuntimeDriver && Application.isPlaying)
        {
            if (_driver == null)
            {
                var go = new GameObject($"RsHandMeshBlockDriver_{GetInstanceID()}");
                go.hideFlags = HideFlags.HideAndDontSave;
                _driver = go.AddComponent<DriverBehaviour>();
                _driver.Owner = this;
                if (!_driverLogged)
                {
                    _driverLogged = true;
                    Debug.Log("[RsHandMeshBlock] Runtime driver created.");
                }
            }
        }

        if (_computeShader == null)
            _computeShader = Resources.Load<ComputeShader>(COMPUTE_SHADER_NAME);
        
        if (_computeShader != null)
        {
            _kernelVertices = _computeShader.FindKernel("CSVertices");
            _kernelIndices = _computeShader.FindKernel("CSIndices");
        }

        // Output is consumed by a scene MonoBehaviour that builds a Unity Mesh.
    }

    private void OnDisable()
    {
        if (_logLifecycle) Debug.Log($"[RsHandMeshBlock] OnDisable ({name})");
        ReleaseBuffers();

        if (_driver != null)
        {
            _driver.Owner = null;
            if (Application.isPlaying)
                Destroy(_driver.gameObject);
            else
                DestroyImmediate(_driver.gameObject);
            _driver = null;
        }
    }

    private void ReleaseBuffers()
    {
        _vertexBuffer?.Release(); _vertexBuffer = null;
        _indexBuffer?.Release(); _indexBuffer = null;
        _depthBuffer?.Release(); _depthBuffer = null;
        _argsBuffer?.Release(); _argsBuffer = null;

        if (_colorTexture != null)
        {
            if (Application.isPlaying) Destroy(_colorTexture);
            else DestroyImmediate(_colorTexture);
            _colorTexture = null;
        }

        _gpuReady = false;
        _needsReinit = true;
    }

    // Process Frame from RealSense Thread
    public override Frame Process(Frame frame, FrameSource frameSource)
    {
        _processCalled = true;

        if (_logLifecycle && !_loggedFirstComposite)
        {
            _loggedFirstComposite = true;
            Debug.Log($"[RsHandMeshBlock:{_mainThreadInstanceId}] Process called. IsComposite={frame.IsComposite}");
        }

        if (frame.IsComposite)
        {
            using (var fs = FrameSet.FromFrame(frame))
            using (var colorFrame = fs.ColorFrame)
            using (var depthFrame = fs.DepthFrame)
            {
                if (colorFrame != null && depthFrame != null)
                {
                    lock (_lock)
                    {
                        // Reinitialize only on resolution change. GPU readiness is handled on main thread.
                        if (_width != depthFrame.Width || _height != depthFrame.Height)
                        {
                            _width = depthFrame.Width;
                            _height = depthFrame.Height;
                            _colorDataCache = new byte[_width * _height * 3]; // RGB24
                            _depthDataCache = new byte[_width * _height * 2]; // 16-bit depth
                            
                            // Initialize intrinsics
                            var intr = depthFrame.Profile.As<VideoStreamProfile>().GetIntrinsics();
                            _intrinsics = new Vector4(intr.fx, intr.fy, intr.ppx, intr.ppy);

                            _needsReinit = true;
                        }

                        if (_colorDataCache.Length == colorFrame.Stride * colorFrame.Height &&
                            _depthDataCache.Length == depthFrame.Stride * depthFrame.Height)
                        {
                            colorFrame.CopyTo(_colorDataCache);
                            depthFrame.CopyTo(_depthDataCache);
                            _hasNewFrame = true;

                            bool capBmp = _enableDepthMapOutput;
                            bool capRaw = _enableRawDepthOutput;
                            if (capBmp || capRaw)
                            {
                                if (capBmp) SaveDepthMapBmp(depthFrame);
                                if (capRaw) SaveRawDepthData(depthFrame);

                                _depthMapFrameCount++;
                                _depthMapCapturedCount++;
                                if (_depthMapCapturedCount >= _depthMapCaptureLimit)
                                {
                                    _enableDepthMapOutput = false;
                                    _enableRawDepthOutput = false;
                                    _depthMapCapturedCount = 0;
                                }
                            }
                            else
                            {
                                _depthMapCapturedCount = 0;
                            }

                            if (_logLifecycle && !_loggedFirstFrameCopied)
                            {
                                _loggedFirstFrameCopied = true;
                                Debug.Log($"[RsHandMeshBlock:{_mainThreadInstanceId}] First frame copied. size=({_width}x{_height}) colorStride={colorFrame.Stride} depthStride={depthFrame.Stride}");
                            }
                        }
                    }
                }
            }
        }
        return frame;
    }

    private void Update()
    {
        // If this instance is used as a processing block clone (not an active scene component),
        // Update() may not be invoked reliably. In that case we use the runtime driver (Tick).
        if (_useRuntimeDriver) return;
        Tick();
    }

    private void Tick()
    {
        if (_computeShader == null) return;

        if (_logLifecycle && !_processCalled)
        {
            if (Time.unscaledTime >= _nextLifecycleLogTime)
            {
                _nextLifecycleLogTime = Time.unscaledTime + 1f;
                Debug.Log("[RsHandMeshBlock] Tick running but Process() not called yet.");
            }
        }

        lock (_lock)
        {
            if (!_hasNewFrame) return;

            // Consume the frame flag early to avoid re-entrancy if Process() is running fast.
            _hasNewFrame = false;

            _updateSawFrame = true;

            if ((_needsReinit || !_gpuReady) && _width > 0 && _height > 0)
            {
                InitializeBuffers(_width, _height);
                _needsReinit = false;

                if (_logLifecycle && !_loggedBuffersInitialized)
                {
                    _loggedBuffersInitialized = true;
                    Debug.Log($"[RsHandMeshBlock:{GetInstanceID()}] Buffers initialized. size=({_width}x{_height})");
                }
            }

            if (_gpuReady)
            {
                DispatchCompute();

                if (_enableCpuOutput)
                    UpdateCpuOutput();

                if (_logLifecycle && !_loggedFirstArgs)
                {
                    _loggedFirstArgs = true;
                    _argsBuffer.GetData(_argsReadback);
                    Debug.Log($"[RsHandMeshBlock:{GetInstanceID()}] First args: vertexCount={_argsReadback[0]} instanceCount={_argsReadback[1]}");
                }

                // Force a one-time args log even if inspector flags are not set on the clone instance.
                if (Application.isPlaying && !_loggedFirstArgsForced)
                {
                    _loggedFirstArgsForced = true;
                    _argsBuffer.GetData(_argsReadback);
                    Debug.Log($"[RsHandMeshBlock:{GetInstanceID()}] Args(forced): vertexCount={_argsReadback[0]} instanceCount={_argsReadback[1]}");
                }

                if (_logIndirectArgsEverySecond && Time.unscaledTime >= _nextArgsLogTime)
                {
                    _nextArgsLogTime = Time.unscaledTime + 1f;
                    _argsBuffer.GetData(_argsReadback);
                    Debug.Log($"[RsHandMeshBlock] args: vertexCount={_argsReadback[0]} instanceCount={_argsReadback[1]} startVertex={_argsReadback[2]} startInstance={_argsReadback[3]}");
                }

                if (_logLifecycle && !_loggedFirstDispatch)
                {
                    _loggedFirstDispatch = true;
                    Debug.Log($"[RsHandMeshBlock:{GetInstanceID()}] First compute dispatch done.");
                }
            }
        }

    }

    private void UpdateCpuOutput()
    {
        if (_argsBuffer == null || _vertexBuffer == null || _indexBuffer == null) return;

        _argsBuffer.GetData(_argsReadback);
        int indexCount = (int)_argsReadback[0];
        if (indexCount <= 0) return;

        int vertexCount = _width * _height;

        if (_vertexReadback == null || _vertexReadback.Length != vertexCount)
            _vertexReadback = new Vertex[vertexCount];
        if (_indexReadback == null || _indexReadback.Length < indexCount)
            _indexReadback = new int[indexCount];

        _vertexBuffer.GetData(_vertexReadback);
        _indexBuffer.GetData(_indexReadback, 0, 0, indexCount);

        if (LatestPositions == null || LatestPositions.Length != vertexCount)
            LatestPositions = new Vector3[vertexCount];
        if (LatestColors == null || LatestColors.Length != vertexCount)
            LatestColors = new Color[vertexCount];
        if (LatestIndices == null || LatestIndices.Length < indexCount)
            LatestIndices = new int[indexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            LatestPositions[i] = _vertexReadback[i].pos;
            LatestColors[i] = new Color(_vertexReadback[i].col.x, _vertexReadback[i].col.y, _vertexReadback[i].col.z, 1f);
        }
        Array.Copy(_indexReadback, LatestIndices, indexCount);
        LatestIndexCount = indexCount;
    }

    private void SaveDepthMapBmp(DepthFrame depthFrame)
    {
        int width = depthFrame.Width;
        int height = depthFrame.Height;
        
        ushort[] depthData = new ushort[width * height];
        depthFrame.CopyTo(depthData);
        float scale = 0.001f;

        byte[] pixels = new byte[width * height * 3]; // RGB24
        int idx = 0;

        for (int y = 0; y < height; y++)
        {
            // BMP files store rows bottom-to-top
            int rowStart = (height - 1 - y) * width;
            for (int x = 0; x < width; x++)
            {
                float depthMeters = depthData[rowStart + x] * scale;
                byte intensity = 0;
                if (depthMeters >= _minDistance && depthMeters <= _maxDistance)
                {
                    float t = (depthMeters - _minDistance) / (_maxDistance - _minDistance);
                    if (t < 0f) t = 0f;
                    if (t > 1f) t = 1f;
                    intensity = (byte)(t * 255f);
                }

                pixels[idx++] = intensity; // B
                pixels[idx++] = intensity; // G
                pixels[idx++] = intensity; // R
            }
        }

        int rowLen = width * 3;
        int pad = (4 - (rowLen % 4)) % 4;
        int rawDataSize = (rowLen + pad) * height;

        byte[] bmp = new byte[54 + rawDataSize];
        bmp[0] = 66; bmp[1] = 77; // 'B', 'M'

        System.BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        System.BitConverter.GetBytes(54).CopyTo(bmp, 10);
        System.BitConverter.GetBytes(40).CopyTo(bmp, 14);
        System.BitConverter.GetBytes(width).CopyTo(bmp, 18);
        System.BitConverter.GetBytes(height).CopyTo(bmp, 22);
        bmp[26] = 1;
        bmp[28] = 24;
        System.BitConverter.GetBytes(rawDataSize).CopyTo(bmp, 34);

        int bmpIdx = 54;
        int pIdx = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bmp[bmpIdx++] = pixels[pIdx++];
                bmp[bmpIdx++] = pixels[pIdx++];
                bmp[bmpIdx++] = pixels[pIdx++];
            }
            bmpIdx += pad;
        }

        if (!System.IO.Directory.Exists(_depthMapSaveFolder))
        {
            System.IO.Directory.CreateDirectory(_depthMapSaveFolder);
        }

        string filename = $"depth_frame_{_depthMapFrameCount:D4}.bmp";
        string path = System.IO.Path.Combine(_depthMapSaveFolder, filename);
        System.IO.File.WriteAllBytes(path, bmp);
    }

    private void SaveRawDepthData(DepthFrame depthFrame)
    {
        int width = depthFrame.Width;
        int height = depthFrame.Height;
        
        ushort[] depthData = new ushort[width * height];
        depthFrame.CopyTo(depthData);

        if (!System.IO.Directory.Exists(_depthMapSaveFolder))
        {
            System.IO.Directory.CreateDirectory(_depthMapSaveFolder);
        }

        string filenameBase = $"depth_raw_{_depthMapFrameCount:D4}";
        
        // Save binary depth data (16-bit unsigned ints)
        string binPath = System.IO.Path.Combine(_depthMapSaveFolder, filenameBase + ".bin");
        byte[] byteData = new byte[depthData.Length * 2];
        System.Buffer.BlockCopy(depthData, 0, byteData, 0, byteData.Length);
        System.IO.File.WriteAllBytes(binPath, byteData);

        // Save metadata as JSON
        DepthMetaData meta = new DepthMetaData
        {
            width = width,
            height = height,
            fx = _intrinsics.x,
            fy = _intrinsics.y,
            ppx = _intrinsics.z,
            ppy = _intrinsics.w,
            depthScale = 0.001f,
            transformMatrix = _transformMatrix
        };
        string jsonPath = System.IO.Path.Combine(_depthMapSaveFolder, filenameBase + ".json");
        System.IO.File.WriteAllText(jsonPath, JsonUtility.ToJson(meta, true));
    }

    private void InitializeBuffers(int w, int h)
    {
        // Recreate all GPU resources on (re)initialize.
        // (Resolution changes or domain reloads can otherwise leave incompatible buffers.)
        ReleaseBuffers();

        // Buffers
        _vertexBuffer = new ComputeBuffer(w * h, Marshal.SizeOf(typeof(Vertex)));
        
        int maxIndices = (w - 1) * (h - 1) * 6;
        _indexBuffer = new ComputeBuffer(maxIndices, sizeof(int), ComputeBufferType.Append);

        _depthBuffer = new ComputeBuffer(w * h / 2, 4); // Packed uint (2x16bit)
        _colorTexture = new Texture2D(w, h, TextureFormat.RGB24, false);

        // DrawProceduralIndirect args layout (4 uint):
        // [0] vertexCountPerInstance, [1] instanceCount, [2] startVertex, [3] startInstance
        _argsBuffer = new ComputeBuffer(1, 4 * sizeof(uint), ComputeBufferType.IndirectArguments);
        _argsBuffer.SetData(_indirectArgsInit);

        _gpuReady = true;
    }

    private void DispatchCompute()
    {
        // Update Textures/Buffers
        _colorTexture.LoadRawTextureData(_colorDataCache);
        _colorTexture.Apply();
        _depthBuffer.SetData(_depthDataCache);

        // Set Params
        _computeShader.SetInt("_Width", _width);
        _computeShader.SetInt("_Height", _height);
        _computeShader.SetFloat("_MinDist", _minDistance);
        _computeShader.SetFloat("_MaxDist", _maxDistance);
        _computeShader.SetFloat("_EdgeThreshold", _edgeThreshold);
        _computeShader.SetVector("_Intrinsics", _intrinsics);

        // Color params
        _computeShader.SetInt("_Mode", (int)_mode);
        _computeShader.SetFloat("_MinHue", _minHue);
        _computeShader.SetFloat("_MaxHue", _maxHue);
        _computeShader.SetFloat("_MinSat", _minSaturation);
        _computeShader.SetFloat("_MaxSat", _maxSaturation);
        _computeShader.SetFloat("_MinVal", _minValue);
        _computeShader.SetFloat("_MaxVal", _maxValue);
        _computeShader.SetInt("_MinY", _minY);
        _computeShader.SetInt("_MaxY", _maxY);
        _computeShader.SetInt("_MinCb", _minCb);
        _computeShader.SetInt("_MaxCb", _maxCb);
        _computeShader.SetInt("_MinCr", _minCr);
        _computeShader.SetInt("_MaxCr", _maxCr);

        // Kernel Vertices
        _computeShader.SetBuffer(_kernelVertices, "_InputDepthBuffer", _depthBuffer);
        _computeShader.SetTexture(_kernelVertices, "_InputColorTexture", _colorTexture);
        _computeShader.SetBuffer(_kernelVertices, "_VertexBuffer", _vertexBuffer);
        
        int groupsX = Mathf.CeilToInt(_width / 8f);
        int groupsY = Mathf.CeilToInt(_height / 8f);
        _computeShader.Dispatch(_kernelVertices, groupsX, groupsY, 1);

        // Kernel Indices
        _indexBuffer.SetCounterValue(0);
        _computeShader.SetBuffer(_kernelIndices, "_VertexBuffer", _vertexBuffer);
        _computeShader.SetBuffer(_kernelIndices, "_IndexBuffer", _indexBuffer);
        _computeShader.Dispatch(_kernelIndices, groupsX, groupsY, 1);

        // Copy count to args
        // Ensure args has correct layout for DrawProceduralIndirect (4 uint).
        // First write zeros + instanceCount=1, then overwrite args[0] with the append counter.
        _argsBuffer.SetData(_indirectArgsInit);
        ComputeBuffer.CopyCount(_indexBuffer, _argsBuffer, 0);

        // Safety: some platforms/drivers can clobber other fields; rewrite args[1..3] explicitly.
        // Read back 4 uints and re-upload with corrected values.
        _argsBuffer.GetData(_indirectArgsStaging);
        _indirectArgsStaging[1] = 1;
        _indirectArgsStaging[2] = 0;
        _indirectArgsStaging[3] = 0;
        _argsBuffer.SetData(_indirectArgsStaging);
    }

    // Drawing is done by standard MeshRenderer once the mesh is updated.
}
