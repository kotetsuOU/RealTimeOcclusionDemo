using System;
using System.Threading;
using System.Threading.Tasks;
using Core.Logging;
using Intel.RealSense;

/// <summary>
/// RealSense ネイティブ Pipeline の非同期起動・フレーム待機スレッド・停止および破棄を管理するセッションクラス。
/// </summary>
internal sealed class RsPipelineSession : IDisposable
{
    public struct StartRequest
    {
        public RsConfiguration.Mode Mode;
        public string SerialNumber;
        public string ResolvedFilePath;
        public RsVideoStreamRequest[] Profiles;
        public bool UseMultithreadWorker;
    }

    public struct StartResult
    {
        public bool Success;
        public PipelineProfile Profile;
        public Exception Error;
    }

    // librealsense のネイティブ初期化を別スレッドへ逃がしつつ、複数台同時起動の USB 競合を防ぐ直列ゲート
    private static readonly SemaphoreSlim _pipelineStartLock = new SemaphoreSlim(1, 1);

    private readonly UnityEngine.Object _logContext;
    private readonly Action<Frame> _onFrameReceived;
    private readonly AutoResetEvent _stopEvent = new AutoResetEvent(false);

    private Pipeline _pipeline;
    private PipelineProfile _profile;
    private Thread _worker;
    private bool _disposed;

    public PipelineProfile ActiveProfile => _profile;

    public RsPipelineSession(UnityEngine.Object logContext, Action<Frame> onFrameReceived)
    {
        _logContext = logContext;
        _onFrameReceived = onFrameReceived;
    }

    /// <summary>
    /// パイプラインをワーカースレッド上で直列ゲート付き起動し、成否結果を返します。
    /// </summary>
    public Task<StartResult> StartBackgroundAsync(StartRequest request, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            Pipeline pipeline = null;
            PipelineProfile profile = null;
            bool gateAcquired = false;

            try
            {
                await _pipelineStartLock.WaitAsync(ct).ConfigureAwait(false);
                gateAcquired = true;
                ct.ThrowIfCancellationRequested();

                pipeline = new Pipeline();
                using (var cfg = new Config())
                {
                    ApplyConfig(cfg, request);
                    profile = pipeline.Start(cfg);
                }

                if (ct.IsCancellationRequested)
                {
                    DisposeTempResources(pipeline, profile, stopFirst: true);
                    return new StartResult { Success = false };
                }

                return new StartResult
                {
                    Success = true,
                    Profile = CommitStartedPipeline(pipeline, profile, request.UseMultithreadWorker)
                };
            }
            catch (OperationCanceledException)
            {
                DisposeTempResources(pipeline, profile, stopFirst: false);
                return new StartResult { Success = false };
            }
            catch (Exception ex)
            {
                DisposeTempResources(pipeline, profile, stopFirst: false);
                return new StartResult { Success = false, Error = ex };
            }
            finally
            {
                if (gateAcquired)
                {
                    _pipelineStartLock.Release();
                }
            }
        });
    }

    /// <summary>
    /// UnityThread モード時に 1 フレーム分のフレームセットを非ブロッキングでポーリングします。
    /// </summary>
    public void PollFrames()
    {
        if (_pipeline == null) return;

        FrameSet frames;
        if (_pipeline.PollForFrames(out frames))
        {
            using (frames)
            {
                _onFrameReceived?.Invoke(frames);
            }
        }
    }

    /// <summary>
    /// フレーム待機ワーカースレッドを停止・合流します。
    /// </summary>
    public void StopWorker()
    {
        if (_worker != null)
        {
            _stopEvent.Set();
            _worker.Join();
            _worker = null;
        }
    }

    /// <summary>
    /// ストリーミング中のネイティブパイプラインを安全に停止します。
    /// </summary>
    public void StopPipeline()
    {
        if (_pipeline != null && _profile != null)
        {
            try
            {
                _pipeline.Stop();
            }
            catch (Exception e)
            {
                AppLogger.LogWarning(_logContext, $"Pipeline stop warning: {e.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_worker != null)
        {
            _stopEvent.Set();
            _worker.Join();
            _worker = null;
        }

        if (_profile != null)
        {
            _profile.Dispose();
            _profile = null;
        }

        if (_pipeline != null)
        {
            _pipeline.Dispose();
            _pipeline = null;
        }

        _stopEvent.Dispose();
    }

    private PipelineProfile CommitStartedPipeline(Pipeline pipeline, PipelineProfile profile, bool useMultithreadWorker)
    {
        if (_profile != null)
        {
            _profile.Dispose();
        }
        if (_pipeline != null)
        {
            _pipeline.Dispose();
        }

        _pipeline = pipeline;
        _profile = profile;

        if (useMultithreadWorker)
        {
            _stopEvent.Reset();
            _worker = new Thread(WaitForFramesLoop)
            {
                IsBackground = true
            };
            _worker.Start();
        }

        return _profile;
    }

    private void WaitForFramesLoop()
    {
        int consecutiveErrors = 0;
        const int maxConsecutiveErrors = 10;
        const int retryDelayMs = 500;

        while (!_stopEvent.WaitOne(0))
        {
            try
            {
                using (var frames = _pipeline.WaitForFrames())
                {
                    consecutiveErrors = 0;
                    _onFrameReceived?.Invoke(frames);
                }
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                AppLogger.LogWarning(_logContext, $"WaitForFrames error ({consecutiveErrors}/{maxConsecutiveErrors}): {ex.Message}");

                if (consecutiveErrors >= maxConsecutiveErrors)
                {
                    AppLogger.LogError(_logContext, "Too many consecutive errors. Device may be disconnected or unresponsive.");
                    break;
                }

                Thread.Sleep(retryDelayMs);
            }
        }
    }

    private static void ApplyConfig(Config cfg, in StartRequest request)
    {
        switch (request.Mode)
        {
            case RsConfiguration.Mode.Live:
                if (!string.IsNullOrEmpty(request.SerialNumber))
                    cfg.EnableDevice(request.SerialNumber);
                foreach (var p in request.Profiles)
                    p.Apply(cfg);
                break;
            case RsConfiguration.Mode.Playback:
                cfg.EnableDeviceFromFile(request.ResolvedFilePath);
                break;
            case RsConfiguration.Mode.Record:
                if (!string.IsNullOrEmpty(request.SerialNumber))
                    cfg.EnableDevice(request.SerialNumber);
                cfg.EnableRecordToFile(request.ResolvedFilePath);
                foreach (var p in request.Profiles)
                    p.Apply(cfg);
                break;
        }
    }

    private static void DisposeTempResources(Pipeline pipeline, PipelineProfile profile, bool stopFirst)
    {
        if (stopFirst && pipeline != null)
        {
            try { pipeline.Stop(); } catch { }
        }
        try { profile?.Dispose(); } catch { }
        try { pipeline?.Dispose(); } catch { }
    }
}
