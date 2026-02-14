using System;
using System.IO;
using UnityEngine;

public class RsGpuProfiler : IDisposable
{
    private readonly System.Diagnostics.Stopwatch _stopwatch;
    private readonly StreamWriter _writer;
    private readonly ComputeBuffer _argsBuffer;
    private readonly int[] _argsData = new int[] { 0, 1, 0, 0 };

    private bool _isRecording;
    private readonly string _filePath;

    public string FilePath => _filePath;

    public RsGpuProfiler()
    {
        string directory = Path.Combine(Application.persistentDataPath, "RsGpuProfile");
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[RsGpuProfiler] Failed to create directory: {e.Message}");
        }

        string fileName = $"RsGpuProfile_{DateTime.Now:yyyyMMdd_HHmmss_fff}.csv";
        _filePath = Path.Combine(directory, fileName);

        try
        {
            _writer = new StreamWriter(_filePath, false);
            _writer.AutoFlush = true;
            _writer.WriteLine("FrameID,InputCount,OutputCount,ExecutionTime(ms),Status");

            UnityEngine.Debug.Log($"[RsGpuProfiler] Recording started. File: {_filePath}");
            _isRecording = true;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[RsGpuProfiler] Failed to create file: {e.Message}");
            _writer = null;
            _isRecording = false;
        }

        _stopwatch = new System.Diagnostics.Stopwatch();
        _argsBuffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);
        _argsBuffer.SetData(_argsData);
    }

    public void BeginProfile()
    {
        if (!_isRecording) return;
        _stopwatch.Restart();
    }

    public void EndProfile(int frameId, int inputCount, ComputeBuffer outputBuffer)
    {
        if (!_isRecording || _writer == null) return;

        ComputeBuffer.CopyCount(outputBuffer, _argsBuffer, 0);

        _argsBuffer.GetData(_argsData);
        _stopwatch.Stop();

        int outputCount = _argsData[0];
        double elapsedMs = _stopwatch.Elapsed.TotalMilliseconds;
        string status = outputCount > 0 ? "OK" : "AllRejected";

        try
        {
            _writer.WriteLine($"{frameId},{inputCount},{outputCount},{elapsedMs:F4},{status}");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[RsGpuProfiler] Write failed: {e.Message}");
        }
    }

    public void Dispose()
    {
        _isRecording = false;

        if (_writer != null)
        {
            _writer.Flush();
            _writer.Close();
            _writer.Dispose();
            UnityEngine.Debug.Log("[RsGpuProfiler] Recording finished.");
        }

        _argsBuffer?.Release();
    }
}