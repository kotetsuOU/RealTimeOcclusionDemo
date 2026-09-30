using System.Collections.Generic;
using RealSense.DummyPointCloud;

namespace SICESI
{
    /// <summary>
    /// SICE SI 評価実験において、点群密度単位の文字列フォーマットや
    /// 探索パラメータの整合性検証・ヘルパー関数を提供するユーティリティクラス。
    /// </summary>
    public static class SICESI_EvaluationParamsHelper
    {
        /// <summary>
        /// 点群物理密度の単位に対応するファイル名用サフィックスを取得します。
        /// </summary>
        public static string GetDensityUnitSuffix(PointDensityUnit densityUnit)
        {
            switch (densityUnit)
            {
                case PointDensityUnit.PointSpacingMm: return "mm";
                case PointDensityUnit.PointsPerCm2: return "pts_cm2";
                case PointDensityUnit.PointsPerMm2: return "pts_mm2";
                case PointDensityUnit.TotalPointCount: return "pts";
                default: return "";
            }
        }
    }
}
