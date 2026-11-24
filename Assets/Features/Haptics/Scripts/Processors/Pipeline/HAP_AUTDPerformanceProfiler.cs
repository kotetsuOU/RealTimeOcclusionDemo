using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Debug;

#nullable enable

/// <summary>
/// AUTD3 のハプティクスパイプライン（FociGenerate → DeviceAllocate → Send）の
/// 処理時間を計測するプロファイラーです。
/// 
/// 計測手段:
/// - System.Diagnostics.Stopwatch による高精度時間計測
/// - Unity ProfilerMarker による Profiler Timeline 連携（別スレッドでも記録可能）
/// - Debug.Log による Console 出力
/// 
/// ※ matsubayashi 氏の確認により、GSPAT は CPU 処理であり、
///   _autd.Send() は Gain 計算について同期処理のため、
///   Stopwatch で正確な処理時間が計測できます。
/// </summary>
public class HAP_AUTDPerformanceProfiler
{
    // --- Profiler Markers（Unity Profiler Timeline に表示される） ---
    private static readonly ProfilerMarker s_MarkerMainThread   = new ProfilerMarker("HAP.Haptics.MainThreadPrecalc");
    private static readonly ProfilerMarker s_MarkerFociGenerate = new ProfilerMarker("HAP.Haptics.FociGenerate");
    private static readonly ProfilerMarker s_MarkerDeviceAlloc  = new ProfilerMarker("HAP.Haptics.DeviceAllocate");
    private static readonly ProfilerMarker s_MarkerSend         = new ProfilerMarker("HAP.Haptics.Send");

    // --- Stopwatch インスタンス ---
    private readonly Stopwatch _swTotal        = new Stopwatch();
    private readonly Stopwatch _swFociGenerate = new Stopwatch();
    private readonly Stopwatch _swDeviceAlloc  = new Stopwatch();
    private readonly Stopwatch _swSend         = new Stopwatch();

    // --- 結果格納（フレーム単位で外部から参照可能） ---
    /// <summary>直近の FociGenerate 処理時間 (ms)</summary>
    public double LastFociGenerateMs  { get; private set; }
    /// <summary>直近の DeviceAllocate 処理時間 (ms)</summary>
    public double LastDeviceAllocateMs { get; private set; }
    /// <summary>直近の Send 処理時間 (ms)</summary>
    public double LastSendMs          { get; private set; }
    /// <summary>直近の Total 処理時間 (ms)</summary>
    public double LastTotalMs         { get; private set; }

    // --- 有効/無効 ---
    private bool _enabled;
    /// <summary>計測の有効/無効。無効時はオーバーヘッドなし。</summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    // --- ログ出力制御 ---
    private bool _logEnabled = true;
    /// <summary>Debug.Log への出力の有効/無効</summary>
    public bool LogEnabled
    {
        get => _logEnabled;
        set => _logEnabled = value;
    }

    private int _logInterval = 60;
    /// <summary>Debug.Log を出力するフレーム間隔（デフォルト60フレームごと）</summary>
    public int LogInterval
    {
        get => _logInterval;
        set => _logInterval = Mathf.Max(1, value);
    }

    private int _frameCounter;

    // --- 統計情報 ---
    private double _totalAccumMs;
    private double _fociAccumMs;
    private double _allocAccumMs;
    private double _sendAccumMs;
    private int _sampleCount;

    /// <summary>統計サンプル数</summary>
    public int SampleCount => _sampleCount;
    /// <summary>平均 Total 処理時間 (ms)</summary>
    public double AvgTotalMs => _sampleCount > 0 ? _totalAccumMs / _sampleCount : 0;
    /// <summary>平均 FociGenerate 処理時間 (ms)</summary>
    public double AvgFociGenerateMs => _sampleCount > 0 ? _fociAccumMs / _sampleCount : 0;
    /// <summary>平均 DeviceAllocate 処理時間 (ms)</summary>
    public double AvgDeviceAllocateMs => _sampleCount > 0 ? _allocAccumMs / _sampleCount : 0;
    /// <summary>平均 Send 処理時間 (ms)</summary>
    public double AvgSendMs => _sampleCount > 0 ? _sendAccumMs / _sampleCount : 0;

    // =====================================================================
    // 計測 API
    // =====================================================================

    /// <summary>Total 計測開始（メインスレッド）</summary>
    public void BeginTotal()
    {
        if (!_enabled) return;
        _swTotal.Restart();
        s_MarkerMainThread.Begin();
    }

    /// <summary>MainThread部分の ProfilerMarker 終了</summary>
    public void EndMainThreadMarker()
    {
        if (!_enabled) return;
        s_MarkerMainThread.End();
    }

    /// <summary>Total 計測終了（バックグラウンドスレッドでSend完了後に呼ばれる）</summary>
    public void EndTotal(string label = "")
    {
        if (!_enabled) return;
        _swTotal.Stop();
        LastTotalMs = _swTotal.Elapsed.TotalMilliseconds;

        // 統計更新
        _totalAccumMs += LastTotalMs;
        _fociAccumMs  += LastFociGenerateMs;
        _allocAccumMs += LastDeviceAllocateMs;
        _sendAccumMs  += LastSendMs;
        _sampleCount++;

        // ログ出力
        if (_logEnabled)
        {
            _frameCounter++;
            if (_frameCounter >= _logInterval)
            {
                _frameCounter = 0;
                string prefix = string.IsNullOrEmpty(label) ? "" : $"{label} ";
                AppLogger.Log(null, HAP_LogTriggers.TagPerformanceProfiler,
                    $"{prefix}Total={LastTotalMs:F3}ms " +
                    $"(FociGen={LastFociGenerateMs:F3}ms, " +
                    $"DevAlloc={LastDeviceAllocateMs:F3}ms, " +
                    $"Send={LastSendMs:F3}ms) " +
                    $"| Avg({_sampleCount}): Total={AvgTotalMs:F3}ms " +
                    $"(FociGen={AvgFociGenerateMs:F3}ms, " +
                    $"DevAlloc={AvgDeviceAllocateMs:F3}ms, " +
                    $"Send={AvgSendMs:F3}ms)");
            }
        }
    }

    /// <summary>FociGenerate 計測開始</summary>
    public void BeginFociGenerate()
    {
        if (!_enabled) return;
        _swFociGenerate.Restart();
        s_MarkerFociGenerate.Begin();
    }

    /// <summary>FociGenerate 計測終了</summary>
    public void EndFociGenerate()
    {
        if (!_enabled) return;
        s_MarkerFociGenerate.End();
        _swFociGenerate.Stop();
        LastFociGenerateMs = _swFociGenerate.Elapsed.TotalMilliseconds;
    }

    /// <summary>DeviceAllocate 計測開始</summary>
    public void BeginDeviceAllocate()
    {
        if (!_enabled) return;
        _swDeviceAlloc.Restart();
        s_MarkerDeviceAlloc.Begin();
    }

    /// <summary>DeviceAllocate 計測終了</summary>
    public void EndDeviceAllocate()
    {
        if (!_enabled) return;
        s_MarkerDeviceAlloc.End();
        _swDeviceAlloc.Stop();
        LastDeviceAllocateMs = _swDeviceAlloc.Elapsed.TotalMilliseconds;
    }

    /// <summary>Send 計測開始</summary>
    public void BeginSend()
    {
        if (!_enabled) return;
        _swSend.Restart();
        s_MarkerSend.Begin();
    }

    /// <summary>Send 計測終了</summary>
    public void EndSend()
    {
        if (!_enabled) return;
        s_MarkerSend.End();
        _swSend.Stop();
        LastSendMs = _swSend.Elapsed.TotalMilliseconds;
    }

    /// <summary>統計情報をリセットします</summary>
    public void ResetStatistics()
    {
        _totalAccumMs = 0;
        _fociAccumMs = 0;
        _allocAccumMs = 0;
        _sendAccumMs = 0;
        _sampleCount = 0;
        _frameCounter = 0;
    }
}
