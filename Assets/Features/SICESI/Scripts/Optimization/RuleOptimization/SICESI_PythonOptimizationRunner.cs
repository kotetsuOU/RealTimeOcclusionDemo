using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Core.Logging;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SICESI
{
    /// <summary>
    /// Python 最適化スクリプト (optimize_pattern_rules.py) をバックグラウンドプロセスとして起動・監視し、
    /// 完了後に算出された最適化結果 (SICESI_OptimizationResult) を読み込むランナークラス。
    /// </summary>
    [AppLoggable("SICESI (OptRunner)")]
    public class SICESI_PythonOptimizationRunner : IAppLoggable
    {
        public const string Tag = "OptRunner";

        private readonly MonoBehaviour _coroutineHost;

        public bool isRunning { get; private set; } = false;
        public string statusMessage { get; private set; } = "Ready";
        public float progress { get; private set; } = 0f;
        public SICESI_OptimizationResult lastResult { get; private set; } = null;

        public SICESI_PythonOptimizationRunner(MonoBehaviour coroutineHost)
        {
            _coroutineHost = coroutineHost;
        }

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            string label = "[SICESI] Python Optimization Runner";
            if (existingLabels.Contains(label)) return;
            group.entries.Add(new LogInstanceEntry
            {
                label = label,
                tag = Tag,
                target = _coroutineHost,
                enabled = true
            });
            existingLabels.Add(label);
        }

        /// <summary>
        /// Python 最適化プロセスを非同期実行し、結果を読み込むコルーチン。
        /// </summary>
        /// <param name="datasetRoot">探索対象のデータセットルートディレクトリ</param>
        /// <param name="outputDir">最適化結果出力先ディレクトリ (省略時は <datasetRoot>/Optimization/RuleOptimization)</param>
        /// <param name="pythonPath">Python 実行可能バイナリのパス (デフォルト: "python")</param>
        /// <param name="skipDiffMaps">diff_map 画像出力をスキップして高速実行するか</param>
        /// <param name="forceRegenerate">真値集計キャッシュを無視して強制再集計するか</param>
        /// <param name="objective">目的関数種別 (平均IoU最大化 または 相対低減率最大化)</param>
        /// <param name="onCompleted">完了時コールバック (成功フラグ, 最適化結果)</param>
        public IEnumerator RunOptimizationRoutine(
            string datasetRoot,
            string outputDir,
            string pythonPath = "python",
            bool skipDiffMaps = true,
            bool forceRegenerate = false,
            SICESI_OptimizationResult.ObjectiveType objective = SICESI_OptimizationResult.ObjectiveType.MeanIoUMaximized,
            Action<bool, SICESI_OptimizationResult> onCompleted = null)
        {
            if (isRunning)
            {
                AppLogger.LogWarning(_coroutineHost, "[SICESI_PythonOpt] 既に最適化プロセスが実行中です。", Tag);
                yield break;
            }

            if (string.IsNullOrEmpty(pythonPath)) pythonPath = "python";

            // Python スクリプトの相対・絶対パス解決
            string scriptPath = Path.Combine(Application.dataPath, "Features", "SICESI", "Python", "optimize_pattern_rules.py");
            if (!File.Exists(scriptPath))
            {
                AppLogger.LogError(_coroutineHost, $"[SICESI_PythonOpt] 最適化スクリプトが見つかりません: {scriptPath}", Tag);
                onCompleted?.Invoke(false, null);
                yield break;
            }

            if (string.IsNullOrEmpty(datasetRoot) || !Directory.Exists(datasetRoot))
            {
                // デフォルトデータセット探索
                string fallbackDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Estimation", "SICESI_Dataset"));
                if (Directory.Exists(fallbackDir))
                {
                    datasetRoot = fallbackDir;
                }
                else
                {
                    AppLogger.LogError(_coroutineHost, $"[SICESI_PythonOpt] 指定されたデータセットフォルダが存在しません: {datasetRoot}", Tag);
                    onCompleted?.Invoke(false, null);
                    yield break;
                }
            }

            if (string.IsNullOrEmpty(outputDir))
            {
                outputDir = Path.Combine(datasetRoot, "Optimization", "RuleOptimization");
            }

            Directory.CreateDirectory(outputDir);

            // 引数の構築
            var argsBuilder = new StringBuilder();
            argsBuilder.Append($"\"{scriptPath}\"");
            argsBuilder.Append($" -i \"{datasetRoot}\"");
            argsBuilder.Append($" -o \"{outputDir}\"");
            if (skipDiffMaps) argsBuilder.Append(" --skip-diff");
            if (forceRegenerate) argsBuilder.Append(" --force");

            string arguments = argsBuilder.ToString();

            isRunning = true;
            statusMessage = "Starting Python Optimization Process...";
            progress = 0.1f;
            lastResult = null;

            AppLogger.Log(_coroutineHost, $"=== [SICESI_PythonOpt] 最適化プロセスを開始します ===", Tag);
            AppLogger.Log(_coroutineHost, $"  Python: {pythonPath}", Tag);
            AppLogger.Log(_coroutineHost, $"  Script: {scriptPath}", Tag);
            AppLogger.Log(_coroutineHost, $"  Dataset: {datasetRoot}", Tag);
            AppLogger.Log(_coroutineHost, $"  Output: {outputDir}", Tag);

            Process process = null;
            var stdOutBuilder = new StringBuilder();
            var stdErrBuilder = new StringBuilder();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                process = new Process { StartInfo = psi };
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        stdOutBuilder.AppendLine(e.Data);
                        if (e.Data.Contains("[ステップ"))
                        {
                            statusMessage = e.Data.Trim();
                        }
                    }
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        stdErrBuilder.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                AppLogger.LogError(_coroutineHost, $"[SICESI_PythonOpt] プロセスの起動に失敗しました: {ex.Message}", Tag);
                isRunning = false;
                statusMessage = $"Failed to start process: {ex.Message}";
                onCompleted?.Invoke(false, null);
                yield break;
            }

            statusMessage = "Python Optimization Running (MILP / Local Search)...";

            // プロセス完了まで非ブロッキング待機
            while (!process.HasExited)
            {
                progress = Mathf.Min(progress + 0.02f, 0.9f);
                yield return new WaitForSeconds(0.2f);
            }

            int exitCode = process.ExitCode;
            process.Dispose();

            if (exitCode != 0)
            {
                AppLogger.LogError(_coroutineHost, $"[SICESI_PythonOpt] 最適化プロセスがエラー終了しました (ExitCode: {exitCode})。\n標準エラー:\n{stdErrBuilder}", Tag);
                isRunning = false;
                statusMessage = $"Optimization failed (ExitCode: {exitCode})";
                onCompleted?.Invoke(false, null);
                yield break;
            }

            AppLogger.Log(_coroutineHost, $"[SICESI_PythonOpt] 最適化プロセスが正常終了しました。結果を読み込みます。", Tag);
            statusMessage = "Loading Optimization Results...";
            progress = 0.95f;

            // 1. JSON ファイルからの読み込みを優先
            string jsonPath = Path.Combine(outputDir, "optimized_parameters.json");
            string csvPath = Path.Combine(outputDir, "common_rules_summary.csv");
            SICESI_OptimizationResult loadedResult = null;

            bool loadSuccess = SICESI_OptimizationResult.TryLoadFromJson(jsonPath, objective, out loadedResult);
            if (!loadSuccess)
            {
                AppLogger.LogWarning(_coroutineHost, $"JSON からの読み込みに失敗したため、CSV からフォールバックパースを試みます: {csvPath}", Tag);
                loadSuccess = SICESI_OptimizationResult.TryLoadFromCsv(csvPath, objective, out loadedResult);
            }

            if (loadSuccess && loadedResult != null)
            {
                lastResult = loadedResult;
                isRunning = false;
                progress = 1.0f;
                statusMessage = $"Optimization Completed! (Mode 256 IoU: {loadedResult.mode256MeanIoU:P2})";

                AppLogger.Log(_coroutineHost, $"=== [SICESI_PythonOpt] 最適化結果の読み込み完了 (出力先: {outputDir}) ===", Tag);
                AppLogger.Log(_coroutineHost, $"  [Mode 8]   {loadedResult.mode8RuleDesc} (閾値 R={loadedResult.mode8ThresholdR}, 平均IoU: {loadedResult.mode8MeanIoU:P2})", Tag);
                AppLogger.Log(_coroutineHost, $"  [Mode 20]  {loadedResult.mode20RuleDesc} (閾値 R={loadedResult.mode20ThresholdR}, L={loadedResult.mode20MaxConsecutiveEmptyL}, 平均IoU: {loadedResult.mode20MeanIoU:P2})", Tag);
                AppLogger.Log(_coroutineHost, $"  [Mode 36]  可視 {loadedResult.mode36VisibleClasses} クラス (平均IoU: {loadedResult.mode36MeanIoU:P2}, HEX: {loadedResult.mode36Hex.Substring(0, 16)}...)", Tag);
                AppLogger.Log(_coroutineHost, $"  [Mode 256] 可視 {loadedResult.mode256VisiblePatterns} パターン (平均IoU: {loadedResult.mode256MeanIoU:P2}, HEX: {loadedResult.mode256Hex.Substring(0, 16)}...)", Tag);

                onCompleted?.Invoke(true, loadedResult);
            }
            else
            {
                AppLogger.LogError(_coroutineHost, $"[SICESI_PythonOpt] 最適化結果ファイル (JSON/CSV) の読み込みに失敗しました: {outputDir}", Tag);
                isRunning = false;
                statusMessage = "Failed to parse result files.";
                onCompleted?.Invoke(false, null);
            }
        }
    }
}
