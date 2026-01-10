using Intel.RealSense;
using System;
using UnityEngine;

public class CustomProcessingBlock : IDisposable
{
    public delegate void FrameProcessor(Frame frame, Action<Frame> output);

    private readonly FrameProcessor _userProcessor;
    private Action<Frame> _onFrameCallback;
    private bool _disposed = false;

    public CustomProcessingBlock(FrameProcessor processor)
    {
        _userProcessor = processor;
    }

    public void Start(Action<Frame> onFrame)
    {
        _onFrameCallback = onFrame;
    }

    public void Process(Frame frame)
    {
        if (_disposed || frame == null) return;

        try
        {
            _userProcessor(frame, OutputFrame);
        }
        catch (Exception e)
        {
            Debug.LogError($"[CustomProcessingBlock] Error processing frame: {e.Message}");
        }
    }

    private void OutputFrame(Frame frame)
    {
        if (_disposed || _onFrameCallback == null) return;
        _onFrameCallback.Invoke(frame);
    }

    public void Dispose()
    {
        _disposed = true;
        _onFrameCallback = null;
    }
}