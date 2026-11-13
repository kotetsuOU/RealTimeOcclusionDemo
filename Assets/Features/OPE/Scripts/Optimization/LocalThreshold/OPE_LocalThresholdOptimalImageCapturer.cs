using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OPE
{
    /// <summary>
    /// T_loc 最適化スイープ完了後、導出された最適値 T_loc* における 8方向白黒マスクおよび
    /// 理想深度ピラミッド正解 (GT) マスク画像を各プリセットごとに生成・保存する Capturer クラス。
    /// ガンマ変換や画面 Blit の歪みを排除し、ビット完全な純粋二値画像 (0 or 255) を直接 PNG 保存します。
    /// </summary>
    public static class OPE_LocalThresholdOptimalImageCapturer
    {
        /// <summary>
        /// 最適値 T_loc* および 8セクター個別最適ベクトルにおける画像を各プリセットごとに撮影・保存するコルーチン。
        /// </summary>
        public static IEnumerator CaptureOptimalImagesRoutine(
            OPE_StereoEvaluationController controller,
            List<OPE_PresetPoseManager.PresetEntry> presets,
            float bestTLoc,
            string baseOutputDir,
            Action<string> onStatusUpdate = null,
            float[] bestSectorIndependentTLoc = null)
        {
            if (controller == null || presets == null || presets.Count == 0) yield break;

            for (int pi = 0; pi < presets.Count; pi++)
            {
                var preset = presets[pi];
                string status = $"Capturing Optimal Images for Best T_loc ({bestTLoc:F2}) [{pi + 1}/{presets.Count}]: {preset.caseName}";
                onStatusUpdate?.Invoke(status);

                OPE_PresetPoseManager.ApplyPresetToScene(controller, preset.jsonPath);
                if (controller.dummyPointCloudProvider != null) controller.dummyPointCloudProvider.ForceUpdateSampling();
                if (PCDRendererFeature.Instance != null) PCDRendererFeature.Instance.MarkPointCloudDataDirty();

                yield return null;

                string presetSubDir = Path.Combine(baseOutputDir, preset.caseName);
                Directory.CreateDirectory(presetSubDir);

                // 1. 理想深度ピラミッドGTと実測点群評価キャッシュを取得
                OPE_LocalThresholdGroundTruthCapturer.GroundTruthCaptureData gtData = null;
                yield return OPE_LocalThresholdGroundTruthCapturer.CaptureGroundTruthRoutine(
                    controller, data => gtData = data);

                if (gtData != null)
                {
                    int w = gtData.width > 0 ? gtData.width : Screen.width;
                    int h = gtData.height > 0 ? gtData.height : Screen.height;

                    // 1. GT 8方向マスクの保存
                    string gtDir = Path.Combine(presetSubDir, "GT");
                    Directory.CreateDirectory(gtDir);

                    if (gtData.gtLeftData != null)
                    {
                        SaveMaskTextureFromData(gtData.gtLeftData, w, h, bitShift: 16, Path.Combine(gtDir, "sector_mask_left.png"));
                        for (int s = 0; s < 8; s++)
                        {
                            SaveSectorBinaryTextureFromData(gtData.gtLeftData, w, h, bitShift: 16, s, Path.Combine(gtDir, $"sector_{s}_mask_left.png"));
                        }
                    }
                    if (gtData.gtRightData != null)
                    {
                        SaveMaskTextureFromData(gtData.gtRightData, w, h, bitShift: 16, Path.Combine(gtDir, "sector_mask_right.png"));
                        for (int s = 0; s < 8; s++)
                        {
                            SaveSectorBinaryTextureFromData(gtData.gtRightData, w, h, bitShift: 16, s, Path.Combine(gtDir, $"sector_{s}_mask_right.png"));
                        }
                    }

                    // 2. 全体共通最適値 T_loc* での推定 8方向マスクおよび個別セクター画像の保存
                    string optDir = Path.Combine(presetSubDir, $"Best_TLoc_{bestTLoc:F2}");
                    Directory.CreateDirectory(optDir);

                    uint[] testLeftData = gtData.leftTestCache?.BuildPackedTestDataForThreshold(bestTLoc);
                    uint[] testRightData = gtData.rightTestCache?.BuildPackedTestDataForThreshold(bestTLoc);

                    if (testLeftData != null)
                    {
                        SaveMaskTextureFromData(testLeftData, w, h, bitShift: 0, Path.Combine(optDir, "sector_mask_left.png"));
                        for (int s = 0; s < 8; s++)
                        {
                            SaveSectorBinaryTextureFromData(testLeftData, w, h, bitShift: 0, s, Path.Combine(optDir, $"sector_{s}_mask_left.png"));
                        }
                    }
                    if (testRightData != null)
                    {
                        SaveMaskTextureFromData(testRightData, w, h, bitShift: 0, Path.Combine(optDir, "sector_mask_right.png"));
                        for (int s = 0; s < 8; s++)
                        {
                            SaveSectorBinaryTextureFromData(testRightData, w, h, bitShift: 0, s, Path.Combine(optDir, $"sector_{s}_mask_right.png"));
                        }
                    }

                    // 3. 8セクター個別最適ベクトル T_loc,s* での推定マスク画像の保存
                    if (bestSectorIndependentTLoc != null && bestSectorIndependentTLoc.Length == 8)
                    {
                        string indepDir = Path.Combine(presetSubDir, "Best_SectorIndependent");
                        Directory.CreateDirectory(indepDir);

                        uint[] indepLeftData = gtData.leftTestCache?.BuildPackedTestDataForThreshold(bestSectorIndependentTLoc);
                        uint[] indepRightData = gtData.rightTestCache?.BuildPackedTestDataForThreshold(bestSectorIndependentTLoc);

                        if (indepLeftData != null)
                        {
                            SaveMaskTextureFromData(indepLeftData, w, h, bitShift: 0, Path.Combine(indepDir, "sector_mask_left.png"));
                            for (int s = 0; s < 8; s++)
                            {
                                SaveSectorBinaryTextureFromData(indepLeftData, w, h, bitShift: 0, s, Path.Combine(indepDir, $"sector_{s}_mask_left.png"));
                            }
                        }
                        if (indepRightData != null)
                        {
                            SaveMaskTextureFromData(indepRightData, w, h, bitShift: 0, Path.Combine(indepDir, "sector_mask_right.png"));
                            for (int s = 0; s < 8; s++)
                            {
                                SaveSectorBinaryTextureFromData(indepRightData, w, h, bitShift: 0, s, Path.Combine(indepDir, $"sector_{s}_mask_right.png"));
                            }
                        }
                    }

                    gtData.Dispose();
                }

                yield return null;
            }
        }

        private static void SaveMaskTextureFromData(uint[] data, int w, int h, int bitShift, string path)
        {
            if (data == null || w <= 0 || h <= 0) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color32[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                uint val = data[i];
                // 評価領域 (isEvaluated: bit 12) かどうか
                bool isEval = ((val >> 12) & 1u) != 0u;
                byte b = isEval ? (byte)((val >> bitShift) & 0xFFu) : (byte)0;
                pixels[i] = new Color32(b, b, b, 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            File.WriteAllBytes(path, png);
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static void SaveSectorBinaryTextureFromData(uint[] data, int w, int h, int bitShift, int sector, string path)
        {
            if (data == null || w <= 0 || h <= 0) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color32[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                uint val = data[i];
                bool isEval = ((val >> 12) & 1u) != 0u;
                bool occ = isEval && ((((val >> bitShift) >> sector) & 1u) != 0u);
                byte b = occ ? (byte)255 : (byte)0;
                pixels[i] = new Color32(b, b, b, 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            File.WriteAllBytes(path, png);
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}

