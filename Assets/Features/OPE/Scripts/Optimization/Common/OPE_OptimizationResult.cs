using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace OPE
{
    /// <summary>
    /// Python 最適化スクリプト (optimize_pattern_rules.py) によって算出された
    /// 各手法 (Mode 8, 20, 36, 256) の最適化パラメータを保持するデータモデル。
    /// </summary>
    [Serializable]
    public class OPE_OptimizationResult
    {
        public enum ObjectiveType
        {
            MeanIoUMaximized,
            RelativeReductionMaximized
        }

        public ObjectiveType objective = ObjectiveType.MeanIoUMaximized;
        public string timestamp = "";
        public string sourceFilePath = "";

        // Mode Average
        public float averageBestTAve = -1f;
        public float averageMeanIoU = 0f;

        // Local Threshold (T_loc)
        public float localThresholdBestTLoc = -1f;
        public float localThresholdMeanIoU = 0f;

        // Mode 8
        public int mode8ThresholdR = 7;
        public string mode8RuleDesc = "Occ < 7";
        public float mode8MeanIoU = 0f;
        public string mode8HexMask = "";

        // Mode 20
        public int mode20ThresholdR = 4;
        public int mode20MaxConsecutiveEmptyL = 1;
        public string mode20RuleDesc = "Occ < 4 or Unocc > 1";
        public float mode20MeanIoU = 0f;
        public string mode20HexMask = "";

        // Mode 36
        public string mode36Hex = "";
        public int mode36VisibleClasses = 0;
        public float mode36MeanIoU = 0f;

        // Mode 256
        public string mode256Hex = "";
        public int mode256VisiblePatterns = 0;
        public float mode256MeanIoU = 0f;

        /// <summary> 点群密度ごとの最適規則リスト (低密度での穴埋めと高密度での輪郭過剰遮蔽防止の両立) </summary>
        public List<OPE_DensityRuleEntry> densityRules = new List<OPE_DensityRuleEntry>();

        /// <summary>
        /// 指定された密度に合致する最適化規則を取得します。
        /// </summary>
        public bool TryGetDensityRule(float currentDensity, out OPE_DensityRuleEntry rule)
        {
            rule = null;
            if (densityRules == null || densityRules.Count == 0 || currentDensity <= 0f) return false;

            float bestDiff = float.MaxValue;
            OPE_DensityRuleEntry bestEntry = null;
            foreach (var entry in densityRules)
            {
                float diff = Mathf.Abs(entry.density - currentDensity);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestEntry = entry;
                }
            }

            if (bestEntry != null && bestDiff < 0.15f)
            {
                rule = bestEntry;
                return true;
            }
            return false;
        }

        /// <summary>
        /// JSON ファイル (optimized_parameters.json) からデシリアライズして結果オブジェクトを生成します。
        /// </summary>
        public static bool TryLoadFromJson(string jsonPath, ObjectiveType targetObjective, out OPE_OptimizationResult result)
        {
            result = null;
            if (!File.Exists(jsonPath)) return false;

            try
            {
                string jsonText = File.ReadAllText(jsonPath);
                var root = JsonUtility.FromJson<OPE_OptimizationRootJson>(jsonText);
                if (root == null) return false;

                var objData = targetObjective == ObjectiveType.MeanIoUMaximized
                    ? root.meanIoUMaximized
                    : root.relativeReductionMaximized;

                if (objData == null) return false;

                result = new OPE_OptimizationResult
                {
                    objective = targetObjective,
                    sourceFilePath = jsonPath,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),

                    mode8ThresholdR = objData.mode8 != null ? objData.mode8.thresholdR : 7,
                    mode8RuleDesc = objData.mode8 != null ? objData.mode8.ruleDesc : "",
                    mode8MeanIoU = objData.mode8 != null ? objData.mode8.meanIoU : 0f,
                    mode8HexMask = objData.mode8 != null ? objData.mode8.hexMask : "",

                    mode20ThresholdR = objData.mode20 != null ? objData.mode20.thresholdR : 4,
                    mode20MaxConsecutiveEmptyL = objData.mode20 != null ? objData.mode20.maxConsecutiveEmptyL : 1,
                    mode20RuleDesc = objData.mode20 != null ? objData.mode20.ruleDesc : "",
                    mode20MeanIoU = objData.mode20 != null ? objData.mode20.meanIoU : 0f,
                    mode20HexMask = objData.mode20 != null ? objData.mode20.hexMask : "",

                    mode36Hex = objData.mode36 != null ? objData.mode36.hexMask : "",
                    mode36VisibleClasses = objData.mode36 != null ? objData.mode36.visibleClasses : 0,
                    mode36MeanIoU = objData.mode36 != null ? objData.mode36.meanIoU : 0f,

                    mode256Hex = objData.mode256 != null ? objData.mode256.hexMask : "",
                    mode256VisiblePatterns = objData.mode256 != null ? objData.mode256.visiblePatterns : 0,
                    mode256MeanIoU = objData.mode256 != null ? objData.mode256.meanIoU : 0f
                };

                if (root.densityRules != null && root.densityRules.Count > 0)
                {
                    foreach (var dr in root.densityRules)
                    {
                        result.densityRules.Add(new OPE_DensityRuleEntry
                        {
                            density = dr.density,
                            mode8ThresholdR = dr.mode8ThresholdR,
                            mode20ThresholdR = dr.mode20ThresholdR,
                            mode20MaxConsecutiveEmptyL = dr.mode20MaxConsecutiveEmptyL,
                            mode36Hex = dr.mode36Hex,
                            mode256Hex = dr.mode256Hex
                        });
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OPE_OptimizationResult] JSON パースに失敗しました: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// CSV ファイル (common_rules_summary.csv) からフォールバックパースして結果オブジェクトを生成します。
        /// ダブルクォートで囲まれたカンマを含む列（例: "occ_classes: [1,1,1,1,1,1,0,0,0]"）を安全に扱います。
        /// </summary>
        public static bool TryLoadFromCsv(string csvPath, ObjectiveType targetObjective, out OPE_OptimizationResult result)
        {
            result = null;
            if (!File.Exists(csvPath)) return false;

            try
            {
                string[] lines = File.ReadAllLines(csvPath);
                if (lines.Length < 2) return false;

                string targetObjStr = targetObjective == ObjectiveType.MeanIoUMaximized
                    ? "Mean_IoU_Maximized"
                    : "Relative_Reduction_Maximized";

                var res = new OPE_OptimizationResult
                {
                    objective = targetObjective,
                    sourceFilePath = csvPath,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    List<string> cols = ParseCsvLine(line);
                    if (cols.Count < 10) continue;

                    string obj = cols[0].Trim();
                    string method = cols[1].Trim();
                    float.TryParse(cols[2].Trim(), out float meanIou);
                    string bestRule = cols.Count > 9 ? cols[9].Trim().Trim('"') : "";
                    string ruleSummary = cols.Count > 10 ? cols[10].Trim().Trim('"') : "";

                    // HEX マスクは常に CSV の末尾カラム
                    string hexMask = cols[cols.Count - 1].Trim().Trim('"').Replace("0x", "").Trim();
                    if (hexMask.Length > 64)
                    {
                        hexMask = hexMask.Substring(0, 64);
                    }
                    else if (hexMask.Length > 0 && hexMask.Length < 64)
                    {
                        hexMask = hexMask.PadLeft(64, '0');
                    }

                    if (!string.Equals(obj, targetObjStr, StringComparison.OrdinalIgnoreCase)) continue;

                    if (method.Contains("Fixed_8"))
                    {
                        res.mode8MeanIoU = meanIou;
                        res.mode8RuleDesc = bestRule;
                        res.mode8HexMask = hexMask;
                        if (bestRule.Contains("<"))
                        {
                            string[] parts = bestRule.Split('<');
                            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int r)) res.mode8ThresholdR = r;
                        }
                    }
                    else if (method.Contains("Fixed_20"))
                    {
                        res.mode20MeanIoU = meanIou;
                        res.mode20RuleDesc = bestRule;
                        res.mode20HexMask = hexMask;
                        // ruleSummary: R_th=4, L_th=1
                        string[] parts = ruleSummary.Split(',');
                        foreach (var part in parts)
                        {
                            var kv = part.Split('=');
                            if (kv.Length == 2)
                            {
                                string k = kv[0].Trim();
                                if (k == "R_th" && int.TryParse(kv[1].Trim(), out int r)) res.mode20ThresholdR = r;
                                if (k == "L_th" && int.TryParse(kv[1].Trim(), out int l)) res.mode20MaxConsecutiveEmptyL = l;
                            }
                        }
                    }
                    else if (method.Contains("36"))
                    {
                        res.mode36MeanIoU = meanIou;
                        res.mode36Hex = hexMask;
                        if (bestRule.Contains("/"))
                        {
                            string[] p = bestRule.Split('/');
                            if (int.TryParse(p[0].Trim(), out int c)) res.mode36VisibleClasses = c;
                        }
                    }
                    else if (method.Contains("256"))
                    {
                        res.mode256MeanIoU = meanIou;
                        res.mode256Hex = hexMask;
                        if (bestRule.Contains("/"))
                        {
                            string[] p = bestRule.Split('/');
                            if (int.TryParse(p[0].Trim(), out int m)) res.mode256VisiblePatterns = m;
                        }
                    }
                }

                result = res;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OPE_OptimizationResult] CSV パースに失敗しました: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// クォートで囲まれたフィールド内のカンマを区切り文字として扱わない CSV 行パーサー。
        /// </summary>
        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(line)) return result;

            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            result.Add(sb.ToString());
            return result;
        }
    }

    [Serializable]
    public class OPE_DensityRuleEntry
    {
        public float density;
        public int mode8ThresholdR = 8;
        public int mode20ThresholdR = 8;
        public int mode20MaxConsecutiveEmptyL = 8;
        public string mode36Hex = "";
        public string mode256Hex = "";
    }

    #region JSON デシリアライズ用ヘルパークラス

    [Serializable]
    public class OPE_OptimizationRootJson
    {
        public OPE_OptimizationObjectiveData meanIoUMaximized;
        public OPE_OptimizationObjectiveData relativeReductionMaximized;
        public List<OPE_DensityRuleEntryJson> densityRules;
    }

    [Serializable]
    public class OPE_DensityRuleEntryJson
    {
        public float density;
        public int mode8ThresholdR;
        public int mode20ThresholdR;
        public int mode20MaxConsecutiveEmptyL;
        public string mode36Hex;
        public string mode256Hex;
    }

    [Serializable]
    public class OPE_OptimizationObjectiveData
    {
        public OPE_Mode8Data mode8;
        public OPE_Mode20Data mode20;
        public OPE_Mode36Data mode36;
        public OPE_Mode256Data mode256;
    }

    [Serializable]
    public class OPE_Mode8Data
    {
        public int thresholdR = 7;
        public string ruleDesc = "";
        public float meanIoU = 0f;
        public string hexMask = "";
    }

    [Serializable]
    public class OPE_Mode20Data
    {
        public int thresholdR = 4;
        public int maxConsecutiveEmptyL = 1;
        public string ruleDesc = "";
        public float meanIoU = 0f;
        public string hexMask = "";
    }

    [Serializable]
    public class OPE_Mode36Data
    {
        public string hexMask = "";
        public int visibleClasses = 0;
        public float meanIoU = 0f;
    }

    [Serializable]
    public class OPE_Mode256Data
    {
        public string hexMask = "";
        public int visiblePatterns = 0;
        public float meanIoU = 0f;
    }

    #endregion
}

