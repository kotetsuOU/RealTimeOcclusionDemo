using System;
using Intel.RealSense;

/// <summary>
/// RealSense フレームストリームの開始・停止・新規フレーム通知を提供する共通インターフェース。
/// <see cref="RsFrameProvider"/> および統合コンポーネントから共通利用します。
/// </summary>
public interface IRsFrameSource
{
    bool Streaming { get; }
    PipelineProfile ActiveProfile { get; }

    event Action<PipelineProfile> OnStart;
    event Action OnStop;
    event Action<Frame> OnNewSample;
}

