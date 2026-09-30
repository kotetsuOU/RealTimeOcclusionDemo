using System.Collections;

namespace SICESI
{
    /// <summary>
    /// SICE SI 評価実験において、ステレオカメラ（左右眼）による各条件（点群密度、セクター数、連続非占有規則、オクルージョン閾値）の
    /// パラメータグリッドスイープ撮影シーケンスを統括・委譲するコーディネータークラス。
    /// 各スイープの実装は SICESI_DensitySweepRunner, SICESI_SectorSweepRunner,
    /// SICESI_ConsecutiveSweepRunner, SICESI_ThresholdSweepRunner に責務分離されています。
    /// </summary>
    public class SICESI_StereoSweepRunner
    {
        private readonly SICESI_DensitySweepRunner _densityRunner;
        private readonly SICESI_SectorSweepRunner _sectorRunner;
        private readonly SICESI_ConsecutiveSweepRunner _consecutiveRunner;
        private readonly SICESI_ThresholdSweepRunner _thresholdRunner;

        public SICESI_DensitySweepRunner DensityRunner => _densityRunner;
        public SICESI_SectorSweepRunner SectorRunner => _sectorRunner;
        public SICESI_ConsecutiveSweepRunner ConsecutiveRunner => _consecutiveRunner;
        public SICESI_ThresholdSweepRunner ThresholdRunner => _thresholdRunner;

        public SICESI_StereoSweepRunner(SICESI_StereoEvaluationController controller, SICESI_MaterialSwapper swapper)
        {
            _densityRunner = new SICESI_DensitySweepRunner(controller, swapper);
            _sectorRunner = new SICESI_SectorSweepRunner(controller, swapper);
            _consecutiveRunner = new SICESI_ConsecutiveSweepRunner(controller, swapper);
            _thresholdRunner = new SICESI_ThresholdSweepRunner(controller, swapper);
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
