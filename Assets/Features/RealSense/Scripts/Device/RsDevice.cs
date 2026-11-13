using System;
using System.Collections;
using System.Linq;
using System.Threading;
using Core.Logging;
using Intel.RealSense;
using UnityEngine;

/// <summary>
/// Manages streaming using a RealSense Device.
/// パス解決は <see cref="RsDevicePathResolver"/>、ネイティブ Pipeline とスレッド管理は <see cref="RsPipelineSession"/> に委譲するファサードです。
/// </summary>
[HelpURL("https://github.com/IntelRealSense/librealsense/tree/master/wrappers/unity")]
[AppLoggable("RealSense (Device)")]
public class RsDevice : RsFrameProvider, ILogContextProvider
{
    /// <summary>
    /// バックグラウンドスレッド等から安全に参照できるログ用プレフィックス
    /// </summary>
    public string LogPrefix
    {
        get
        {
            if (string.IsNullOrEmpty(m_deviceName) && AppLogger.IsMainThread)
            {
                try { m_deviceName = gameObject.name; } catch { }
            }
            return !string.IsNullOrEmpty(m_deviceName) ? $"RsDevice: {m_deviceName}" : "RsDevice";
        }
    }

    /// <summary>
    /// The parallelism mode of the module
    /// </summary>
    public enum ProcessMode
    {
        Multithread,
        UnityThread,
    }

    /// <summary>
    /// Threading mode of operation, Multithread or UnityThread
    /// </summary>
    [Tooltip("Threading mode of operation, Multithreads or Unitythread")]
    public ProcessMode processMode;

    /// <summary>
    /// The number of frames to record. Set to 0 for unlimited recording.
    /// </summary>
    [Tooltip("The number of frames to record. Set to 0 for unlimited recording.")]
    public int recordDurationInFrames = 0;

    /// <summary>
    /// User configuration
    /// </summary>
    public RsConfiguration DeviceConfiguration = new RsConfiguration
    {
        mode = RsConfiguration.Mode.Live,
        RequestedSerialNumber = string.Empty,
        Profiles = new RsVideoStreamRequest[] {
            new RsVideoStreamRequest {Stream = Intel.RealSense.Stream.Depth, StreamIndex = -1, Width = 640, Height = 480, Format = Format.Z16 , Framerate = 30 },
        }
    };

    [Tooltip("カメラが見つからない場合のリトライ回数（USB初期化待ち対策）")]
    public int maxRetries = 10;

    [Tooltip("リトライ間隔（秒）")]
    public float retryIntervalSeconds = 1f;

    /// <summary>
    /// Notifies upon streaming start
    /// </summary>
    public override event Action<PipelineProfile> OnStart;

    /// <summary>
    /// Notifies when streaming has stopped
    /// </summary>
    public override event Action OnStop;

    /// <summary>
    /// Fired when a new frame is available
    /// </summary>
    public override event Action<Frame> OnNewSample;

    private int frameCount = 0;
    private string m_deviceName;
    private RsPipelineSession _session;
    private CancellationTokenSource _startCts;

    void Awake()
    {
        m_deviceName = gameObject.name;
        _session = new RsPipelineSession(this, RaiseSampleEvent);
    }

    void OnEnable()
    {
        frameCount = 0;
        if (_session == null)
        {
            _session = new RsPipelineSession(this, RaiseSampleEvent);
        }

        CancelPendingStart();
        _startCts = new CancellationTokenSource();
        StartCoroutine(StartWithRetry(_startCts.Token));
    }

    void Update()
    {
        if (!Streaming)
            return;

        if (DeviceConfiguration.mode == RsConfiguration.Mode.Record && recordDurationInFrames > 0)
        {
            if (frameCount >= recordDurationInFrames)
            {
                AppLogger.Log(this, $"Expected recording duration reached: {frameCount}/{recordDurationInFrames} frames.");
                StopStreaming();
                return;
            }
        }

        if (processMode == ProcessMode.UnityThread)
        {
            _session?.PollFrames();
        }
    }

    void OnDisable()
    {
        StopStreaming();
    }

    void OnDestroy()
    {
        OnStop = null;
        CancelPendingStart();
        _session?.Dispose();
        _session = null;
        ActiveProfile = null;
    }

    private IEnumerator StartWithRetry(CancellationToken ct)
    {
        // シーンロード(Integration)中の同期ブロックを避けるため1フレーム待ってから非同期起動を開始
        yield return null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            if (ct.IsCancellationRequested || !isActiveAndEnabled || _session == null)
                yield break;

            var request = BuildStartRequest();
            var startTask = _session.StartBackgroundAsync(request, ct);

            while (!startTask.IsCompleted)
            {
                if (ct.IsCancellationRequested || !isActiveAndEnabled)
                    yield break;
                yield return null;
            }

            if (ct.IsCancellationRequested || !isActiveAndEnabled)
                yield break;

            var result = startTask.Result;
            if (result.Success)
            {
                ActiveProfile = result.Profile;
                DeviceConfiguration.Profiles = ActiveProfile.Streams.Select(RsVideoStreamRequest.FromProfile).ToArray();
                StartCoroutine(WaitAndStart());
                yield break;
            }

            if (result.Error != null)
            {
                AppLogger.LogWarning(this, $"Pipeline start error: {result.Error.Message}");
            }

            if (attempt < maxRetries)
            {
                AppLogger.LogWarning(this, $"Pipeline start failed (attempt {attempt}/{maxRetries}). Retrying in {retryIntervalSeconds}s...");
                yield return new WaitForSeconds(retryIntervalSeconds);
            }
            else
            {
                AppLogger.LogError(this, $"Pipeline start failed after {maxRetries} attempts. Camera may not be connected.");
            }
        }
    }

    private RsPipelineSession.StartRequest BuildStartRequest()
    {
        var mode = DeviceConfiguration.mode;
        string resolvedPath = null;

        if (mode == RsConfiguration.Mode.Playback)
        {
            resolvedPath = RsDevicePathResolver.ResolveAbsolutePath(DeviceConfiguration.PlaybackFile);
        }
        else if (mode == RsConfiguration.Mode.Record)
        {
            resolvedPath = RsDevicePathResolver.ResolveAbsolutePath(DeviceConfiguration.RecordPath);
            RsDevicePathResolver.EnsureRecordDirectory(resolvedPath);
            AppLogger.Log(this, $"Setup Recording => \nRaw Input: {DeviceConfiguration.RecordPath}\nResolved: {resolvedPath}");
        }

        return new RsPipelineSession.StartRequest
        {
            Mode = mode,
            SerialNumber = DeviceConfiguration.RequestedSerialNumber,
            ResolvedFilePath = resolvedPath,
            Profiles = DeviceConfiguration.Profiles != null ? DeviceConfiguration.Profiles.ToArray() : Array.Empty<RsVideoStreamRequest>(),
            UseMultithreadWorker = processMode == ProcessMode.Multithread
        };
    }

    private static int _lastOnStartFrame = -1;

    private IEnumerator WaitAndStart()
    {
        yield return new WaitForEndOfFrame();

        // 複数デバイスの OnStart（ComputeBuffer / Shader 確保）が同一フレームに集中しないよう分散
        while (_lastOnStartFrame == Time.frameCount)
        {
            yield return null;
        }
        _lastOnStartFrame = Time.frameCount;

        Streaming = true;
        OnStart?.Invoke(ActiveProfile);
    }

    private void StopStreaming()
    {
        CancelPendingStart();
        OnNewSample = null;
        _session?.StopWorker();

        if (Streaming)
        {
            OnStop?.Invoke();
            _session?.StopPipeline();
            Streaming = false;

            if (DeviceConfiguration.mode == RsConfiguration.Mode.Record)
            {
                RsDevicePathResolver.VerifyRecordedFile(DeviceConfiguration.RecordPath, frameCount, this);
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

    private void RaiseSampleEvent(Frame frame)
    {
        if (DeviceConfiguration.mode == RsConfiguration.Mode.Record && recordDurationInFrames > 0)
        {
            Interlocked.Increment(ref frameCount);
        }

        OnNewSample?.Invoke(frame);
    }
}
