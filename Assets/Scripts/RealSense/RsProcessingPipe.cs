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

/// <summary>
/// RealSenseカメラ（Source）から取得したフレームデータを、
/// 指定した複数の処理ブロック（RsProcessingBlock）に順次通し、
/// パイプラインとして最終的な加工済みフレームを提供するコンポーネント。
/// </summary>
[Serializable]
public class RsProcessingPipe : RsFrameProvider
{
    [Tooltip("フレームの元となるプロバイダー（カメラ・デバイスコントローラー本体など）")]
    public RsFrameProvider Source;

    [Tooltip("適用するプロセッシングブロックが定義されたプロファイル（ScriptableObject）")]
    public RsProcessingProfile profile;

    public override event Action<PipelineProfile> OnStart;
    public override event Action OnStop;
    public override event Action<Frame> OnNewSample;

    // RealSense SDK標準のCustomProcessingBlockを使用してフレーム送受信をラップする
    private CustomProcessingBlock _block;
    private RsDepthToColorCalibration _calibration;
    private int _frameCounter = 0;

    [Tooltip("処理を実行する間隔（フレーム数）。1の場合は毎フレーム処理を行うが、2以上の場合は間引いて（スキップして）パフォーマンスを向上させる。")]
    [SerializeField] private int _processIntervalFrames = 1;

    /// <summary>
    /// 現在適用されている処理ブロックの読み取り専用リスト
    /// </summary>
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

    /// <summary>
    /// 各フレームごとに実行されるメイン処理。設定した間隔でフレームをスキップしつつ、
    /// 有効な加工ブロック（フィルタなど）を順番に通し、最終結果を output アクションへ送る。
    /// </summary>
    internal void ProcessFrame(Frame frame, Action<Frame> output)
    {
        _frameCounter++;

        // 間引き処理：指定フレーム間隔に一致しない場合はスキップ
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

                    // ブロックが無効なら処理をスキップ
                    if (!pb.Enabled)
                        continue;

                    // ブロック固有の処理（ノイズ除去や点群生成など）を実行
                    Frame processed = pb.Process(f, default(FrameSource));

                    // 新しいフレームが返された場合、旧フレームを解放（元のフレーム以外の場合）
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

            // 最終的に修飾されたフレームを出力へ引き渡す
            output(f);

            // 入力された元のフレームオブジェクトとは異なる新しいメモリのフレームが生成された場合、最終解放
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