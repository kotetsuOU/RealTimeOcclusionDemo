using UnityEngine;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// SICE SI 評価実験において、各種スイープ・スナップショットの実行前バリデーション
    /// （二重実行防止、必要コンポーネントの null チェック）および Coroutine の起動を統括するディスパッチャクラス。
    /// </summary>
    public class SICESI_EvaluationDispatcher
    {
        private readonly SICESI_StereoEvaluationController _controller;

        public SICESI_EvaluationDispatcher(SICESI_StereoEvaluationController controller)
        {
            _controller = controller;
        }

        public void CaptureGroundTruth()
        {
            if (_controller.isCapturing) return;
            _controller.StartCoroutine(_controller.SnapshotRunner.CaptureGroundTruthRoutine());
        }

        public void CaptureCurrentCondition(string subFolderName = "")
        {
            if (_controller.isCapturing) return;
            _controller.StartCoroutine(_controller.SnapshotRunner.CaptureCurrentRoutine(subFolderName));
        }

        public void RunDensitySweep()
        {
            if (_controller.isCapturing) return;
            if (_controller.dummyPointCloudProvider == null)
            {
                AppLogger.LogError(_controller, "[SICESI] RsDummyPointCloudProvider が設定されていません。");
                return;
            }
            _controller.StartCoroutine(_controller.StereoSweepRunner.DensitySweepRoutine());
        }

        public void RunSectorSweep()
        {
            if (_controller.isCapturing) return;
            if (_controller.occlusionPipelineController == null)
            {
                AppLogger.LogError(_controller, "[SICESI] PCDOcclusionPipelineController が設定されていません。");
                return;
            }
            _controller.StartCoroutine(_controller.StereoSweepRunner.SectorSweepRoutine());
        }

        public void RunConsecutiveSectorSweep()
        {
            if (_controller.isCapturing) return;
            if (_controller.occlusionPipelineController == null)
            {
                AppLogger.LogError(_controller, "[SICESI] PCDOcclusionPipelineController が設定されていません。");
                return;
            }
            _controller.StartCoroutine(_controller.StereoSweepRunner.ConsecutiveSectorSweepRoutine());
        }

        public void RunDensityOcclusionThresholdSweep()
        {
            if (_controller.isCapturing) return;
            _controller.StartCoroutine(_controller.StereoSweepRunner.DensityOcclusionThresholdSweepRoutine());
        }

        public void CaptureSceneOverview()
        {
            if (_controller.isCapturing) return;
            _controller.StartCoroutine(_controller.SnapshotRunner.CaptureSceneViewRoutine());
        }

        public void RunBatchReSweep(string parentDirectory = "")
        {
            if (_controller.isCapturing) return;
            if (string.IsNullOrEmpty(parentDirectory))
            {
                parentDirectory = System.IO.Path.Combine(_controller.outputDirectory, _controller.casesParentFolder);
            }
            _controller.StartCoroutine(_controller.BatchReSweepRunner.RunBatchReSweepRoutine(parentDirectory));
        }

        public void RunGeneralizationBenchmark()
        {
            if (_controller.isCapturing) return;
            _controller.StartCoroutine(_controller.GeneralizationRunner.RunGeneralizationBenchmarkRoutine());
        }

        public void RunPresetBenchmark(System.Collections.Generic.List<SICESI_PresetPoseManager.PresetEntry> presets)
        {
            if (_controller.isCapturing) return;
            _controller.StartCoroutine(_controller.GeneralizationRunner.RunBenchmarkFromPresetsRoutine(presets));
        }

        public void RunOptimizationPresets(System.Collections.Generic.List<SICESI_PresetPoseManager.PresetEntry> presets, bool autoRunPython = true)
        {
            if (_controller.isCapturing) return;
            _controller.StartCoroutine(_controller.OptimizationPresetRunner.RunOptimizationPresetsRoutine(presets, autoRunPython));
        }

        public void RunPythonOptimization(
            string datasetRoot = null,
            string outputDir = null,
            string pythonPath = "python",
            bool skipDiffMaps = true,
            bool forceRegenerate = false,
            SICESI_OptimizationResult.ObjectiveType objective = SICESI_OptimizationResult.ObjectiveType.MeanIoUMaximized,
            bool autoApplyToPipeline = true)
        {
            if (string.IsNullOrEmpty(datasetRoot))
            {
                if (!string.IsNullOrEmpty(_controller.outputDirectory) && !string.IsNullOrEmpty(_controller.casesParentFolder) &&
                    System.IO.Directory.Exists(System.IO.Path.Combine(_controller.outputDirectory, _controller.casesParentFolder)))
                {
                    datasetRoot = System.IO.Path.Combine(_controller.outputDirectory, _controller.casesParentFolder);
                }
                else
                {
                    datasetRoot = !string.IsNullOrEmpty(_controller.outputDirectory)
                        ? _controller.outputDirectory
                        : System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "Estimation", "SICESI_Dataset"));
                }
            }

            if (string.IsNullOrEmpty(outputDir))
            {
                outputDir = System.IO.Path.Combine(datasetRoot, "RuleOptimizationResults");
            }

            AppLogger.Log(_controller, $"[RunPythonOptimization] 最適化を実行します。\n  対象データセット: {datasetRoot}\n  結果出力先: {outputDir}", SICESI_StereoEvaluationController.TagCore);

            _controller.StartCoroutine(_controller.PythonOptimizationRunner.RunOptimizationRoutine(
                datasetRoot,
                outputDir,
                pythonPath,
                skipDiffMaps,
                forceRegenerate,
                objective,
                (success, result) =>
                {
                    if (success && autoApplyToPipeline && result != null)
                    {
                        SICESI_OptimizationApplier.ApplyAll(_controller.occlusionPipelineController, result);
                    }
                }));
        }
    }
}
