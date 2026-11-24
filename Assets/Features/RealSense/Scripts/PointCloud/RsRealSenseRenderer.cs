using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Core.Logging;
using Intel.RealSense;
using UnityEngine;

/// <summary>
/// <see cref="RsDevice"/>・<see cref="RsProcessingPipe"/>・<see cref="RsPointCloudRenderer"/> の
/// 3コンポーネントの窓口を1つに統合した RealSense 点群コンポーネント。
/// デバイス制御は <see cref="RsPipelineSession"/>、ブロック処理は <see cref="RsProcessingRunner"/> に委譲します。
/// </summary>
[AppLoggable("RealSense (Integrated)")]
public class RsRealSenseRenderer : RsPointCloudRenderer, IRsFrameSource, IRsProcessIntervalConfigurable, ILogContextProvider
{
    #region Device & Pipeline Inspector Fields

    [Header("Device Settings")]
    [Tooltip("Threading mode of operation, Multithread or UnityThread")]
    public RsDevice.ProcessMode processMode = RsDevice.ProcessMode.Multithread;

    [Tooltip("The number of frames to record. Set to 0 for unlimited recording.")]
    public int recordDurationInFrames = 0;

    public RsConfiguration DeviceConfiguration = new RsConfiguration
    {
        mode = RsConfiguration.Mode.Live,
        RequestedSerialNumber = string.Empty,
        Profiles = new RsVideoStreamRequest[]
        {
            new RsVideoStreamRequest { Stream = Intel.RealSense.Stream.Depth, StreamIndex = -1, Width = 640, Height = 480, Format = Format.Z16, Framerate = 30 },
        }
    };

    [Tooltip("カメラが見つからない場合のリトライ回数（USB初期化待ち対策）")]
    public int maxRetries = 10;

    [Tooltip("リトライ間隔（秒）")]
    public float retryIntervalSeconds = 1f;

    [Header("Processing Pipeline Settings")]
    [Tooltip("適用するプロセッシングブロックが定義されたプロファイル（ScriptableObject）")]
    public RsProcessingProfile processingProfile;

    [Tooltip("処理を実行する間隔（フレーム数）。1の場合は毎フレーム処理を行う。")]
    [SerializeField] private int _processIntervalFrames = 1;

    #endregion

    #region IRsFrameSource Implementation

    public bool Streaming { get; private set; }
    public PipelineProfile ActiveProfile { get; private set; }

    public event Action<PipelineProfile> OnStart;
    public event Action OnStop;
    public event Action<Frame> OnNewSample;

    #endregion

    #region Private Fields

    private static int _lastOnStartFrame = -1;

    private RsPipelineSession _session;
    private RsProcessingRunner _runner;
    private CancellationTokenSource _startCts;
    private int _recordedFrameCount = 0;
    private string _deviceName;
    private bool _hasStarted = false;

    #endregion

    /// <summary>
    /// バックグラウンドスレッド等から安全に参照できるログ用プレフィックス
    /// </summary>
    public string LogPrefix
    {
        get
        {
            if (string.IsNullOrEmpty(_deviceName) && AppLogger.IsMainThread)
            {
                try { _deviceName = gameObject.name; } catch { }
            }
            return !string.IsNullOrEmpty(_deviceName) ? $"RsRealSenseRenderer: {_deviceName}" : "RsRealSenseRenderer";
        }
    }

    public IReadOnlyList<RsProcessingBlock> RuntimeBlocks
        => _runner != null ? _runner.RuntimeBlocks : (IReadOnlyList<RsProcessingBlock>)Array.Empty<RsProcessingBlock>();

    public T GetFirstBlock<T>() where T : RsProcessingBlock
    {
        return _runner != null ? _runner.GetFirstBlock<T>() : null;
    }

    public void SetProcessIntervalFrames(int value)
    {
        _processIntervalFrames = value > 0 ? value : 1;
        if (_runner != null)
        {
            _runner.ProcessIntervalFrames = _processIntervalFrames;
        }
    }

    public int GetProcessIntervalFrames() => _processIntervalFrames;

    #region Unity Lifecycle

    private void OnValidate()
    {
        if (_processIntervalFrames <= 0)
            _processIntervalFrames = 1;

        if (_runner != null)
            _runner.ProcessIntervalFrames = _processIntervalFrames;
    }

    private void Awake()
    {
        _deviceName = gameObject.name;
        if (_processIntervalFrames <= 0)
            _processIntervalFrames = 1;

        _session = new RsPipelineSession(this, OnDeviceSample);
        _runner = new RsProcessingRunner(processingProfile, _processIntervalFrames, OnProcessedFrame);
    }

    private void OnEnable()
    {
        _recordedFrameCount = 0;

        if (_session == null)
            _session = new RsPipelineSession(this, OnDeviceSample);

        if (_runner == null)
            _runner = new RsProcessingRunner(processingProfile, _processIntervalFrames, OnProcessedFrame);

        CancelPendingStart();
        _startCts = new CancellationTokenSource();
        StartCoroutine(RsPipelineStartHelper.StartWithRetry(
            this,
            _session,
            () => RsPipelineStartHelper.BuildStartRequest(DeviceConfiguration, processMode, this),
            maxRetries,
            retryIntervalSeconds,
            OnPipelineStarted,
            _startCts.Token));
    }

    private void Update()
    {
        if (!Streaming)
            return;

        if (DeviceConfiguration.mode == RsConfiguration.Mode.Record && recordDurationInFrames > 0)
        {
            if (_recordedFrameCount >= recordDurationInFrames)
            {
                AppLogger.Log(this, $"Expected recording duration reached: {_recordedFrameCount}/{recordDurationInFrames} frames.");
                StopStreaming();
                return;
            }
        }

        if (processMode == RsDevice.ProcessMode.UnityThread)
        {
            _session?.PollFrames();
        }
    }

    private void OnDisable()
    {
        StopStreaming();
    }

    protected override void TryAutoAssignDependencies()
    {
        // 統合コンポーネントでは外部 RsProcessingPipe を参照しないため、RsDeviceController のみ補完する
        if (rsDeviceController == null)
        {
            rsDeviceController = FindFirstObjectByType<RsDeviceController>();
        }
    }

    protected override void OnRendererStart()
    {
        _hasStarted = true;

        if (Streaming && ActiveProfile != null)
        {
            InitializePointCloud(this, _runner.RuntimeProfile, ActiveProfile);
        }
    }

    protected override void OnRendererDispose()
    {
        OnStart = null;
        OnStop = null;
        OnNewSample = null;

        CancelPendingStart();

        _runner?.Dispose();
        _runner = null;

        _session?.Dispose();
        _session = null;

        ActiveProfile = null;
        Initializer?.Dispose();
    }

    #endregion

    #region Device Connection & Streaming

    private void OnPipelineStarted(PipelineProfile profile)
    {
        ActiveProfile = profile;
        DeviceConfiguration.Profiles = ActiveProfile.Streams.Select(RsVideoStreamRequest.FromProfile).ToArray();
        StartCoroutine(WaitAndStart());
    }

    private IEnumerator WaitAndStart()
    {
        yield return new WaitForEndOfFrame();

        // 複数デバイスの OnStart（ComputeBuffer / Shader 確保）が同一フレームに集中しないよう分散
        while (_lastOnStartFrame == Time.frameCount)
        {
            yield return null;
        }
        _lastOnStartFrame = Time.frameCount;

        _runner?.StartStreaming(ActiveProfile);
        Streaming = true;

        if (_hasStarted)
        {
            InitializePointCloud(this, _runner?.RuntimeProfile, ActiveProfile);
        }

        OnStart?.Invoke(ActiveProfile);
    }

    private void StopStreaming()
    {
        CancelPendingStart();
        _session?.StopWorker();
        _runner?.StopStreaming();

        if (Streaming)
        {
            OnStop?.Invoke();
            _session?.StopPipeline();
            Streaming = false;

            if (DeviceConfiguration.mode == RsConfiguration.Mode.Record)
            {
                RsDevicePathResolver.VerifyRecordedFile(DeviceConfiguration.RecordPath, _recordedFrameCount, this);
            }
        }
    }

    private void CancelPendingStart()
    {
        if (_startCts != null)
        {
            _startCts.Cancel();
            _startCts.Dispose();
            _startCts = null;
        }
    }

    private void OnDeviceSample(Frame frame)
    {
        if (DeviceConfiguration.mode == RsConfiguration.Mode.Record && recordDurationInFrames > 0)
        {
            Interlocked.Increment(ref _recordedFrameCount);
        }

        _runner?.EnqueueFrame(frame);
    }

    private void OnProcessedFrame(Frame frame)
    {
        OnNewSample?.Invoke(frame);
    }

    #endregion
}

