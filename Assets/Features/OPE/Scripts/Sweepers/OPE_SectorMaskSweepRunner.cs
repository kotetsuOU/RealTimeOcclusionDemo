using System.Collections;

namespace OPE
{
    /// <summary>
    /// SICE SI 評価実験において、8 セクター占有マスク、直接投影マスク、および GPU 実遮蔽マスクの
    /// スイープ取得・エクスポートを統括・委譲するコーディネータークラス。
    /// 各処理は OPE_Sector8MaskSweepRunner および OPE_SectorMaskDensityRunner に責務分離されています。
    /// </summary>
    public class OPE_SectorMaskSweepRunner
    {
        private readonly OPE_Sector8MaskSweepRunner _sector8Runner;
        private readonly OPE_SectorMaskDensityRunner _densityRunner;

        public OPE_Sector8MaskSweepRunner Sector8Runner => _sector8Runner;
        public OPE_SectorMaskDensityRunner DensityRunner => _densityRunner;

        public OPE_SectorMaskSweepRunner(OPE_StereoEvaluationController controller, OPE_SectorMaskCollector collector)
        {
            _sector8Runner = new OPE_Sector8MaskSweepRunner(controller, collector);
            _densityRunner = new OPE_SectorMaskDensityRunner(controller, collector);
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
