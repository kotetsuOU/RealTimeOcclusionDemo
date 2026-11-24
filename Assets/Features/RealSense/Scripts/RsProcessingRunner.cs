using System;
using System.Collections.Generic;
using Intel.RealSense;
using UnityEngine;

/// <summary>
/// <see cref="RsProcessingProfile"/> に定義された複数の <see cref="RsProcessingBlock"/> を管理し、
/// フレームの間引きおよびパイプライン処理を順次実行する Pure C# ランナー。
/// </summary>
public sealed class RsProcessingRunner : IDisposable
{
    private readonly Action<Frame> _onFrameProcessed;
    private readonly List<UnityEngine.Object> _instantiatedObjects = new List<UnityEngine.Object>();

    private RsProcessingProfile _runtimeProfile;
    private CustomProcessingBlock _block;
    private RsDepthToColorCalibration _calibration;
    private int _frameCounter = 0;
    private int _processIntervalFrames = 1;
    private bool _isRunning = false;
    private bool _isDisposed = false;

    /// <summary>
    /// 実行時に複製されたランタイム用プロファイル
    /// </summary>
    public RsProcessingProfile RuntimeProfile => _runtimeProfile;

    /// <summary>
    /// 現在適用されている処理ブロックの読み取り専用リスト
    /// </summary>
    public IReadOnlyList<RsProcessingBlock> RuntimeBlocks
        => _runtimeProfile != null && _runtimeProfile._processingBlocks != null
            ? _runtimeProfile._processingBlocks
            : (IReadOnlyList<RsProcessingBlock>)Array.Empty<RsProcessingBlock>();

    /// <summary>
    /// 処理を実行するフレーム間隔（1以上で毎フレームまたは間引き実行）
    /// </summary>
    public int ProcessIntervalFrames
    {
        get => _processIntervalFrames;
        set => _processIntervalFrames = value > 0 ? value : 1;
    }

    public RsProcessingRunner(RsProcessingProfile sourceProfile, int processIntervalFrames, Action<Frame> onFrameProcessed)
    {
        _onFrameProcessed = onFrameProcessed ?? throw new ArgumentNullException(nameof(onFrameProcessed));
        ProcessIntervalFrames = processIntervalFrames;

        InitializeProfile(sourceProfile);

        _block = new CustomProcessingBlock(ProcessFrame);
        _block.Start(OnBlockOutput);
    }

    /// <summary>
    /// 元の ScriptableObject アセットを汚さないようランタイム用インスタンスを複製する
    /// </summary>
    private void InitializeProfile(RsProcessingProfile sourceProfile)
    {
        if (sourceProfile != null)
        {
            _runtimeProfile = UnityEngine.Object.Instantiate(sourceProfile);
            _instantiatedObjects.Add(_runtimeProfile);

            if (_runtimeProfile._processingBlocks == null)
            {
                _runtimeProfile._processingBlocks = new List<RsProcessingBlock>();
            }

            for (int i = 0; i < _runtimeProfile._processingBlocks.Count; i++)
            {
                var block = _runtimeProfile._processingBlocks[i];
                if (block != null)
                {
                    var clonedBlock = UnityEngine.Object.Instantiate(block);
                    _runtimeProfile._processingBlocks[i] = clonedBlock;
                    _instantiatedObjects.Add(clonedBlock);
                }
            }
        }
        else
        {
            _runtimeProfile = ScriptableObject.CreateInstance<RsProcessingProfile>();
            _runtimeProfile._processingBlocks = new List<RsProcessingBlock>();
            _instantiatedObjects.Add(_runtimeProfile);
        }
    }

    /// <summary>
    /// 指定型の最初の処理ブロックを取得する
    /// </summary>
    public T GetFirstBlock<T>() where T : RsProcessingBlock
    {
        if (_runtimeProfile == null || _runtimeProfile._processingBlocks == null)
            return null;

        foreach (var block in _runtimeProfile._processingBlocks)
        {
            if (block is T typedBlock)
                return typedBlock;
        }
        return null;
    }

    /// <summary>
    /// ストリーミング開始時にキャリブレーション情報を更新し、処理を有効化する
    /// </summary>
    public void StartStreaming(PipelineProfile activeProfile)
    {
        if (_isDisposed)
            return;

        if (activeProfile != null)
        {
            _calibration = new RsDepthToColorCalibration(activeProfile);

            if (_runtimeProfile != null && _runtimeProfile._processingBlocks != null)
            {
                foreach (var pb in _runtimeProfile._processingBlocks)
                {
                    if (pb is RsIntegratedPointCloud integratedPointCloud)
                    {
                        integratedPointCloud.SetCalibration(_calibration);
                    }
                }
            }
        }

        _isRunning = true;
    }

    /// <summary>
    /// ストリーミング停止時に処理を無効化する
    /// </summary>
    public void StopStreaming()
    {
        _isRunning = false;
    }

    /// <summary>
    /// 入力フレームを CustomProcessingBlock へ投入する
    /// </summary>
    public void EnqueueFrame(Frame frame)
    {
        if (_isDisposed || !_isRunning || _block == null)
            return;

        _block.Process(frame);
    }

    private void OnBlockOutput(Frame frame)
    {
        _onFrameProcessed?.Invoke(frame);
    }

    /// <summary>
    /// 設定した間隔でフレームをスキップしつつ、有効な加工ブロックを順番に通す
    /// </summary>
    internal void ProcessFrame(Frame frame, Action<Frame> output)
    {
        _frameCounter++;

        // 間引き処理：指定フレーム間隔に一致しない場合はスキップ
        if (_frameCounter % _processIntervalFrames != 0)
            return;

        try
        {
            if (!_isRunning || _isDisposed)
                return;

            Frame currentFrame = frame;

            if (_runtimeProfile != null && _runtimeProfile._processingBlocks != null)
            {
                var filters = _runtimeProfile._processingBlocks;

                for (int i = 0; i < filters.Count; i++)
                {
                    var pb = filters[i];
                    if (pb == null || !pb.Enabled)
                        continue;

                    Frame processed = pb.Process(currentFrame, default(FrameSource));

                    // 新しいフレームが返された場合、元フレーム以外の中間フレームを解放する
                    if (processed != currentFrame)
                    {
                        if (currentFrame != frame)
                        {
                            currentFrame.Dispose();
                        }
                        currentFrame = processed;
                    }
                }
            }

            output(currentFrame);

            // 入力された元のフレームとは異なる新規フレームが生成された場合、最終解放する
            if (currentFrame != frame)
            {
                currentFrame.Dispose();
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _isRunning = false;

        if (_block != null)
        {
            _block.Dispose();
            _block = null;
        }

        for (int i = 0; i < _instantiatedObjects.Count; i++)
        {
            if (_instantiatedObjects[i] != null)
            {
                UnityEngine.Object.Destroy(_instantiatedObjects[i]);
            }
        }
        _instantiatedObjects.Clear();
        _runtimeProfile = null;
        _calibration = null;
    }
}

