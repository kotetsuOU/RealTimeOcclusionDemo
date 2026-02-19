using Intel.RealSense;
using System;
using System.Collections.Generic;
using UnityEngine;

[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false)]
public sealed class ProcessingBlockDataAttribute : System.Attribute
{
    // See the attribute guidelines at
    //  http://go.microsoft.com/fwlink/?LinkId=85236
    public readonly Type blockClass;

    public ProcessingBlockDataAttribute(Type blockClass)
    {
        this.blockClass = blockClass;
    }
}

[Serializable]
public class RsProcessingPipe : RsFrameProvider
{
    public RsFrameProvider Source;
    public RsProcessingProfile profile;

    public override event Action<PipelineProfile> OnStart;
    public override event Action OnStop;
    public override event Action<Frame> OnNewSample;

    private CustomProcessingBlock _block;
    private RsDepthToColorCalibration _calibration;
    private int _frameCounter = 0;

    [SerializeField] private int _processIntervalFrames = 1;

    public IReadOnlyList<RsProcessingBlock> RuntimeBlocks
        => profile != null ? profile._processingBlocks : (IReadOnlyList<RsProcessingBlock>)Array.Empty<RsProcessingBlock>();

    public T GetFirstBlock<T>() where T : RsProcessingBlock
    {
        if (profile == null || profile._processingBlocks == null) return null;
        foreach (var b in profile._processingBlocks)
        {
            if (b is T t) return t;
        }
        return null;
    }

    void Awake()
    {
        if (profile != null)
        {
            profile = Instantiate(profile);

            for (int i = 0; i < profile._processingBlocks.Count; i++)
            {
                if (profile._processingBlocks[i] != null)
                {
                    profile._processingBlocks[i] = Instantiate(profile._processingBlocks[i]);
                }
            }
        }
        else
        {
            profile = ScriptableObject.CreateInstance<RsProcessingProfile>();
            profile._processingBlocks = new List<RsProcessingBlock>();
        }

        if (Source != null)
        {
            Source.OnStart += OnSourceStart;
            Source.OnStop += OnSourceStop;
        }

        _block = new CustomProcessingBlock(ProcessFrame);
        _block.Start(OnFrame);

        if (_processIntervalFrames <= 0) { _processIntervalFrames = 1; }
    }

    private void OnSourceStart(PipelineProfile activeProfile)
    {
        if (Source != null)
        {
            Source.OnNewSample += _block.Process;
        }

        ActiveProfile = activeProfile;

        _calibration = new RsDepthToColorCalibration(activeProfile);

        if (profile != null)
        {
            foreach (var pb in profile._processingBlocks)
            {
                if (pb is RsIntegratedPointCloud integratedPointCloud)
                {
                    integratedPointCloud.SetCalibration(_calibration);
                }
            }
        }

        Streaming = true;

        OnStart?.Invoke(activeProfile);
    }

    private void OnSourceStop()
    {
        if (!Streaming)
            return;

        if (_block != null && Source != null)
            Source.OnNewSample -= _block.Process;

        Streaming = false;

        OnStop?.Invoke();
    }

    private void OnFrame(Frame f)
    {
        OnNewSample?.Invoke(f);
    }

    private void OnDestroy()
    {
        OnSourceStop();
        if (_block != null)
        {
            _block.Dispose();
            _block = null;
        }
    }

    internal void ProcessFrame(Frame frame, Action<Frame> output)
    {
        _frameCounter++;

        if (_frameCounter % _processIntervalFrames != 0)
            return;

        try
        {
            if (!Streaming)
                return;

            Frame f = frame;

            if (profile != null)
            {
                var filters = profile._processingBlocks;

                foreach (var pb in filters)
                {
                    if (pb == null) continue;

                    if (!pb.Enabled)
                        continue;

                    Frame processed = pb.Process(f, default(FrameSource));

                    if (processed != f)
                    {
                        if (f != frame)
                        {
                            f.Dispose();
                        }
                        f = processed;
                    }
                }
            }

            output(f);

            if (f != frame)
                f.Dispose();
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogException(e);
        }
    }

    public void SetProcessIntervalFrames(int value)
    {
        _processIntervalFrames = value;
    }

    public int GetProcessIntervalFrames()
    {
        return _processIntervalFrames;
    }
}