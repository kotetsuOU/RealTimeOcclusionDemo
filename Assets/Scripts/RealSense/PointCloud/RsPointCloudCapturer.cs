using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 統合された点群バッファから、指定したフレーム数分のデータをキャプチャし、
/// PLYファイルとして保存する機能を提供するコンポーネント。
/// 1フレームを指定するとスナップショット、複数フレームを指定すると高密度なGTデータとして保存される。
/// </summary>
[RequireComponent(typeof(RsGlobalPointCloudManager))]
public class RsPointCloudCapturer : MonoBehaviour
{
    [Header("Capture Settings")]
    [Tooltip("一度のキャプチャで蓄積するフレーム数。1なら単一スナップショット")]
    [Range(1, 100)]
    public int captureFrames = 1;

    [Tooltip("出力先ディレクトリ（未指定時はプロジェクトルート）")]
    public string outputDirectory = "";

    private bool _isCapturing = false;
    private RsGlobalPointCloudManager _manager;

    // GPU側の点群データ構造とのマッピング用
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PointData
    {
        public Vector3 pos;
        public Vector3 col;
        public uint type;
    }

    private const int STRIDE = 28;

    public bool IsCapturing => _isCapturing;

    private void Awake()
    {
        _manager = GetComponent<RsGlobalPointCloudManager>();
    }

    [ContextMenu("Capture PointCloud (PLY)")]
    public void StartCapturePLY()
    {
        // 既にキャプチャ処理が走っている場合は重複実行を防ぐ
        if (_isCapturing)
        {
            Debug.LogWarning("[RsPointCloudCapturer] Already capturing.");
            return;
        }

        // そもそもマネージャ側に点群データが存在しない場合はキャプチャ不可
        if (_manager.CurrentTotalCount <= 0)
        {
            Debug.LogWarning("[RsPointCloudCapturer] No point cloud data to capture.");
            return;
        }

        // コルーチンを使って指定フレーム数分の読み戻しと蓄積を開始する
        StartCoroutine(AccumulateAndExportPLY(captureFrames));
    }

    /// <summary>
    /// GPUから非同期で点群を読み戻し、指定フレーム数分の頂点と色を蓄積します。
    /// メモリ不足（OOM）を防ぐため、全フレームのデータをRAMに保持せず、
    /// 直接テンポラリファイルへストリーミング書き込みを行います。
    /// </summary>
    /// <param name="frameCount">蓄積するフレーム数</param>
    private IEnumerator AccumulateAndExportPLY(int frameCount)
    {
        _isCapturing = true;
        int totalValidPoints = 0;

        // 一時保存ファイルのパスを生成
        string tempFileName = $"temp_gt_{Guid.NewGuid()}.bin";
        string tempFilePath = Path.Combine(Application.temporaryCachePath, tempFileName);

        // ファイルを空にして作成
        File.WriteAllBytes(tempFilePath, new byte[0]);

        Debug.Log($"[RsPointCloudCapturer] Start capturing {frameCount} frames... (Streaming to Disk)");

        for (int i = 0; i < frameCount; i++)
        {
            // 対象フレームで点群が無い場合はスキップして次のフレームへ
            if (_manager.CurrentTotalCount <= 0)
            {
                yield return new WaitForEndOfFrame();
                continue;
            }

            var globalBuffer = _manager.GetGlobalBuffer();
            if (globalBuffer == null || !globalBuffer.IsValid())
            {
                yield return new WaitForEndOfFrame();
                continue;
            }

            // メインスレッドをフットプリントせず、GPU内で結合された点群バッファ全体を非同期でリクエスト
            var request = AsyncGPUReadback.Request(globalBuffer, _manager.CurrentTotalCount * STRIDE, 0);

            // 結果が返ってくるまでコルーチンで待機する
            yield return new WaitUntil(() => request.done);

            if (request.hasError)
            {
                Debug.LogError("[RsPointCloudCapturer] GPU readback error.");
                continue;
            }

            // リクエスト結果から実際のポイントの配列データを取得。
            // 背景スレッド（Task）で安全にディスク書き出しを行うため管理ヒープの配列にコピー
            var managedArray = request.GetData<PointData>().ToArray();

            // ここから先は重いファイル入出力＋ループ処理になるため、メインスレッドをブロックさせずTaskに逃がす
            // これにより、数百〜数千万回のループ処理による深刻なフレームスパイクとProfilerのメモリ枯渇（OOM）を防ぐ
            bool chunkWritten = false;
            int appendedPoints = 0;
            Exception taskEx = null;

            Task.Run(() =>
            {
                try
                {
                    appendedPoints = ProcessAndAppendToTemp(managedArray, tempFilePath);
                }
                catch (Exception e)
                {
                    taskEx = e;
                }
                finally
                {
                    chunkWritten = true;
                }
            });

            // 背景スレッドでのファイルへの1フレーム分の書き出しが終わるまでメインスレッドで待機
            // この間、Unityは描画などのアップデートを継続できる
            yield return new WaitUntil(() => chunkWritten);

            if (taskEx != null)
            {
                Debug.LogError($"[RsPointCloudCapturer] Export task failed on frame {i + 1}: {taskEx}");
                _isCapturing = false;
                yield break;
            }

            totalValidPoints += appendedPoints;

            if (frameCount > 1)
            {
                Debug.Log($"[RsPointCloudCapturer] Captured frame {i + 1}/{frameCount}. Frame points: {appendedPoints}. Total accumulated: {totalValidPoints}");
            }
            yield return null; 
        }

        Debug.Log($"[RsPointCloudCapturer] Capture complete. Assembling {totalValidPoints} points to binary PLY...");

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string prefix = frameCount > 1 ? "GroundTruth" : "Snapshot";
        string fileName = $"{prefix}_{timestamp}.ply";

        string dir = string.IsNullOrWhiteSpace(outputDirectory) ? 
            Path.Combine(Application.dataPath, "HandTrackingData/GroundTruthHandData") : outputDirectory;

        // 保存先のディレクトリが存在しない場合には生成する
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string finalFilePath = Path.Combine(dir, fileName);

        // テンポラリデータを最終的なPLYに組み立てる処理はバックグラウンドに任せる
        Task.Run(() => AssemblePlyFile(finalFilePath, tempFilePath, totalValidPoints))
            .ContinueWith(t => {
                // ファイルへの書き込みが完了したとき（あるいは失敗したとき）にUnityメインスレッドでコールバック
                if(t.IsFaulted) Debug.LogError($"[RsPointCloudCapturer] Export failed: {t.Exception}");
                else Debug.Log($"[RsPointCloudCapturer] Successfully saved point cloud to:\n{finalFilePath}");

                #if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
                #endif

                // キャプチャ完了状態へと戻す
                _isCapturing = false;
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// PLYのヘッダーを書き込んだのち、テンポラリのバイナリデータを結合して最終的なファイルを出力します。
    /// 完了後、テンポラリファイルは削除されます。
    /// </summary>
    private static void AssemblePlyFile(string finalPath, string tempFilePath, int totalVertices)
    {
        using (var finalFs = new FileStream(finalPath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(finalFs))
        {
            string header = "ply\n" +
                            "format binary_little_endian 1.0\n" +
                            $"element vertex {totalVertices}\n" +
                            "property float x\n" +
                            "property float y\n" +
                            "property float z\n" +
                            "property uchar red\n" +
                            "property uchar green\n" +
                            "property uchar blue\n" +
                            "end_header\n";

            writer.Write(System.Text.Encoding.ASCII.GetBytes(header));
            writer.Flush(); // ヘッダーを確実に書き込む

            // テンポラリファイルから点群のバイナリデータを丸ごと一括転写する
            using (var tempFs = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read))
            {
                tempFs.CopyTo(finalFs);
            }
        }

        // ゴミ掃除（用済みのテンポラリファイルを削除）
        if (File.Exists(tempFilePath))
        {
            File.Delete(tempFilePath);
        }
    }

    /// <summary>
    /// バックグラウンドタスク内でファイルへの一時保存とループ処理を行い、メインスレッドの処理負荷を開放します。
    /// </summary>
    private static int ProcessAndAppendToTemp(PointData[] points, string filePath)
    {
        int validPoints = 0;
        using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write))
        using (var writer = new BinaryWriter(fs))
        {
            for (int p = 0; p < points.Length; p++)
            {
                var pt = points[p];
                if (pt.pos.sqrMagnitude > 0.0001f) 
                {
                    writer.Write(-pt.pos.x);
                    writer.Write(pt.pos.y);
                    writer.Write(pt.pos.z);
                    writer.Write((byte)Mathf.Clamp(pt.col.x * 255f, 0, 255));
                    writer.Write((byte)Mathf.Clamp(pt.col.y * 255f, 0, 255));
                    writer.Write((byte)Mathf.Clamp(pt.col.z * 255f, 0, 255));
                    validPoints++;
                }
            }
        }
        return validPoints;
    }
}
