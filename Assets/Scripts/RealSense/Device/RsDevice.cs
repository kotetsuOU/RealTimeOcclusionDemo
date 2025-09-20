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
                        cfg.EnableDeviceFromFile(DeviceConfiguration.PlaybackFile);
                        break;
                    }
                case RsConfiguration.Mode.Record:
                    {
                        if (!string.IsNullOrEmpty(DeviceConfiguration.RequestedSerialNumber))
                            cfg.EnableDevice(DeviceConfiguration.RequestedSerialNumber);
                        cfg.EnableRecordToFile(DeviceConfiguration.RecordPath);
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

        if (Streaming && OnStop != null)
            OnStop();

        if (m_pipeline != null)
        {
            m_pipeline.Stop();
        }
        Streaming = false;
    }

    private void RaiseSampleEvent(Frame frame)
    {
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
        while (!stopEvent.WaitOne(0))
        {
            try
            {
                using (var frames = m_pipeline.WaitForFrames())
                {
                    RaiseSampleEvent(frames);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("RealSense WaitForFrames error: " + ex.Message);
            }
        }
    }

    void Update()
    {
        if (!Streaming)
            return;

        if (processMode != ProcessMode.UnityThread)
            return;

        FrameSet frames;
        if (m_pipeline.PollForFrames(out frames))
        {
            using (frames)
            {
                if (DeviceConfiguration.mode == RsConfiguration.Mode.Record && recordDurationInFrames > 0)
                {
                    frameCount++;
                    if (frameCount >= recordDurationInFrames)
                    {
                        UnityEngine.Debug.Log($"Recording completed after {frameCount} frames.");
                        StopStreaming();
                        return;
                    }
                }
                RaiseSampleEvent(frames);
            }
        }
    }
}