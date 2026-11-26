using Intel.RealSense;
using System;
using System.Linq;

public class RsDataProvider : IDisposable
{
    private readonly IRsFrameSource _frameSource;
    private FrameQueue _frameQueue;
    public int FrameWidth { get; private set; }
    public int FrameHeight { get; private set; }

    public RsDataProvider(IRsFrameSource frameSource)
    {
        _frameSource = frameSource;
    }

    public void Start()
    {
        if (_frameSource == null) return;

        _frameSource.OnStart += OnStartStreaming;
        _frameSource.OnStop += Dispose;

        if (_frameSource.Streaming && _frameSource.ActiveProfile != null)
        {
            OnStartStreaming(_frameSource.ActiveProfile);
        }
    }

    private void OnStartStreaming(PipelineProfile profile)
    {
        if (_frameQueue != null) return;

        _frameQueue = new FrameQueue(1);
        using (var depth = profile.Streams.FirstOrDefault(s => s.Stream == Intel.RealSense.Stream.Depth && s.Format == Format.Z16).As<VideoStreamProfile>())
        {
            FrameWidth = depth.Width;
            FrameHeight = depth.Height;
        }
        _frameSource.OnNewSample += OnNewSample;
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
        if (_frameSource != null)
        {
            _frameSource.OnNewSample -= OnNewSample;
            _frameSource.OnStart -= OnStartStreaming;
            _frameSource.OnStop -= Dispose;
        }
        _frameQueue?.Dispose();
        _frameQueue = null;
    }
}
