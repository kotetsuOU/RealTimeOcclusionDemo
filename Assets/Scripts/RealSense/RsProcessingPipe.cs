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

    void Awake()
    {
        // 【修正点】プロファイルと内部ブロックのディープコピー（インスタンス化）
        // これを行わないと、複数のカメラで同一のScriptableObject（メモリ）を共有してしまい、
        // キャリブレーション情報やGPUバッファが混線する。
        if (profile != null)
        {
            // 1. Profile自体を複製（これでこのPipe専用のProfileになる）
            profile = Instantiate(profile);

            // 2. Profile内の各ブロックも複製（中身のScriptableObjectもユニークにする）
            for (int i = 0; i < profile._processingBlocks.Count; i++)
            {
                if (profile._processingBlocks[i] != null)
                {
                    // ScriptableObject.Instantiateにより、独立したメモリ領域を確保
                    profile._processingBlocks[i] = Instantiate(profile._processingBlocks[i]);
                }
            }
        }
        else
        {
            // プロファイルがない場合は空を作成
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
        // ブロックのインスタンス化後にイベント購読を行うため、ここでの変更は不要
        if (Source != null)
        {
            // 修正: CustomProcessingBlockのAction対応版シグネチャを使用
            // (前回修正した CustomProcessingBlock.cs の仕様に合わせる)
            // CustomProcessingBlock側が Process(Frame) を呼ぶため、ここでは購読のみでよいが、
            // _blockは内部で処理を持つため、Sourceからのイベントをブリッジする必要があるか？

            // CustomProcessingBlockの実装によれば、
            // _block.Process(Frame) を呼ぶ必要がある。
            Source.OnNewSample += _block.Process;
        }

        ActiveProfile = activeProfile;

        _calibration = new RsDepthToColorCalibration(activeProfile);

        if (profile != null)
        {
            foreach (var pb in profile._processingBlocks)
            {
                // インスタンス化されたユニークなブロックに対してキャリブレーションをセット
                if (pb is RsIntegratedPointCloud integratedPointCloud)
                {
                    integratedPointCloud.SetCalibration(_calibration);
                }
                // 不要な型チェック削除済み
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

    // CustomProcessingBlock (Action版) に対応したメソッドシグネチャ
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

                    // FrameSource引数は使わないためdefaultでOK
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