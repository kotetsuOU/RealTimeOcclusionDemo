using Intel.RealSense;
using System;
using UnityEngine;

/// <summary>
/// RealSenseのフレーム処理をカスタマイズするためのブロック。
/// C#のデリゲート（コールバック）を用いてフレームに対する任意の処理を割り込み実行できる。
/// </summary>
public class CustomProcessingBlock : IDisposable
{
    /// <summary>
    /// フレーム処理を実行するデリゲートの定義。
    /// 入力フレームと、処理結果のフレームを出力するためのコールバックを受け取る。
    /// </summary>
    public delegate void FrameProcessor(Frame frame, Action<Frame> output);

    private readonly FrameProcessor _userProcessor;
    private Action<Frame> _onFrameCallback;
    private bool _disposed = false;

    /// <summary>
    /// カスタムプロセッシングブロックを初期化する
    /// </summary>
    /// <param name="processor">フレームを受け取って処理するコールバック関数</param>
    public CustomProcessingBlock(FrameProcessor processor)
    {
        _userProcessor = processor;
    }

    /// <summary>
    /// 処理されたフレームを受け取るためのコールバックを登録して開始する
    /// </summary>
    /// <param name="onFrame">処理が終わったフレームを受け取るアクション</param>
    public void Start(Action<Frame> onFrame)
    {
        _onFrameCallback = onFrame;
    }

    /// <summary>
    /// 入力されたフレームに対してカスタムの実行処理（FrameProcessor）を非同期または同期で適用する
    /// </summary>
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

    /// <summary>
    /// 処理が完了したフレームを最終的にコールバックへ送るメソッド
    /// </summary>
    private void OutputFrame(Frame frame)
    {
        if (_disposed || _onFrameCallback == null) return;
        _onFrameCallback.Invoke(frame);
    }

    /// <summary>
    /// ブロックの使用を終了してリソースやコールバック参照を解放する
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _onFrameCallback = null;
    }
}