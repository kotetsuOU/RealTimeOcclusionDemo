using UnityEngine;

/// <summary>
/// 点群の合成やサンプリングのパフォーマンス統計、プロファイリングなどを扱う部分的クラス。
/// 非同期ロガーやGPUプロファイラーの状態管理を行い、デバッグ時のボトルネック特定に活用されます。
/// </summary>
public partial class RsGlobalPointCloudManager
{
    // 非同期でのPCA推移状況・パフォーマンスログなどの書き出しハンドラ
    private RsAsyncStatsLogger _asyncLogger;

    // GPUからの読み戻しや特定処理の所要時間をCSVログとして記録するプロファイラ
    private RsGpuProfiler _gpuProfiler;

    // パフォーマンス集計のために一秒ごとに計測した秒間呼び出し／ヒット等の記録用コンテナ
    private int _pcaCallsPerSec = 0;
    private int _pcaCacheHitsPerSec = 0;
    private int _pcaCacheMissesPerSec = 0;
    
    // 一秒ごとにリセットされるための経過カウンター
    private int _pcaCallsCounter = 0;
    private int _pcaCacheHitsCounter = 0;
    private int _pcaCacheMissesCounter = 0;

    // 最後に各カメラの利用に関する統計をリセットした時間（秒間管理用）
    private float _lastStatsResetTime = 0f;

    /// <summary>
    /// GPUプロファイラー機能が設定・変更された際に、
    /// 有効化していれば新規生成、無効化されていれば破棄（Dispose）を行います。
    /// 各フレームやインスペクターのOnValidate時に呼び出されます。
    /// </summary>
    private void ApplyGpuProfilerState()
    {
        if (_gpuProfilerEnabled)
        {
            if (_gpuProfiler == null)
            {
                _gpuProfiler = new RsGpuProfiler();
            }
        }
        else
        {
            // 無効化された場合、ファイルハンドルなどが開いていれば適切に閉じる処理を実行
            _gpuProfiler?.Dispose();
            _gpuProfiler = null;
        }
    }

    /// <summary>
    /// 実時間が1秒以上経過したかを毎フレーム判定し、
    /// 超過した場合は保持しているPCAキャッシュや呼び出しのカウントを
    /// PerSec（秒間当たり）の変数として確定・更新します。
    /// 更新後に次秒への計測に向けたリセットが行われます。
    /// </summary>
    private void UpdateDebugStats()
    {
        float currentTime = Time.realtimeSinceStartup;
        
        // 前回のリセット時間から1秒以上経過した場合にのみ推移（カウント）を確定する
        if (currentTime - _lastStatsResetTime >= 1f)
        {
            _pcaCallsPerSec = _pcaCallsCounter;
            _pcaCacheHitsPerSec = _pcaCacheHitsCounter;
            _pcaCacheMissesPerSec = _pcaCacheMissesCounter;
            
            _pcaCallsCounter = 0;
            _pcaCacheHitsCounter = 0;
            _pcaCacheMissesCounter = 0;
            _lastStatsResetTime = currentTime;
        }
    }

    /// <summary>
    /// UpdateDebugStats で計算された推移をはじめとする情報を
    /// 設定された非同期統計ロガーの出力インターフェースへと渡します。
    /// さらにすべての子レンダラーが計測したフィルタ系の負荷等もまとめてログに排出する機能です。
    /// </summary>
    private void LogDebugStats()
    {
        if (_asyncLogger != null)
        {
            // マネージャ全体としてのPCA性能状況を記録
            _asyncLogger.LogGlobalManagerStats(_pcaCallsPerSec, _pcaCacheHitsPerSec, _pcaCacheMissesPerSec);
            
            // 子として管理している各カメラ（レンダラー）固有のパフォーマンス情報を取得し、同じく記録
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                
                var stats = renderer.GetComputeStats();
                if (stats != null)
                {
                    _asyncLogger.LogComputeStats(
                        renderer.gameObject.name,
                        stats.FilterCallsPerSec,          // ComputeShaderの呼び出し回数
                        stats.CountReadbackSkippedPerSec, // 頂点数のGPUからCPUへの読み戻し(Readback)をスキップした回数
                        stats.SamplesReadbackSkippedPerSec); // サンプリングしたデータのGPUからCPUへの読み戻しをスキップした回数
                }
            }
        }
    }

    /// <summary>
    /// 全ての子レンダラー（各カメラ）に対して、パフォーマンス集計ログを一括で記録開始します。
    /// 主にエディタ上からのバッチコントロールのデバッグ処理として利用される機能です。
    /// </summary>
    /// <param name="append">既存のログファイルに追記するかどうか（falseの場合は上書き等）</param>
    public void StartAllPerformanceLogs(bool append = false)
    {
        ApplyToAllRenderers(r =>
        {
            r.appendLog = append;
            r.StartPerformanceLog();
        });
    }

    /// <summary>
    /// 全ての子レンダラー（各カメラ）に対して、計測中であるパフォーマンスログ記録を即座に行い停止させます。
    /// ファイルハンドルを安全に閉じるための命令です。
    /// </summary>
    public void StopAllPerformanceLogs()
    {
        ApplyToAllRenderers(r => r.StopPerformanceLog());
    }

    /// <summary>
    /// 現在、いずれか一つの子レンダラーにおいて、
    /// CSVへのパフォーマンス情報ロギング（計測処理）が実行中であるかを判定して取得します。
    /// エディタ上のUI表示更新などに利用されます。
    /// </summary>
    public bool IsAnyPerformanceLogging()
    {
        var first = GetFirstRenderer();
        return first != null && first.IsPerformanceLogging;
    }
}
