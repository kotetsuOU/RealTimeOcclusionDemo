using System.Collections.Generic;

namespace SICESI
{
    /// <summary>
    /// SICE SI ベンチマークで評価対象となる 5 手法
    /// （Mode_Average, Mode 8, Mode 20, Mode 36, Mode 256）の設定定義と
    /// パイプラインコントローラーへの適用を担当するクラス。
    /// </summary>
    public static class SICESI_BenchmarkMethodRegistry
    {
        public struct MethodConfig
        {
            public string methodName;
            public PCDRendererFeature.PCD_OcclusionEvaluationMode mode;
            public int minSectors;
            public int maxConsecutiveZeros;
            public string customLutHex;
        }

        /// <summary>
        /// 現在有効な動的最適化パラメータ。設定されている場合はデフォルトの固定値の代わりにこれが使用されます。
        /// </summary>
        public static SICESI_OptimizationResult ActiveOptimizedParameters { get; set; } = null;

        /// <summary>
        /// ベンチマーク評価対象となる 5 手法の一覧設定を取得します。
        /// 最適化パラメータが設定されている場合は、動的に最適解が反映されます。
        /// </summary>
        public static List<MethodConfig> GetEvaluationMethods()
        {
            int r8 = ActiveOptimizedParameters != null ? ActiveOptimizedParameters.mode8ThresholdR : 7;
            int r20 = ActiveOptimizedParameters != null ? ActiveOptimizedParameters.mode20ThresholdR : 4;
            int l20 = ActiveOptimizedParameters != null ? ActiveOptimizedParameters.mode20MaxConsecutiveEmptyL : 1;
            string hex36 = ActiveOptimizedParameters != null && !string.IsNullOrEmpty(ActiveOptimizedParameters.mode36Hex)
                ? ActiveOptimizedParameters.mode36Hex
                : PCDOcclusionPipelineController.DEFAULT_HEX_36;
            string hex256 = ActiveOptimizedParameters != null && !string.IsNullOrEmpty(ActiveOptimizedParameters.mode256Hex)
                ? ActiveOptimizedParameters.mode256Hex
                : PCDOcclusionPipelineController.DEFAULT_HEX_256;

            return new List<MethodConfig>
            {
                new MethodConfig
                {
                    methodName = "Mode_Average (Baseline)",
                    mode = PCDRendererFeature.PCD_OcclusionEvaluationMode.Average,
                    minSectors = 1,
                    maxConsecutiveZeros = 8,
                    customLutHex = ""
                },
                new MethodConfig
                {
                    methodName = $"Mode_8 (SectorThreshold_R{r8})",
                    mode = PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold,
                    minSectors = r8,
                    maxConsecutiveZeros = 8,
                    customLutHex = ""
                },
                new MethodConfig
                {
                    methodName = $"Mode_20 (Consecutive_R{r20}_L{l20})",
                    mode = PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorConsecutiveZeros,
                    minSectors = r20,
                    maxConsecutiveZeros = l20,
                    customLutHex = ""
                },
                new MethodConfig
                {
                    methodName = "Mode_36 (RotationLUT36)",
                    mode = PCDRendererFeature.PCD_OcclusionEvaluationMode.RotationLUT36,
                    minSectors = 1,
                    maxConsecutiveZeros = 8,
                    customLutHex = hex36
                },
                new MethodConfig
                {
                    methodName = "Mode_256 (PatternLUT256)",
                    mode = PCDRendererFeature.PCD_OcclusionEvaluationMode.PatternLUT256,
                    minSectors = 1,
                    maxConsecutiveZeros = 8,
                    customLutHex = hex256
                }
            };
        }

        /// <summary>
        /// 指定された評価手法の設定をパイプラインコントローラーへ適用します。
        /// </summary>
        public static void ApplyMethodSettings(PCDOcclusionPipelineController pipeline, MethodConfig method)
        {
            if (pipeline == null) return;

            pipeline.evaluationMode = method.mode;
            if (method.mode == PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold)
            {
                pipeline.minOccludedSectorsMode8 = method.minSectors;
            }
            else if (method.mode == PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorConsecutiveZeros)
            {
                pipeline.minOccludedSectorsMode20 = method.minSectors;
            }
            else
            {
                pipeline.minOccludedSectors = method.minSectors;
            }

            pipeline.maxConsecutiveEmptySectors = method.maxConsecutiveZeros;
            if (!string.IsNullOrEmpty(method.customLutHex))
            {
                pipeline.customPatternLutHex = method.customLutHex;
            }
        }
    }
}
