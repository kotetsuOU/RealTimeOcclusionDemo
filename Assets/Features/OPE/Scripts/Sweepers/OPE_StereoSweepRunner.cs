using System.Collections;

namespace OPE
{
    /// <summary>
    /// SICE SI 評価実験において、ステレオカメラ（左右眼）による各条件（点群密度、セクター数、連続非占有規則、オクルージョン閾値）の
    /// パラメータグリッドスイープ撮影シーケンスを統括・委譲するコーディネータークラス。
    /// 各スイープの実装は OPE_DensitySweepRunner, OPE_SectorSweepRunner,
    /// OPE_ConsecutiveSweepRunner, OPE_ThresholdSweepRunner に責務分離されています。
    /// </summary>
    public class OPE_StereoSweepRunner
    {
        private readonly OPE_DensitySweepRunner _densityRunner;
        private readonly OPE_SectorSweepRunner _sectorRunner;
        private readonly OPE_ConsecutiveSweepRunner _consecutiveRunner;
        private readonly OPE_ThresholdSweepRunner _thresholdRunner;

        public OPE_DensitySweepRunner DensityRunner => _densityRunner;
        public OPE_SectorSweepRunner SectorRunner => _sectorRunner;
        public OPE_ConsecutiveSweepRunner ConsecutiveRunner => _consecutiveRunner;
        public OPE_ThresholdSweepRunner ThresholdRunner => _thresholdRunner;

        public OPE_StereoSweepRunner(OPE_StereoEvaluationController controller, OPE_MaterialSwapper swapper)
        {
            _densityRunner = new OPE_DensitySweepRunner(controller, swapper);
            _sectorRunner = new OPE_SectorSweepRunner(controller, swapper);
            _consecutiveRunner = new OPE_ConsecutiveSweepRunner(controller, swapper);
            _thresholdRunner = new OPE_ThresholdSweepRunner(controller, swapper);
        }

        /// <summary>
        /// 点群密度スイープ撮影を実行します。
        /// </summary>
        public IEnumerator DensitySweepRoutine() => _densityRunner.RunRoutine();

        /// <summary>
        /// セクター閾値スイープ撮影（Averageモード比較含む）を実行します。
        /// </summary>
        public IEnumerator SectorSweepRoutine() => _sectorRunner.RunRoutine();

        /// <summary>
        /// 連続非占有セクター許容規則（20代表候補）のスイープ撮影を実行します。
        /// </summary>
        public IEnumerator ConsecutiveSectorSweepRoutine() => _consecutiveRunner.RunRoutine();

        /// <summary>
        /// 点群密度 × オクルージョン閾値のグリッドスイープ撮影を実行します。
        /// </summary>
        public IEnumerator DensityOcclusionThresholdSweepRoutine() => _thresholdRunner.RunRoutine();
    }
}
