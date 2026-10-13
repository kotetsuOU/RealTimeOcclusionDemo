using System;
using System.IO;
using UnityEngine;

namespace SICESI
{
    /// <summary>
    /// SICE SI ベンチマーク評価における撮影画像のディスク I/O（読み込み・削除・差分マップ生成）を担当するクラス。
    /// </summary>
    public static class SICESI_BenchmarkImageIO
    {
        /// <summary>
        /// GT とテスト画像の差分を可視化して保存します（白=TP, 赤=FP:遮蔽漏れ, 青=FN:過剰遮蔽）。
        /// </summary>
        public static void SaveDiffImage(Texture2D gtTex, Texture2D testTex, string path, byte threshold = 128)
        {
            if (gtTex == null || testTex == null || gtTex.width != testTex.width || gtTex.height != testTex.height) return;

            Color32[] gt = gtTex.GetPixels32();
            Color32[] test = testTex.GetPixels32();
            var diff = new Color32[gt.Length];
            for (int i = 0; i < gt.Length; i++)
            {
                bool g = SICESI_IoUCalculator.IsVirtualObjectPixel(gt[i], threshold);
                bool t = SICESI_IoUCalculator.IsVirtualObjectPixel(test[i], threshold);
                if (g && t) diff[i] = new Color32(255, 255, 255, 255);
                else if (!g && t) diff[i] = new Color32(255, 0, 0, 255);
                else if (g && !t) diff[i] = new Color32(0, 0, 255, 255);
                else diff[i] = new Color32(0, 0, 0, 255);
            }

            var tex = new Texture2D(gtTex.width, gtTex.height, TextureFormat.RGB24, false);
            tex.SetPixels32(diff);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// <summary>
        /// 指定ディレクトリ内の既存ステレオ画像（Left/Right、直下の両階層）を削除します。
        /// 前回実行の古い画像を新規撮影画像と取り違えることを防ぎます。
        /// </summary>
        public static void DeleteStereoImages(string targetDir, string filePrefix)
        {
            string[] paths =
            {
                Path.Combine(targetDir, "Left", $"{filePrefix}_left.png"),
                Path.Combine(targetDir, "Right", $"{filePrefix}_right.png"),
                Path.Combine(targetDir, $"{filePrefix}_left.png"),
                Path.Combine(targetDir, $"{filePrefix}_right.png")
            };
            for (int i = 0; i < paths.Length; i++)
            {
                try { if (File.Exists(paths[i])) File.Delete(paths[i]); }
                catch (IOException) { }
            }
        }

        /// <summary>
        /// 指定ディレクトリからステレオ撮影画像（Left, Right）を検索して読み込みます。
        /// - サブディレクトリ階層: {targetDir}/Left/{prefix}_left.png, {targetDir}/Right/{prefix}_right.png
        /// - フラット階層 (フォールバック): {targetDir}/{prefix}_left.png, {targetDir}/{prefix}_right.png
        /// の両方を自動解決します。
        /// </summary>
        public static (Texture2D leftTex, Texture2D rightTex) LoadStereoTextures(
            string targetDir,
            string filePrefix,
            int maxWaitMs = 2000)
        {
            // 1. 左目パス解決 (Left/ サブフォルダ優先、フォールバックで直下)
            string leftPath = Path.Combine(targetDir, "Left", $"{filePrefix}_left.png");
            if (!File.Exists(leftPath))
            {
                string flatLeft = Path.Combine(targetDir, $"{filePrefix}_left.png");
                if (File.Exists(flatLeft)) leftPath = flatLeft;
            }

            // 2. 右目パス解決 (Right/ サブフォルダ優先、フォールバックで直下)
            string rightPath = Path.Combine(targetDir, "Right", $"{filePrefix}_right.png");
            if (!File.Exists(rightPath))
            {
                string flatRight = Path.Combine(targetDir, $"{filePrefix}_right.png");
                if (File.Exists(flatRight)) rightPath = flatRight;
            }

            Texture2D leftTex = LoadTextureFromDisk(leftPath, maxWaitMs);
            Texture2D rightTex = LoadTextureFromDisk(rightPath, maxWaitMs);

            return (leftTex, rightTex);
        }

        /// <summary>
        /// ディスクから PNG 画像を Texture2D として読み込みます。
        /// 非同期書き込み中の場合を考慮し、ファイルが存在してサイズが安定するまで待機します。
        /// </summary>
        public static Texture2D LoadTextureFromDisk(string path, int maxWaitMs = 2000)
        {
            if (string.IsNullOrEmpty(path)) return null;

            // 非同期書き込み完了待機
            int waited = 0;
            const int interval = 50;
            while (!File.Exists(path) && waited < maxWaitMs)
            {
                System.Threading.Thread.Sleep(interval);
                waited += interval;
            }

            if (!File.Exists(path)) return null;

            // ファイルが書き込み中でロックされている可能性を考慮したリトライ読み込み
            waited = 0;
            while (waited < maxWaitMs)
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    if (bytes != null && bytes.Length > 0)
                    {
                        var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                        if (tex.LoadImage(bytes))
                        {
                            return tex;
                        }
                        UnityEngine.Object.DestroyImmediate(tex);
                    }
                }
                catch (IOException)
                {
                    // ファイルが他スレッドにより書き込み中の場合は少し待機してリトライ
                    System.Threading.Thread.Sleep(interval);
                    waited += interval;
                    continue;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SICESI_BenchmarkImageIO] テクスチャ読み込み例外 ({path}): {ex.Message}");
                    return null;
                }

                System.Threading.Thread.Sleep(interval);
                waited += interval;
            }

            return null;
        }
    }
}
