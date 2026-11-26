using System;
using System.Collections;
using System.Linq;
using System.Threading;
using Core.Logging;
using Intel.RealSense;
using UnityEngine;

/// <summary>
/// RealSense デバイスの起動リクエスト構築およびリトライ付き非同期起動コルーチンを提供する共通ヘルパー。
/// <see cref="RsDevice"/> と <see cref="RsRealSenseRenderer"/> の重複起動ロジックを統合します。
/// </summary>
internal static class RsPipelineStartHelper
{
    public static RsPipelineSession.StartRequest BuildStartRequest(
        RsConfiguration config,
        RsDevice.ProcessMode processMode,
        UnityEngine.Object logContext)
    {
        var mode = config.mode;
        string resolvedPath = null;

        if (mode == RsConfiguration.Mode.Playback)
        {
            resolvedPath = RsDevicePathResolver.ResolveAbsolutePath(config.PlaybackFile);
        }
        else if (mode == RsConfiguration.Mode.Record)
        {
            resolvedPath = RsDevicePathResolver.ResolveAbsolutePath(config.RecordPath);
            RsDevicePathResolver.EnsureRecordDirectory(resolvedPath);
            AppLogger.Log(logContext, $"Setup Recording => \nRaw Input: {config.RecordPath}\nResolved: {resolvedPath}");
        }

        return new RsPipelineSession.StartRequest
        {
            Mode = mode,
            SerialNumber = config.RequestedSerialNumber,
            ResolvedFilePath = resolvedPath,
            Profiles = config.Profiles != null ? config.Profiles.ToArray() : Array.Empty<RsVideoStreamRequest>(),
            UseMultithreadWorker = processMode == RsDevice.ProcessMode.Multithread
        };
    }

    public static IEnumerator StartWithRetry(
        MonoBehaviour owner,
        RsPipelineSession session,
        Func<RsPipelineSession.StartRequest> requestBuilder,
        int maxRetries,
        float retryIntervalSeconds,
        Action<PipelineProfile> onStarted,
        CancellationToken ct)
    {
        // シーンロード中の同期ブロックを避けるため1フレーム待ってから非同期起動を開始
        yield return null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            if (ct.IsCancellationRequested || !owner.isActiveAndEnabled || session == null)
                yield break;

            var request = requestBuilder();
            var startTask = session.StartBackgroundAsync(request, ct);

            while (!startTask.IsCompleted)
            {
                if (ct.IsCancellationRequested || !owner.isActiveAndEnabled)
                    yield break;
                yield return null;
            }

            if (ct.IsCancellationRequested || !owner.isActiveAndEnabled)
                yield break;

            var result = startTask.Result;
            if (result.Success)
            {
                onStarted?.Invoke(result.Profile);
                yield break;
            }

            if (result.Error != null)
            {
                AppLogger.LogWarning(owner, $"Pipeline start error: {result.Error.Message}");
            }

            if (attempt < maxRetries)
            {
                AppLogger.LogWarning(owner, $"Pipeline start failed (attempt {attempt}/{maxRetries}). Retrying in {retryIntervalSeconds}s...");
                yield return new WaitForSeconds(retryIntervalSeconds);
            }
            else
            {
                AppLogger.LogError(owner, $"Pipeline start failed after {maxRetries} attempts. Camera may not be connected.");
            }
        }
    }
}

