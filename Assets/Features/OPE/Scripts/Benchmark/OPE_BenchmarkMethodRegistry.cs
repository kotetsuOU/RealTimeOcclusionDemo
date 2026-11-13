using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OPE
{
    /// <summary>
    /// SICE SI ベンチマークで評価対象となる 5 手法
    /// （Mode_Average, Mode 8, Mode 20, Mode 36, Mode 256）の設定定義と
    /// パイプラインコントローラーへの適用を担当するクラス。
    /// </summary>
    public static class OPE_BenchmarkMethodRegistry
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
            public bool useSectorSpecificThreshold;
            public float[] sectorThreshold_T_loc;
        }

        /// <summary>
        /// 現在有効な動的最適化パラメータ。設定されている場合はデフォルトの固定値の代わりにこれが使用されます。
        /// </summary>
        public static OPE_OptimizationResult ActiveOptimizedParameters { get; set; } = null;

        /// <summary>
        /// ベンチマーク評価対象となる 5 手法の一覧設定を取得します。
        /// 引数に pipeline が渡された場合、PCDOcclusionPipelineController (Inspector) で設定された値
        /// (T_ave, T_loc) を最優先し、手法固有の閾値 (R8, R20, L20, HEX) は最適化結果 (ActiveOptimizedParameters) を優先します。
        /// </summary>
        public static List<MethodConfig> GetEvaluationMethods(PCDOcclusionPipelineController pipeline = null)
        {
            EnsureOptimizedParametersLoaded();

            // 1. T_ave と T_loc, 個別閾値設定: pipeline (Inspector) の設定値を最優先
            float optTAve = (pipeline != null && pipeline.T_ave > 0f)
                ? pipeline.T_ave
                : ((ActiveOptimizedParameters != null && ActiveOptimizedParameters.averageMeanIoU > 0f && ActiveOptimizedParameters.averageBestTAve > 0f)
                    ? ActiveOptimizedParameters.averageBestTAve
                    : 0.24f);

            float optTLoc = (pipeline != null && pipeline.T_loc > 0f)
                ? pipeline.T_loc
                : ((ActiveOptimizedParameters != null && ActiveOptimizedParameters.localThresholdMeanIoU > 0f && ActiveOptimizedParameters.localThresholdBestTLoc > 0f)
                    ? ActiveOptimizedParameters.localThresholdBestTLoc
                    : optTAve);

            // セクター個別閾値 (特にSec5等) による外側輪郭過剰遮蔽を防ぐため、ベンチマーク比較では安定した共通 T_loc スカラーを標準とする
            bool useSector = false;
            float[] sectorTLoc = null;

            // 2. Mode 8 閾値 R: ActiveOptimizedParameters があれば優先、無ければ Inspector またはデフォルト(7)
            int r8 = (ActiveOptimizedParameters != null && ActiveOptimizedParameters.mode8ThresholdR > 0)
                ? ActiveOptimizedParameters.mode8ThresholdR
                : ((pipeline != null && pipeline.minOccludedSectorsMode8 > 0) ? pipeline.minOccludedSectorsMode8 : 7);

            // 3. Mode 20 閾値 R, L: ActiveOptimizedParameters があれば優先、無ければ Inspector またはデフォルト(3/2)
            int r20 = (ActiveOptimizedParameters != null && ActiveOptimizedParameters.mode20ThresholdR > 0)
                ? ActiveOptimizedParameters.mode20ThresholdR
                : ((pipeline != null && pipeline.minOccludedSectorsMode20 > 0) ? pipeline.minOccludedSectorsMode20 : 3);

            int l20 = (ActiveOptimizedParameters != null && ActiveOptimizedParameters.mode20MaxConsecutiveEmptyL >= 0)
                ? ActiveOptimizedParameters.mode20MaxConsecutiveEmptyL
                : ((pipeline != null && pipeline.maxConsecutiveEmptySectors >= 0) ? pipeline.maxConsecutiveEmptySectors : 2);

            // 4. Mode 36 / 256 の HEX: それぞれの最適解または最新最適化パラメータを厳密に割り当て
            string hex36 = (ActiveOptimizedParameters != null && !string.IsNullOrEmpty(ActiveOptimizedParameters.mode36Hex) && ActiveOptimizedParameters.mode36Hex.Length >= 64)
                ? ActiveOptimizedParameters.mode36Hex
                : PCDOcclusionPipelineController.DEFAULT_HEX_36;

            string hex256 = (ActiveOptimizedParameters != null && !string.IsNullOrEmpty(ActiveOptimizedParameters.mode256Hex) && ActiveOptimizedParameters.mode256Hex.Length >= 64)
                ? ActiveOptimizedParameters.mode256Hex
                : PCDOcclusionPipelineController.DEFAULT_HEX_256;

            // Inspector に手動入力されたカスタムHEXがある場合の優先適用
            if (pipeline != null && !string.IsNullOrEmpty(pipeline.customPatternLutHex) && pipeline.customPatternLutHex.Length >= 64)
            {
                if (pipeline.evaluationMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.RotationLUT36)
                {
                    hex36 = pipeline.customPatternLutHex;
                }
                else if (pipeline.evaluationMode == PCDRendererFeature.PCD_OcclusionEvaluationMode.PatternLUT256)
                {
                    if (pipeline.customPatternLutHex != PCDOcclusionPipelineController.DEFAULT_HEX_36)
                    {
                        hex256 = pipeline.customPatternLutHex;
                    }
                }
            }

            Core.Logging.AppLogger.Log("OPE", $"[BenchmarkMethodRegistry] 評価パラメータ確定: T_ave={optTAve:F2} (Pipeline={pipeline != null}, Pipeline.T_ave={pipeline?.T_ave:F2}), T_loc={(useSector ? "[8Dir-Indep]" : optTLoc.ToString("F2"))}, R8={r8}, R20={r20}/L20={l20}");

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
                    tLoc = optTLoc,
                    useSectorSpecificThreshold = false,
                    sectorThreshold_T_loc = null
                },
                new MethodConfig
                {
                    methodName = $"Mode_8 (SectorThreshold_R{r8})",
                    mode = PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold,
                    minSectors = r8,
                    maxConsecutiveZeros = 8,
                    customLutHex = "",
                    tAve = optTAve,
                    tLoc = optTLoc,
                    useSectorSpecificThreshold = useSector,
                    sectorThreshold_T_loc = sectorTLoc
                },
                new MethodConfig
                {
                    methodName = $"Mode_20 (Consecutive_R{r20}_L{l20})",
                    mode = PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorConsecutiveZeros,
                    minSectors = r20,
                    maxConsecutiveZeros = l20,
                    customLutHex = "",
                    tAve = optTAve,
                    tLoc = optTLoc,
                    useSectorSpecificThreshold = useSector,
                    sectorThreshold_T_loc = sectorTLoc
                },
                new MethodConfig
                {
                    methodName = "Mode_36 (RotationLUT36)",
                    mode = PCDRendererFeature.PCD_OcclusionEvaluationMode.RotationLUT36,
                    minSectors = 1,
                    maxConsecutiveZeros = 8,
                    customLutHex = hex36,
                    tAve = optTAve,
                    tLoc = optTLoc,
                    useSectorSpecificThreshold = useSector,
                    sectorThreshold_T_loc = sectorTLoc
                },
                new MethodConfig
                {
                    methodName = "Mode_256 (PatternLUT256)",
                    mode = PCDRendererFeature.PCD_OcclusionEvaluationMode.PatternLUT256,
                    minSectors = 1,
                    maxConsecutiveZeros = 8,
                    customLutHex = hex256,
                    tAve = optTAve,
                    tLoc = optTLoc,
                    useSectorSpecificThreshold = useSector,
                    sectorThreshold_T_loc = sectorTLoc
                }
            };
        }

        /// <summary>
        /// 指定された評価手法の設定をパイプラインコントローラーへ適用します。
        /// 手法に応じた評価モード、判定パラメータに加え、T_ave と T_loc を確実に固定値で反映します。
        /// currentDensity が指定されており、密度別最適化パラメータが存在する場合は自動的に適応します。
        /// </summary>
        public static void ApplyMethodSettings(PCDOcclusionPipelineController pipeline, MethodConfig method, float currentDensity = 0f)
        {
            if (pipeline == null) return;

            pipeline.evaluationMode = method.mode;
            pipeline.T_ave = method.tAve;
            pipeline.T_loc = method.tLoc;
            pipeline.occlusionThreshold = (method.mode == PCDRendererFeature.PCD_OcclusionEvaluationMode.Average) ? method.tAve : method.tLoc;
            pipeline.useSectorSpecificThreshold = method.useSectorSpecificThreshold;
            if (method.sectorThreshold_T_loc != null && method.sectorThreshold_T_loc.Length == 8)
            {
                pipeline.sectorThreshold_T_loc = (float[])method.sectorThreshold_T_loc.Clone();
            }

            int r8 = method.minSectors;
            int r20 = method.minSectors;
            int l20 = method.maxConsecutiveZeros;
            string hex = method.customLutHex;

            // 密度適応最適化パラメータの適用 (低密度での穴埋めと高密度での過剰遮蔽防止の両立)
            if (currentDensity > 0f && ActiveOptimizedParameters != null &&
                ActiveOptimizedParameters.TryGetDensityRule(currentDensity, out var densRule))
            {
                r8 = densRule.mode8ThresholdR;
                r20 = densRule.mode20ThresholdR;
                l20 = densRule.mode20MaxConsecutiveEmptyL;
                if (method.mode == PCDRendererFeature.PCD_OcclusionEvaluationMode.RotationLUT36 && !string.IsNullOrEmpty(densRule.mode36Hex))
                {
                    hex = densRule.mode36Hex;
                }
                else if (method.mode == PCDRendererFeature.PCD_OcclusionEvaluationMode.PatternLUT256 && !string.IsNullOrEmpty(densRule.mode256Hex))
                {
                    hex = densRule.mode256Hex;
                }
            }

            if (method.mode == PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorThreshold)
            {
                pipeline.minOccludedSectorsMode8 = r8;
            }
            else if (method.mode == PCDRendererFeature.PCD_OcclusionEvaluationMode.SectorConsecutiveZeros)
            {
                pipeline.minOccludedSectorsMode20 = r20;
                pipeline.maxConsecutiveEmptySectors = l20;
            }
            else if (method.mode == PCDRendererFeature.PCD_OcclusionEvaluationMode.RotationLUT36 ||
                     method.mode == PCDRendererFeature.PCD_OcclusionEvaluationMode.PatternLUT256)
            {
                if (!string.IsNullOrEmpty(hex))
                {
                    pipeline.customPatternLutHex = hex;
                }
            }

            if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
            {
                PCDRendererFeature.Instance.settings.evaluationMode = method.mode;
                PCDRendererFeature.Instance.settings.occlusionThreshold_T_ave = method.tAve;
                PCDRendererFeature.Instance.settings.occlusionThreshold_T_loc = method.tLoc;
                PCDRendererFeature.Instance.settings.useSectorSpecificThreshold = method.useSectorSpecificThreshold;
                if (method.sectorThreshold_T_loc != null && method.sectorThreshold_T_loc.Length == 8)
                {
                    PCDRendererFeature.Instance.settings.sectorThreshold_T_loc = (float[])method.sectorThreshold_T_loc.Clone();
                }
                PCDRendererFeature.Instance.settings.minOccludedSectors = pipeline.minOccludedSectors;
                PCDRendererFeature.Instance.settings.maxConsecutiveEmptySectors = pipeline.maxConsecutiveEmptySectors;
            }

            pipeline.MarkLutDirty();
        }

        /// <summary>
        /// ドメインリロード等で ActiveOptimizedParameters が未設定の場合、
        /// 最新の最適化 JSON (optimized_parameters.json) または CSV (common_rules_summary.csv) から自動復元を試みます。
        /// </summary>
        public static void EnsureOptimizedParametersLoaded()
        {
            if (ActiveOptimizedParameters != null) return;

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // 1. JSON ファイルからの優先ロード
            string[] jsonSearchPaths = new string[]
            {
                Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExperiment", "AdditionalExperiments", "Presets", "Optimization", "Additional", "Optimization", "RuleOptimization", "optimized_parameters.json"),
                Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExperiment", "AdditionalExperiments", "Presets", "Optimization", "Additional", "RuleOptimizationResults", "optimized_parameters.json"),
                Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExperiment", "Optimization", "RuleOptimization", "optimized_parameters.json"),
                Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExperiment", "RuleOptimizationResults", "optimized_parameters.json"),
                Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExp", "RuleOptimizationResults", "optimized_parameters.json"),
                Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExperiments", "Presets", "Optimization", "Additional", "Optimization", "RuleOptimization", "optimized_parameters.json"),
                Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExperiments", "Presets", "Optimization", "Additional", "RuleOptimizationResults", "optimized_parameters.json")
            };

            foreach (var p in jsonSearchPaths)
            {
                if (File.Exists(p) && OPE_OptimizationResult.TryLoadFromJson(p, OPE_OptimizationResult.ObjectiveType.MeanIoUMaximized, out var res))
                {
                    ActiveOptimizedParameters = res;
                    Debug.Log($"[OPE_BenchmarkMethodRegistry] 最新の最適化パラメータ(JSON)を自動ロードしました: {p}");
                    return;
                }
            }

            // 2. CSV ファイルからのフォールバックロード
            string[] csvSearchPaths = new string[]
            {
                Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExperiment", "AdditionalExperiments", "Presets", "Optimization", "Additional", "Optimization", "RuleOptimization", "common_rules_summary.csv"),
                Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExperiment", "AdditionalExperiments", "Presets", "Optimization", "Additional", "RuleOptimizationResults", "common_rules_summary.csv"),
                Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExperiment", "Optimization", "RuleOptimization", "common_rules_summary.csv"),
                Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExperiment", "RuleOptimizationResults", "common_rules_summary.csv"),
                Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExp", "RuleOptimizationResults", "common_rules_summary.csv"),
                Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExperiments", "Presets", "Optimization", "Additional", "Optimization", "RuleOptimization", "common_rules_summary.csv"),
                Path.Combine(userProfile, "Documents", "tsutsumi", "Estimation", "AdditionalExperiments", "Presets", "Optimization", "Additional", "RuleOptimizationResults", "common_rules_summary.csv")
            };

            foreach (var p in csvSearchPaths)
            {
                if (File.Exists(p) && OPE_OptimizationResult.TryLoadFromCsv(p, OPE_OptimizationResult.ObjectiveType.MeanIoUMaximized, out var res))
                {
                    ActiveOptimizedParameters = res;
                    Debug.Log($"[OPE_BenchmarkMethodRegistry] 最新の最適化パラメータ(CSV)を自動ロードしました: {p}");
                    return;
                }
            }
        }
    }
}

