using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

public class RsAsyncStatsLogger : IDisposable
{
    private readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
    private readonly Thread _writerThread;
    private readonly string _filePath;
    private volatile bool _isRunning = true;
    private readonly AutoResetEvent _signal = new AutoResetEvent(false);

    public bool IsEnabled { get; set; } = true;

    public RsAsyncStatsLogger(string fileName = "RsAsyncStats.log")
    {
        string directory = Path.Combine(Application.persistentDataPath, "RsLogs");
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _filePath = Path.Combine(directory, $"{timestamp}_{fileName}");

        _writerThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "RsAsyncStatsLogger"
        };
        _writerThread.Start();

        Enqueue($"=== RsAsyncStatsLogger Started: {DateTime.Now} ===");
        Enqueue($"FilePath: {_filePath}");
        Enqueue("Time,Source,FilterCalls,CountSkip,SamplesSkip,PcaCalls,CacheHit,CacheMiss");
    }

    public void LogComputeStats(string source, int filterCalls, int countSkip, int samplesSkip)
    {
        if (!IsEnabled) return;
        string line = $"{Time.realtimeSinceStartup:F2},{source},{filterCalls},{countSkip},{samplesSkip},,,";
        Enqueue(line);
    }

    public void LogGlobalManagerStats(int pcaCalls, int cacheHits, int cacheMisses)
    {
        if (!IsEnabled) return;
        string line = $"{Time.realtimeSinceStartup:F2},GlobalPCM,,,,{pcaCalls},{cacheHits},{cacheMisses}";
        Enqueue(line);
    }

    public void Log(string message)
    {
        if (!IsEnabled) return;
        Enqueue($"{Time.realtimeSinceStartup:F2},MSG,{message}");
    }

    private void Enqueue(string line)
    {
        _logQueue.Enqueue(line);
        _signal.Set();
    }

    private void WriterLoop()
    {
        var sb = new StringBuilder();

        while (_isRunning || !_logQueue.IsEmpty)
        {
            _signal.WaitOne(100);

            sb.Clear();
            while (_logQueue.TryDequeue(out string line))
            {
                sb.AppendLine(line);
            }

            if (sb.Length > 0)
            {
                try
                {
                    File.AppendAllText(_filePath, sb.ToString());
                }
                catch (Exception)
                {
                    // Silently ignore write errors
                }
            }
        }
    }

    public void Dispose()
    {
        IsEnabled = false;
        _isRunning = false;
        _signal.Set();

        if (_writerThread != null && _writerThread.IsAlive)
        {
            _writerThread.Join(1000);
        }

        _signal.Dispose();
    }

    public string GetLogFilePath() => _filePath;
}
