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
    }
}
