using System.Collections;

namespace SICESI
{
    /// <summary>
    /// SICE SI 評価実験において、8 セクター占有マスク、直接投影マスク、および GPU 実遮蔽マスクの
    /// スイープ取得・エクスポートを統括・委譲するコーディネータークラス。
    /// 各処理は SICESI_Sector8MaskSweepRunner および SICESI_SectorMaskDensityRunner に責務分離されています。
    /// </summary>
    public class SICESI_SectorMaskSweepRunner
    {
        private readonly SICESI_Sector8MaskSweepRunner _sector8Runner;
        private readonly SICESI_SectorMaskDensityRunner _densityRunner;

        public SICESI_Sector8MaskSweepRunner Sector8Runner => _sector8Runner;
        public SICESI_SectorMaskDensityRunner DensityRunner => _densityRunner;

        public SICESI_SectorMaskSweepRunner(SICESI_StereoEvaluationController controller, SICESI_SectorMaskCollector collector)
        {
            _sector8Runner = new SICESI_Sector8MaskSweepRunner(controller, collector);
            _densityRunner = new SICESI_SectorMaskDensityRunner(controller, collector);
        }

        /// <summary>
        /// 8セクター・ビットプレーン方式の二値マスク画面保存スイープを実行します。
        /// </summary>
        public IEnumerator Sector8MaskSweepRoutine() => _sector8Runner.RunRoutine();

        /// <summary>
        /// 占有セクターマスクの GPU 同期リードバック（AsyncGPUReadback）を用いた密度スイープ収集を実行します。
        /// </summary>
        public IEnumerator SectorMaskDensitySweepRoutine() => _densityRunner.RunRoutine();
    }
}
