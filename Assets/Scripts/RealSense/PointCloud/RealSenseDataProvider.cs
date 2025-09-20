using Intel.RealSense;
using System;
using System.IO;
using System.Linq;

public class RealSenseDataProvider : IDisposable
{
    private readonly RsProcessingPipe _processingPipe;
    private FrameQueue _frameQueue;
    public int FrameWidth { get; private set; }
    public int FrameHeight { get; private set; }

    public RealSenseDataProvider(RsProcessingPipe processingPipe)
    {
        _processingPipe = processingPipe;
    }

    public void Start()
    {
        _processingPipe.OnStart += OnStartStreaming;
        _processingPipe.OnStop += Dispose;
    }

    private void OnStartStreaming(PipelineProfile profile)
    {
        _frameQueue = new FrameQueue(1);
        using (var depth = profile.Streams.FirstOrDefault(s => s.Stream == Intel.RealSense.Stream.Depth && s.Format == Format.Z16).As<VideoStreamProfile>())
        {
            FrameWidth = depth.Width;
            FrameHeight = depth.Height;
        }
        _processingPipe.OnNewSample += OnNewSample;
    }

    private void OnNewSample(Frame frame)
    {
        if (_frameQueue == null) return;
        try
        {
            if (frame.IsComposite)
            {
                using (var fs = frame.As<FrameSet>())
                using (var points = fs.FirstOrDefault<Points>(Intel.RealSense.Stream.Depth, Format.Xyz32f))
                {
                    if (points != null) _frameQueue.Enqueue(points);
                }
                return;
            }
            if (frame.Is(Extension.Points)) _frameQueue.Enqueue(frame);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogException(e);
        }
    }

    public bool PollForFrame(out Points points)
    {
        points = null;
        return _frameQueue?.PollForFrame(out points) ?? false;
    }

    public void Dispose()
    {
        if (_processingPipe != null)
        {
            _processingPipe.OnNewSample -= OnNewSample;
            _processingPipe.OnStart -= OnStartStreaming;
            _processingPipe.OnStop -= Dispose;
        }
        _frameQueue?.Dispose();
        _frameQueue = null;
    }
}
