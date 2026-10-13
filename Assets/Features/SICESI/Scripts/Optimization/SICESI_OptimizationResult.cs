using System;
using System.IO;
using UnityEngine;

namespace SICESI
{
    /// <summary>
    /// Python 最適化スクリプト (optimize_pattern_rules.py) によって算出された
    /// 各手法 (Mode 8, 20, 36, 256) の最適化パラメータを保持するデータモデル。
    /// </summary>
    [Serializable]
    public class SICESI_OptimizationResult
    {
        public enum ObjectiveType
        {
            MeanIoUMaximized,
            RelativeReductionMaximized
        }

        public ObjectiveType objective = ObjectiveType.MeanIoUMaximized;
        public string timestamp = "";
        public string sourceFilePath = "";

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

        /// <summary>
        /// JSON ファイル (optimized_parameters.json) からデシリアライズして結果オブジェクトを生成します。
        /// </summary>
        public static bool TryLoadFromJson(string jsonPath, ObjectiveType targetObjective, out SICESI_OptimizationResult result)
        {
            result = null;
            if (!File.Exists(jsonPath)) return false;

            try
            {
                string jsonText = File.ReadAllText(jsonPath);
                var root = JsonUtility.FromJson<SICESI_OptimizationRootJson>(jsonText);
                if (root == null) return false;

                var objData = targetObjective == ObjectiveType.MeanIoUMaximized
                    ? root.meanIoUMaximized
                    : root.relativeReductionMaximized;

                if (objData == null) return false;

                result = new SICESI_OptimizationResult
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

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SICESI_OptimizationResult] JSON パースに失敗しました: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// CSV ファイル (common_rules_summary.csv) からフォールバックパースして結果オブジェクトを生成します。
        /// </summary>
        public static bool TryLoadFromCsv(string csvPath, ObjectiveType targetObjective, out SICESI_OptimizationResult result)
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

                var res = new SICESI_OptimizationResult
                {
                    objective = targetObjective,
                    sourceFilePath = csvPath,
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    string[] cols = line.Split(',');
                    if (cols.Length < 13) continue;

                    string obj = cols[0].Trim();
                    string method = cols[1].Trim();
                    float.TryParse(cols[2].Trim(), out float meanIou);
                    string bestRule = cols[9].Trim().Trim('"');
                    string ruleSummary = cols[10].Trim().Trim('"');
                    string hexMask = cols[12].Trim().Trim('"').Replace("0x", "");

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
                Debug.LogWarning($"[SICESI_OptimizationResult] CSV パースに失敗しました: {ex.Message}");
                return false;
            }
        }
    }

    #region JSON デシリアライズ用ヘルパークラス

    [Serializable]
    public class SICESI_OptimizationRootJson
    {
        public SICESI_OptimizationObjectiveData meanIoUMaximized;
        public SICESI_OptimizationObjectiveData relativeReductionMaximized;
    }

    [Serializable]
    public class SICESI_OptimizationObjectiveData
    {
        public SICESI_Mode8Data mode8;
        public SICESI_Mode20Data mode20;
        public SICESI_Mode36Data mode36;
        public SICESI_Mode256Data mode256;
    }

    [Serializable]
    public class SICESI_Mode8Data
    {
        public int thresholdR = 7;
        public string ruleDesc = "";
        public float meanIoU = 0f;
        public string hexMask = "";
    }

    [Serializable]
    public class SICESI_Mode20Data
    {
        public int thresholdR = 4;
        public int maxConsecutiveEmptyL = 1;
        public string ruleDesc = "";
        public float meanIoU = 0f;
        public string hexMask = "";
    }

    [Serializable]
    public class SICESI_Mode36Data
    {
        public string hexMask = "";
        public int visibleClasses = 0;
        public float meanIoU = 0f;
    }

    [Serializable]
    public class SICESI_Mode256Data
    {
        public string hexMask = "";
        public int visiblePatterns = 0;
        public float meanIoU = 0f;
    }

    #endregion
}
