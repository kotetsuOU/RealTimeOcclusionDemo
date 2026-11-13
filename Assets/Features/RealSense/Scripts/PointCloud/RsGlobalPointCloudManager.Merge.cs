using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 点群の合成（Merge）処理を管理する部分的クラス。
/// 複数のカメラや単一カメラの点群データを、再利用可能な CommandBuffer と ComputeShader を用いて
/// 統合バッファ (_globalBuffer) へ1パスで転送・結合します。
/// </summary>
public partial class RsGlobalPointCloudManager
{
    // float3 pos(12) + float3 col(12) + uint type(4) = 28 bytes
    // ComputeBuffer用の1頂点あたりのデータサイズ定義
    private const int STRIDE = 28;
    private const string GlobalMergeSampleName = "RsPointCloud.GlobalMerge";

    private static readonly int SourceBufferId = Shader.PropertyToID("_SourceBuffer");
    private static readonly int DestinationBufferId = Shader.PropertyToID("_DestinationBuffer");
    private static readonly int CopyCountId = Shader.PropertyToID("_CopyCount");
    private static readonly int DstOffsetId = Shader.PropertyToID("_DstOffset");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private bool CanDispatchMerge()
    {
        return mergeComputeShader != null && _kernelMerge >= 0 && _globalBuffer != null && _globalBuffer.IsValid();
    }

    private void EnsureMergeCommandBuffer()
    {
        if (_mergeCommandBuffer == null)
        {
            _mergeCommandBuffer = new CommandBuffer { name = GlobalMergeSampleName };
        }
    }

    /// <summary>
    /// 全ての管理対象カメラ（レンダラー）の点群を一つのグローバルバッファに統合する全体処理。
    /// 全レンダラー分のディスパッチを単一の CommandBuffer に記録し、1回の ExecuteCommandBuffer で実行します。
    /// </summary>
    private void ProcessMergeAll()
    {
        if (!CanDispatchMerge())
        {
            CurrentTotalCount = 0;
            return;
        }

        EnsureMergeCommandBuffer();
        _mergeCommandBuffer.Clear();

        bool hasDispatches = false;
        int currentTotalCount = 0;

        for (int i = 0; i < _activeRenderersCache.Count; i++)
        {
            int copiedCount = EnqueueCopy(_mergeCommandBuffer, _activeRenderersCache[i], currentTotalCount, ref hasDispatches);
            currentTotalCount += copiedCount;

            // 統合後の点数が最大許容数に到達・超過した場合は、以降のカメラの点群は描画せずに処理を打ち切る
            if (currentTotalCount >= maxTotalPoints) break;
        }

        ExecuteMergeCommandBuffer(hasDispatches);
        CurrentTotalCount = currentTotalCount;
    }

    /// <summary>
    /// 指定された単一カメラの点群のみをグローバルバッファへコピーする処理。
    /// デバッグや特定カメラのみのキャプチャを行いたい場合に利用されます。
    /// </summary>
    private void ProcessSingleCamera()
    {
        if (!CanDispatchMerge() || debugCameraIndex < 0 || debugCameraIndex >= _activeRenderersCache.Count)
        {
            CurrentTotalCount = 0;
            return;
        }

        EnsureMergeCommandBuffer();
        _mergeCommandBuffer.Clear();

        bool hasDispatches = false;
        int copiedCount = EnqueueCopy(_mergeCommandBuffer, _activeRenderersCache[debugCameraIndex], 0, ref hasDispatches);

        ExecuteMergeCommandBuffer(hasDispatches);
        CurrentTotalCount = copiedCount;
    }

    /// <summary>
    /// 各レンダラーの点群バッファから、統合バッファ (_globalBuffer) へのコピーディスパッチを
    /// 共有 CommandBuffer に記録します。
    /// </summary>
    private int EnqueueCopy(CommandBuffer cmd, RsPointCloudRenderer renderer, int dstOffset, ref bool hasDispatches)
    {
        if (renderer == null) return 0;

        ComputeBuffer srcBuffer = renderer.GetPCDSourceBuffer();
        int count = renderer.GetPCDSourceCount();

        if (srcBuffer == null || !srcBuffer.IsValid() || count <= 0) return 0;

        if (dstOffset + count > maxTotalPoints)
        {
            count = maxTotalPoints - dstOffset;
            if (count <= 0) return 0;
        }

        if (!hasDispatches)
        {
            cmd.BeginSample(GlobalMergeSampleName);
            cmd.SetComputeBufferParam(mergeComputeShader, _kernelMerge, DestinationBufferId, _globalBuffer);
            hasDispatches = true;
        }

        cmd.SetComputeBufferParam(mergeComputeShader, _kernelMerge, SourceBufferId, srcBuffer);
        cmd.SetComputeIntParam(mergeComputeShader, CopyCountId, count);
        cmd.SetComputeIntParam(mergeComputeShader, DstOffsetId, dstOffset);
        cmd.SetComputeVectorParam(mergeComputeShader, ColorId, renderer.pointCloudColor);

        int threadGroups = (count + 255) / 256;
        cmd.DispatchCompute(mergeComputeShader, _kernelMerge, threadGroups, 1, 1);

        return count;
    }

    private void ExecuteMergeCommandBuffer(bool hasDispatches)
    {
        if (!hasDispatches) return;

        _mergeCommandBuffer.EndSample(GlobalMergeSampleName);
        Graphics.ExecuteCommandBuffer(_mergeCommandBuffer);
    }
}
