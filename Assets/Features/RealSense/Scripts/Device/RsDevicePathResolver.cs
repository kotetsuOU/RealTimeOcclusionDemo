using System;
using System.IO;
using Core.Logging;
using UnityEngine;

/// <summary>
/// RealSense の再生/録画ファイルパスの解決および録画結果の検証を担う Pure C# ヘルパー。
/// </summary>
internal static class RsDevicePathResolver
{
    /// <summary>
    /// StreamingAssets またはプロジェクト相対パスを OS 上の絶対パスへ解決します。
    /// メインスレッド上（Application.streamingAssetsPath 参照可能時）で呼び出す必要があります。
    /// </summary>
    public static string ResolveAbsolutePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        path = path.Replace("\\", "/");

        if (path.StartsWith("Assets/StreamingAssets/", StringComparison.OrdinalIgnoreCase))
        {
            string subPath = path.Substring("Assets/StreamingAssets/".Length);
            return Path.Combine(Application.streamingAssetsPath, subPath).Replace("\\", "/");
        }
        if (path.StartsWith("StreamingAssets/", StringComparison.OrdinalIgnoreCase))
        {
            string subPath = path.Substring("StreamingAssets/".Length);
            return Path.Combine(Application.streamingAssetsPath, subPath).Replace("\\", "/");
        }

        if (!Path.IsPathRooted(path))
        {
            string projectRoot = AppPaths.ProjectRoot;
            return projectRoot + "/" + path;
        }

        return path;
    }

    /// <summary>
    /// 録画先の親ディレクトリが存在しない場合は作成します。
    /// </summary>
    public static void EnsureRecordDirectory(string resolvedFilePath)
    {
        if (string.IsNullOrEmpty(resolvedFilePath)) return;
        string recordDir = Path.GetDirectoryName(resolvedFilePath);
        if (!string.IsNullOrEmpty(recordDir) && !Directory.Exists(recordDir))
        {
            Directory.CreateDirectory(recordDir);
        }
    }

    /// <summary>
    /// 録画停止後にディスク上の出力ファイル存在とサイズを検証してログ出力します。
    /// </summary>
    public static void VerifyRecordedFile(string rawRecordPath, int capturedFrames, UnityEngine.Object logContext)
    {
        string finalRecordPath = ResolveAbsolutePath(rawRecordPath);
        if (File.Exists(finalRecordPath))
        {
            var fileInfo = new FileInfo(finalRecordPath);
            AppLogger.Log(logContext, $"Recording Stopped. File successfully found on disk: {finalRecordPath}\nFrames captured: {capturedFrames}, File Size: {fileInfo.Length} bytes.");
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
        }
        else
        {
            AppLogger.LogError(logContext, $"File NOT found on disk! Path: {finalRecordPath}. Frames captured: {capturedFrames}. Check if RealSense devices are working properly.");
        }
    }
}

