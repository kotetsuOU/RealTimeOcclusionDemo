using System;
using System.Threading;
using UnityEngine;
using Intel.RealSense;
using System.Collections;
using System.Linq;

/// <summary>
/// Manages streaming using a RealSense Device
/// </summary>
[HelpURL("https://github.com/IntelRealSense/librealsense/tree/master/wrappers/unity")]
public class RsDevice : RsFrameProvider
{
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

    private int frameCount = 0;

    private string GetAbsolutePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        path = path.Replace("\\", "/");

        if (!System.IO.Path.IsPathRooted(path) || path.StartsWith("Assets/"))
        {
            string projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..")).Replace("\\", "/");

            if (path.StartsWith("Assets/"))
            {
                path = path.Substring(7);
            }

            return projectRoot + "/Assets/" + path;
        }

        return path;
    }

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

    private Thread worker;
    private readonly AutoResetEvent stopEvent = new AutoResetEvent(false);
    private Pipeline m_pipeline;

    void OnEnable()
    {
        frameCount = 0;
        m_pipeline = new Pipeline();

        using (var cfg = new Config())
        {
            switch (DeviceConfiguration.mode)
            {
                case RsConfiguration.Mode.Live:
                    {
                        if (!string.IsNullOrEmpty(DeviceConfiguration.RequestedSerialNumber))
                            cfg.EnableDevice(DeviceConfiguration.RequestedSerialNumber);
                        foreach (var p in DeviceConfiguration.Profiles)
                            p.Apply(cfg);
                        break;
                    }
                case RsConfiguration.Mode.Playback:
                    {
                        var finalPlaybackPath = GetAbsolutePath(DeviceConfiguration.PlaybackFile);
                        cfg.EnableDeviceFromFile(finalPlaybackPath);
                        break;
                    }
                case RsConfiguration.Mode.Record:
                    {
                        if (!string.IsNullOrEmpty(DeviceConfiguration.RequestedSerialNumber))
                            cfg.EnableDevice(DeviceConfiguration.RequestedSerialNumber);

                        var finalRecordPath = GetAbsolutePath(DeviceConfiguration.RecordPath);
                        var recordDir = System.IO.Path.GetDirectoryName(finalRecordPath);
                        if (!string.IsNullOrEmpty(recordDir) && !System.IO.Directory.Exists(recordDir))
                        {
                            System.IO.Directory.CreateDirectory(recordDir);
                        }

                        cfg.EnableRecordToFile(finalRecordPath);
                        UnityEngine.Debug.Log($"[RsDevice] Setup Recording => \nRaw Input: {DeviceConfiguration.RecordPath}\nResolved: {finalRecordPath}");
                        foreach (var p in DeviceConfiguration.Profiles)
                            p.Apply(cfg);
                        break;
                    }
            }

            ActiveProfile = m_pipeline.Start(cfg);
        }

        DeviceConfiguration.Profiles = ActiveProfile.Streams.Select(RsVideoStreamRequest.FromProfile).ToArray();

        if (processMode == ProcessMode.Multithread)
        {
            stopEvent.Reset();
            worker = new Thread(WaitForFrames);
            worker.IsBackground = true;
            worker.Start();
        }

        StartCoroutine(WaitAndStart());
    }

    IEnumerator WaitAndStart()
    {
        yield return new WaitForEndOfFrame();
        Streaming = true;
        if (OnStart != null)
            OnStart(ActiveProfile);
    }

    void OnDisable()
    {
        StopStreaming();
    }

    void OnDestroy()
    {
        OnStop = null;
        if (ActiveProfile != null)
        {
            ActiveProfile.Dispose();
            ActiveProfile = null;
        }

        if (m_pipeline != null)
        {
            m_pipeline.Dispose();
            m_pipeline = null;
        }
    }

    private void StopStreaming()
    {
        OnNewSample = null;

        if (worker != null)
        {
            stopEvent.Set();
            worker.Join();
            worker = null;
        }

        if (Streaming)
        {
            if (OnStop != null)
                OnStop();

            if (m_pipeline != null && ActiveProfile != null)
            {
                try
                {
                    m_pipeline.Stop();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"Pipeline stop warning: {e.Message}");
                }
            }
            Streaming = false;

            if (DeviceConfiguration.mode == RsConfiguration.Mode.Record)
            {
                var finalRecordPath = GetAbsolutePath(DeviceConfiguration.RecordPath);
                if (System.IO.File.Exists(finalRecordPath))
                {
                    var fileInfo = new System.IO.FileInfo(finalRecordPath);
                    UnityEngine.Debug.Log($"[RsDevice] Recording Stopped. File successfully found on disk: {finalRecordPath}\nFrames captured: {frameCount}, File Size: {fileInfo.Length} bytes.");
#if UNITY_EDITOR
                    UnityEditor.AssetDatabase.Refresh();
#endif
                }
                else
                {
                    UnityEngine.Debug.LogError($"[RsDevice] File NOT found on disk! Path: {finalRecordPath}. Frames captured: {frameCount}. Check if RealSense devices are working properly.");
                }
            }
        }
    }

    private void RaiseSampleEvent(Frame frame)
    {
        if (DeviceConfiguration.mode == RsConfiguration.Mode.Record && recordDurationInFrames > 0)
        {
            Interlocked.Increment(ref frameCount);
        }

        var onNewSample = OnNewSample;
        if (onNewSample != null)
        {
            onNewSample(frame);
        }
    }

    /// <summary>
    /// Worker Thread for multithreaded operations
    /// </summary>
    private void WaitForFrames()
    {
        int consecutiveErrors = 0;
        const int maxConsecutiveErrors = 10;
        const int retryDelayMs = 500;

        while (!stopEvent.WaitOne(0))
        {
            try
            {
                using (var frames = m_pipeline.WaitForFrames())
                {
                    consecutiveErrors = 0;
                    RaiseSampleEvent(frames);
                }
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                UnityEngine.Debug.LogWarning($"RealSense WaitForFrames error ({consecutiveErrors}/{maxConsecutiveErrors}): {ex.Message}");
                
                if (consecutiveErrors >= maxConsecutiveErrors)
                {
                    UnityEngine.Debug.LogError("RealSense: Too many consecutive errors. Device may be disconnected or unresponsive.");
                    break;
                }
                
                Thread.Sleep(retryDelayMs);
            }
        }
    }

    void Update()
    {
        if (!Streaming)
            return;

        if (DeviceConfiguration.mode == RsConfiguration.Mode.Record && recordDurationInFrames > 0)
        {
            if (frameCount >= recordDurationInFrames)
            {
                UnityEngine.Debug.Log($"[RsDevice] Expected recording duration reached: {frameCount}/{recordDurationInFrames} frames.");
                StopStreaming();
                return;
            }
        }

        if (processMode != ProcessMode.UnityThread)
            return;

        FrameSet frames;
        if (m_pipeline.PollForFrames(out frames))
        {
            using (frames)
            {
                RaiseSampleEvent(frames);
            }
        }
    }
}