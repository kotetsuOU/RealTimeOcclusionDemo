using Intel.RealSense;
using System;
using System.Collections.Generic;
using UnityEngine;
using Core.Logging;

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
[AppLoggable("RealSense (Pipeline)")]
public class RsProcessingPipe : RsFrameProvider, IRsProcessIntervalConfigurable
{
    [Tooltip("フレームの元となるプロバイダー（カメラ・デバイスコントローラー本体など）")]
    public RsFrameProvider Source;

    [Tooltip("適用するプロセッシングブロックが定義されたプロファイル（ScriptableObject）")]
    public RsProcessingProfile profile;

    public override event Action<PipelineProfile> OnStart;
    public override event Action OnStop;
    public override event Action<Frame> OnNewSample;

    // 実際のパイプライン処理・ブロック複製・メモリ管理は RsProcessingRunner に委譲する
    private RsProcessingRunner _runner;
    private RsFrameProvider _subscribedSource;

    [Tooltip("処理を実行する間隔（フレーム数）。1の場合は毎フレーム処理を行うが、2以上の場合は間引いて（スキップして）パフォーマンスを向上させる。")]
    [SerializeField] private int _processIntervalFrames = 1;

    /// <summary>
    /// 現在適用されている処理ブロックの読み取り専用リスト
    /// </summary>
    public IReadOnlyList<RsProcessingBlock> RuntimeBlocks
        => _runner != null
            ? _runner.RuntimeBlocks
            : (profile != null && profile._processingBlocks != null
                ? profile._processingBlocks
                : (IReadOnlyList<RsProcessingBlock>)Array.Empty<RsProcessingBlock>());

    public T GetFirstBlock<T>() where T : RsProcessingBlock
    {
        if (_runner != null)
            return _runner.GetFirstBlock<T>();

        if (profile == null || profile._processingBlocks == null) return null;
        foreach (var b in profile._processingBlocks)
        {
            if (b is T t) return t;
        }
        return null;
    }

    private void Reset()
    {
        TryAutoAssignSource();
    }

    private void OnValidate()
    {
        if (_processIntervalFrames <= 0)
            _processIntervalFrames = 1;

        if (_runner != null)
            _runner.ProcessIntervalFrames = _processIntervalFrames;
    }

    void Awake()
    {
        if (_processIntervalFrames <= 0)
            _processIntervalFrames = 1;

        TryAutoAssignSource();

        _runner = new RsProcessingRunner(profile, _processIntervalFrames, OnFrame);
        // 既存コード（RsPointCloudInitializer 等）との互換性のため、複製済みランタイムプロファイルを保持する
        profile = _runner.RuntimeProfile;

        SubscribeSource();
    }

    private void Start()
    {
        // Awake 時点で既に Source がストリーミング開始していた場合のフォールバック
        if (!Streaming && _subscribedSource != null && _subscribedSource.Streaming)
        {
            OnSourceStart(_subscribedSource.ActiveProfile);
        }
    }

    private void TryAutoAssignSource()
    {
        if (Source != null)
            return;

        // 同一 GameObject 上の別 RsFrameProvider（主に RsDevice）を自動検出
        var providers = GetComponents<RsFrameProvider>();
        for (int i = 0; i < providers.Length; i++)
        {
            if (providers[i] != null && providers[i] != this)
            {
                Source = providers[i];
                break;
            }
        }
    }

    private void SubscribeSource()
    {
        if (Source == null || _subscribedSource == Source)
            return;

        UnsubscribeSource();
        _subscribedSource = Source;
        _subscribedSource.OnStart += OnSourceStart;
        _subscribedSource.OnStop += OnSourceStop;
    }

    private void UnsubscribeSource()
    {
        if (_subscribedSource == null)
            return;

        _subscribedSource.OnStart -= OnSourceStart;
        _subscribedSource.OnStop -= OnSourceStop;
        if (_runner != null)
        {
            _subscribedSource.OnNewSample -= _runner.EnqueueFrame;
        }
        _subscribedSource = null;
    }

    protected virtual void OnSourceStart(PipelineProfile activeProfile)
    {
        if (Streaming)
            return;

        if (_subscribedSource != null && _runner != null)
        {
            _subscribedSource.OnNewSample += _runner.EnqueueFrame;
        }

        ActiveProfile = activeProfile;
        _runner?.StartStreaming(activeProfile);

        Streaming = true;
        OnStart?.Invoke(activeProfile);
    }

    protected virtual void OnSourceStop()
    {
        if (!Streaming)
            return;

        if (_subscribedSource != null && _runner != null)
        {
            _subscribedSource.OnNewSample -= _runner.EnqueueFrame;
        }

        _runner?.StopStreaming();
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
        UnsubscribeSource();

        _runner?.Dispose();
        _runner = null;
        profile = null;
    }

    public void SetProcessIntervalFrames(int value)
    {
        _processIntervalFrames = value > 0 ? value : 1;
        if (_runner != null)
        {
            _runner.ProcessIntervalFrames = _processIntervalFrames;
        }
    }

    public int GetProcessIntervalFrames()
    {
        return _processIntervalFrames;
    }
}
