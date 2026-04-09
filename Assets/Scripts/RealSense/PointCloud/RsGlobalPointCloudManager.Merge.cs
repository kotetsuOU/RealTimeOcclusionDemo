using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 点群の合成（Merge）処理を管理する部分的クラス。
/// 複数のカメラや単一カメラの点群データを、ComputeShaderを用いて全体のグローバルバッファに転送・結合します。
/// </summary>
public partial class RsGlobalPointCloudManager
{
    // float3 pos(12) + float3 col(12) + uint type(4) = 28 bytes
    // ComputeBuffer用の1頂点あたりのデータサイズ定義
    private const int STRIDE = 28;

    /// <summary>
    /// 全ての管理対象カメラ（レンダラー）の点群を一つのグローバルバッファに統合する全体処理。
    /// 各レンダラーのバッファから最大点数を超えないように順次GPUコピーを行います。
    /// </summary>
    private void ProcessMergeAll()
    {
        int currentTotalCount = 0;

        // 全ての子レンダラーを巡回してコピー処理をディスパッチ
        foreach (var renderer in GetChildRenderers())
        {
            if (renderer == null) continue;

            // コピー・コマンドをキューに積み、コピーされた頂点数を加算する
            int copiedCount = DispatchCopy(renderer, currentTotalCount);
            currentTotalCount += copiedCount;

            // 統合後の点数が最大許容数に到達・超過した場合は、以降のカメラの点群は描画せずに処理を打ち切る
            if (currentTotalCount >= maxTotalPoints) break;
        }

        // 現在の実際に結合された総点群数を更新
        CurrentTotalCount = currentTotalCount;
    }

    /// <summary>
    /// 指定された単一カメラの点群のみをグローバルバッファへコピーする処理。
    /// デバッグや特定カメラのみのキャプチャを行いたい場合に利用されます。
    /// </summary>
    private void ProcessSingleCamera()
    {
        var activeRenderers = new List<RsPointCloudRenderer>();
        foreach (var renderer in GetChildRenderers())
        {
            activeRenderers.Add(renderer);
        }

        // 指定インデックスが範囲外の場合は不正なため、総数を0として処理終了
        if (debugCameraIndex < 0 || debugCameraIndex >= activeRenderers.Count)
        {
            CurrentTotalCount = 0;
            return;
        }

        var targetRenderer = activeRenderers[debugCameraIndex];

        // 対象カメラの点群サイズのみをディスパッチコピー (オフセット0)
        int copiedCount = DispatchCopy(targetRenderer, 0);

        CurrentTotalCount = copiedCount;
    }

    /// <summary>
    /// 各レンダラーの点群バッファから、統合バッファ(globalBuffer)へオフセット位置から並列コピーを行う。
    /// ComputeShaderとCommandBufferを利用することで、CPUを介さずに高速なGPU間データ転送を実現しています。
    /// </summary>
    /// <param name="renderer">コピー元の点群データを持つレンダラー</param>
    /// <param name="dstOffset">統合先グローバルバッファ内の書き込み開始オフセット</param>
    /// <returns>実際にコピーがスケジュールされた頂点数</returns>
    private int DispatchCopy(RsPointCloudRenderer renderer, int dstOffset)
    {
        if (renderer == null) return 0;

        ComputeBuffer srcBuffer = renderer.GetPCDSourceBuffer();
        int count = renderer.GetPCDSourceCount();

        // コピー元バッファが無効、またはデータが存在しない場合はスキップ
        if (srcBuffer == null || count <= 0) return 0;

        // 最大許容数を超えないように実際のコピー数をクリップ（安全対策）
        if (dstOffset + count > maxTotalPoints)
        {
            count = maxTotalPoints - dstOffset;
            if (count <= 0) return 0;
        }

        // コピー用のComputeShaderにパラメータを設定
        mergeComputeShader.SetBuffer(_kernelMerge, "_SourceBuffer", srcBuffer);
        mergeComputeShader.SetBuffer(_kernelMerge, "_DestinationBuffer", _globalBuffer);
        mergeComputeShader.SetInt("_CopyCount", count);
        mergeComputeShader.SetInt("_DstOffset", dstOffset);
        mergeComputeShader.SetVector("_Color", renderer.pointCloudColor);

        // ComputeShaderでのスレッドグループ数を計算（1グループにつき256スレッド処理を前提とする）
        int threadGroups = Mathf.CeilToInt(count / 256.0f);

        // GPUに処理を積むためのCommandBufferを生成 (Unityプロファイラで判別しやすくするため命名)
        var cmd = new CommandBuffer { name = "RsPointCloud.GlobalMerge" };
        string sampleName = $"RsPointCloud.GlobalMerge.Dispatch/{renderer.gameObject.name}";
        
        cmd.BeginSample(sampleName);
        cmd.DispatchCompute(mergeComputeShader, _kernelMerge, threadGroups, 1, 1);
        cmd.EndSample(sampleName);
        
        // 構築したコマンドを即時実行してGPUにキューイング
        Graphics.ExecuteCommandBuffer(cmd);
        
        // 使い終わったCommandBufferを解放（メモリリーク防止）
        cmd.Release();

        return count;
    }
}
