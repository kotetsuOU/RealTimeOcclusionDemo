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
            public float tAve;
            public float tLoc;
        }

        /// <summary>
        /// 現在有効な動的最適化パラメータ。設定されている場合はデフォルトの固定値の代わりにこれが使用されます。
        /// </summary>
        public static SICESI_OptimizationResult ActiveOptimizedParameters { get; set; } = null;

        /// <summary>
        /// ベンチマーク評価対象となる 5 手法の一覧設定を取得します。
        /// 引数に pipeline が渡された場合、PCDOcclusionPipelineController (Inspector) で設定された値
        /// (T_ave, T_loc, 各モードの閾値 R/L, カスタムHEX等) を最優先で尊重して各手法のパラメータを構成します。
        /// </summary>
        public static List<MethodConfig> GetEvaluationMethods(PCDOcclusionPipelineController pipeline = null)
        {
            EnsureOptimizedParametersLoaded();

            // 1. T_ave と T_loc: pipeline (Inspector) の設定値を最優先
            float optTAve = (pipeline != null && pipeline.T_ave > 0f)
                ? pipeline.T_ave
                : ((ActiveOptimizedParameters != null && ActiveOptimizedParameters.averageMeanIoU > 0f && ActiveOptimizedParameters.averageBestTAve > 0f)
                    ? ActiveOptimizedParameters.averageBestTAve
                    : 0.5f);

            float optTLoc = (pipeline != null && pipeline.T_loc > 0f)
                ? pipeline.T_loc
                : 0.1f;

            // 2. Mode 8 閾値 R: pipeline (Inspector) の設定値を最優先
            int r8 = (pipeline != null && pipeline.minOccludedSectorsMode8 > 0)
                ? pipeline.minOccludedSectorsMode8
                : (ActiveOptimizedParameters != null ? ActiveOptimizedParameters.mode8ThresholdR : 7);

            // 3. Mode 20 閾値 R, L: pipeline (Inspector) の設定値を最優先
            int r20 = (pipeline != null && pipeline.minOccludedSectorsMode20 > 0)
                ? pipeline.minOccludedSectorsMode20
                : (ActiveOptimizedParameters != null ? ActiveOptimizedParameters.mode20ThresholdR : 3);

            int l20 = (pipeline != null && pipeline.maxConsecutiveEmptySectors >= 0)
                ? pipeline.maxConsecutiveEmptySectors
                : (ActiveOptimizedParameters != null ? ActiveOptimizedParameters.mode20MaxConsecutiveEmptyL : 2);

            // 4. Mode 36 / 256 の HEX: それぞれの最適解または最新最適化パラメータを厳密に割り当て
            string hex36 = (ActiveOptimizedParameters != null && !string.IsNullOrEmpty(ActiveOptimizedParameters.mode36Hex))
                ? ActiveOptimizedParameters.mode36Hex
                : PCDOcclusionPipelineController.DEFAULT_HEX_36;

            string hex256 = (ActiveOptimizedParameters != null && !string.IsNullOrEmpty(ActiveOptimizedParameters.mode256Hex))
                ? ActiveOptimizedParameters.mode256Hex
                : PCDOcclusionPipelineController.DEFAULT_HEX_256;

            // Inspector に手動入力されたカスタムHEXがある場合の優先適用
            if (pipeline != null && !string.IsNullOrEmpty(pipeline.customPatternLutHex))
            {
                if (pipeline.evaluationMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.RotationLUT36)
                {
                    hex36 = pipeline.customPatternLutHex;
                }
                else if (pipeline.evaluationMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.PatternLUT256)
                {
                    // 誤って36用のHEXが入っている場合を除外して適用
                    if (pipeline.customPatternLutHex != PCDOcclusionPipelineController.DEFAULT_HEX_36)
                    {
                        hex256 = pipeline.customPatternLutHex;
                    }
                }
            }

            return new List<MethodConfig>
            {
                new MethodConfig
                {
                    methodName = "Mode_Average (Baseline)",
                    mode = PCDRendererFeature.PCD_OcclusionEvaluationMode.Average,
                    minSectors = 1,
                    maxConsecutiveZeros = 8,
                    customLutHex = "",
                    tAve = optTAve,
                    tLoc = optTLoc
                },
                new MethodConfig
                {
                    methodName = $"Mode_8 (SectorThreshold_R{r8})",
                    mode = PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold,
                    minSectors = r8,
                    maxConsecutiveZeros = 8,
                    customLutHex = "",
                    tAve = optTAve,
                    tLoc = optTLoc
                },
                new MethodConfig
                {
                    methodName = $"Mode_20 (Consecutive_R{r20}_L{l20})",
                    mode = PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorConsecutiveZeros,
                    minSectors = r20,
                    maxConsecutiveZeros = l20,
                    customLutHex = "",
                    tAve = optTAve,
                    tLoc = optTLoc
                },
                new MethodConfig
                {
                    methodName = "Mode_36 (RotationLUT36)",
                    mode = PCDRendererFeature.PCD_OcclusionEvaluationMode.RotationLUT36,
                    minSectors = 1,
                    maxConsecutiveZeros = 8,
                    customLutHex = hex36,
                    tAve = optTAve,
                    tLoc = optTLoc
                },
                new MethodConfig
                {
                    methodName = "Mode_256 (PatternLUT256)",
                    mode = PCDRendererFeature.PCD_OcclusionEvaluationMode.PatternLUT256,
                    minSectors = 1,
                    maxConsecutiveZeros = 8,
                    customLutHex = hex256,
                    tAve = optTAve,
                    tLoc = optTLoc
                }
            };
        }

        /// <summary>
        /// 指定された評価手法の設定をパイプラインコントローラーへ適用します。
        /// 手法に応じた評価モード、判定パラメータに加え、T_ave と T_loc を確実に固定値で反映します。
        /// 評価対象外の手法のパラメータ (minOccludedSectorsMode20 等) を誤って上書き汚染しないよう厳格に分離します。
        /// </summary>
        public static void ApplyMethodSettings(PCDOcclusionPipelineController pipeline, MethodConfig method)
        {
            if (pipeline == null) return;

            pipeline.evaluationMode = method.mode;
            pipeline.T_ave = method.tAve;
            pipeline.T_loc = method.tLoc;
            pipeline.occlusionThreshold = (method.mode == PCDRendererFeature.PCD_OcclusionEvaluationMode.Average) ? method.tAve : method.tLoc;

            if (method.mode == PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold)
            {
                pipeline.minOccludedSectorsMode8 = method.minSectors;
            }
            else if (method.mode == PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorConsecutiveZeros)
            {
                pipeline.minOccludedSectorsMode20 = method.minSectors;
                pipeline.maxConsecutiveEmptySectors = method.maxConsecutiveZeros;
            }
            else if (method.mode == PCDRendererFeature.PCD_OcclusionEvaluationMode.RotationLUT36 ||
                     method.mode == PCDRendererFeature.PCD_OcclusionEvaluationMode.PatternLUT256)
            {
                if (!string.IsNullOrEmpty(method.customLutHex))
                {
                    pipeline.customPatternLutHex = method.customLutHex;
                }
            }
        }

        /// <summary>
        /// ドメインリロード等で ActiveOptimizedParameters が未設定の場合、
        /// 最新の最適化サマリーCSV (common_rules_summary.csv) から自動復元を試みます。
        /// </summary>
        public static void EnsureOptimizedParametersLoaded()
        {
            if (ActiveOptimizedParameters != null) return;

            string userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            string[] searchPaths = new string[]
            {
                System.IO.Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExperiment", "RuleOptimizationResults", "common_rules_summary.csv"),
                System.IO.Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExperiment", "AdditionalExperiments", "Presets", "Optimization", "RuleOptimizationResults", "common_rules_summary.csv"),
                System.IO.Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExperiment", "AdditionalExperiments", "Presets", "Optimization", "Additional", "RuleOptimizationResults", "common_rules_summary.csv")
            };

            foreach (var p in searchPaths)
            {
                if (System.IO.File.Exists(p) && SICESI_OptimizationResult.TryLoadFromCsv(p, SICESI_OptimizationResult.ObjectiveType.MeanIoUMaximized, out var res))
                {
                    ActiveOptimizedParameters = res;
                    UnityEngine.Debug.Log($"[SICESI_BenchmarkMethodRegistry] 最新の最適化パラメータを自動ロードしました: {p}");
                    break;
                }
            }
        }
    }
}

