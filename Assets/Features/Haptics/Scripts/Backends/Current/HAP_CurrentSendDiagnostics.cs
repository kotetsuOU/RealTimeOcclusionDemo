#if AUTD3_SDK_CURRENT
using System.Diagnostics;
using System.Threading;
using Core.Logging;
using Features.Haptics.Debug;

#nullable enable

namespace Features.Haptics.Backends.Current
{
    /// <summary>
    /// Current SDK (autd3-sdk v0.14.1) の送信頻度スロットリング、ロック保持監視、
    /// および 1 秒間隔の送信統計ログ集計を担当する診断クラス。
    /// </summary>
    public sealed class HAP_CurrentSendDiagnostics
    {
        private long _lastStatsTimestamp;
        private long _lastFociSendTimestamp;
        private int _sentCountWindow;
        private int _skippedLockCountWindow;
        private int _throttledCountWindow;
        private int _errCountWindow;
        private volatile string _lastSendError = "";
        private double _lastSendDurationMs;
        private volatile string _lockHolder = "none";
        private long _lockAcquiredTimestamp;

        /// <summary>
        /// 送信ロックの取得元を記録します。
        /// </summary>
        public void MarkLockAcquired(string holder)
        {
            _lockHolder = holder;
            Volatile.Write(ref _lockAcquiredTimestamp, Stopwatch.GetTimestamp());
        }

        /// <summary>
        /// 送信ロックの解放を記録します。
        /// </summary>
        public void MarkLockReleased()
        {
            _lockHolder = "none";
            Volatile.Write(ref _lockAcquiredTimestamp, 0);
        }

        /// <summary>
        /// 非同期送信の最短送信間隔（スロットリング）を確認し、送信可能であればタイムスタンプを更新します。
        /// </summary>
        public bool TryConsumeSendSlot(double minIntervalMs)
        {
            long nowTicks = Stopwatch.GetTimestamp();
            double elapsedMs = _lastFociSendTimestamp != 0
                ? (double)(nowTicks - _lastFociSendTimestamp) * 1000.0 / Stopwatch.Frequency
                : minIntervalMs;

            if (elapsedMs < minIntervalMs)
            {
                Interlocked.Increment(ref _throttledCountWindow);
                return false;
            }

            _lastFociSendTimestamp = nowTicks;
            return true;
        }

        /// <summary>
        /// ロック競合によるスキップをカウントします。
        /// </summary>
        public void RecordSkippedLock() => Interlocked.Increment(ref _skippedLockCountWindow);

        /// <summary>
        /// 送信成功をカウントします。
        /// </summary>
        public void RecordSent() => Interlocked.Increment(ref _sentCountWindow);

        /// <summary>
        /// 送信エラーをカウントし、直近エラーメッセージを保持します。
        /// </summary>
        public void RecordError(string message)
        {
            Interlocked.Increment(ref _errCountWindow);
            _lastSendError = message;
        }

        /// <summary>
        /// 直近の送信所要時間 (ms) を記録します。
        /// </summary>
        public void SetLastSendDurationMs(double durationMs) => _lastSendDurationMs = durationMs;

        /// <summary>
        /// 1 秒間隔で送信統計サマリーを AppLogger へ出力します。
        /// </summary>
        public void RecordSendStatsIfNeeded(bool synchronousSend, int clusterCount, int deviceCount)
        {
            if (!AppLogger.IsEnabled(null, HAP_LogTriggers.TagLinkService)) return;

            long now = Stopwatch.GetTimestamp();
            if (_lastStatsTimestamp == 0)
            {
                _lastStatsTimestamp = now;
                return;
            }

            double elapsedSec = (double)(now - _lastStatsTimestamp) / Stopwatch.Frequency;
            if (elapsedSec < 1.0) return;

            int sent = Interlocked.Exchange(ref _sentCountWindow, 0);
            int skipped = Interlocked.Exchange(ref _skippedLockCountWindow, 0);
            int throttled = Interlocked.Exchange(ref _throttledCountWindow, 0);
            int errs = Interlocked.Exchange(ref _errCountWindow, 0);
            string lastErr = Interlocked.Exchange(ref _lastSendError, "");
            _lastStatsTimestamp = now;

            long acquiredAt = Volatile.Read(ref _lockAcquiredTimestamp);
            double heldMs = acquiredAt != 0 ? (double)(now - acquiredAt) * 1000.0 / Stopwatch.Frequency : 0.0;

            string errSuffix = string.IsNullOrEmpty(lastErr) ? "" : $", lastErr={lastErr}";
            AppLogger.Log(null, HAP_LogTriggers.TagLinkService,
                $"[CurrentBackend] SendStats ({elapsedSec:F2}s): sent={sent}, throttled={throttled}, skippedLock={skipped}, " +
                $"err={errs}, sync={synchronousSend}, lastSendMs={_lastSendDurationMs:F2}, " +
                $"lockHolder={_lockHolder}, heldMs={heldMs:F1}, " +
                $"clusters={clusterCount}, devices={deviceCount}{errSuffix}");
        }
    }
}
#endif

