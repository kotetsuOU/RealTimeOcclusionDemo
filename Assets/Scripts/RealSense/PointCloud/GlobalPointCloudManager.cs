using System.Collections.Generic;
using UnityEngine;

public class GlobalPointCloudManager : MonoBehaviour
{
    public static GlobalPointCloudManager Instance { get; private set; }

    public enum OutputMode
    {
        MergeAll,
        SingleCamera,
        None
    }

    [Header("Settings")]
    public ComputeShader mergeComputeShader;
    public int maxTotalPoints = 3000000;

    [Header("Debug Options")]
    [Tooltip("出力モードを選択します")]
    public OutputMode outputMode = OutputMode.MergeAll;

    [Tooltip("SingleCameraモード時に表示するカメラのインデックス")]
    public int debugCameraIndex = 0;

    [Header("References")]
    public List<RsPointCloudRenderer> renderers = new List<RsPointCloudRenderer>();

    private ComputeBuffer _globalBuffer;
    private int _kernelMerge;
    private const int STRIDE = 28; // float3 pos(12) + float3 col(12) + uint type(4)

    public int CurrentTotalCount { get; private set; } = 0;

    private void Awake()
    {
        Instance = this;
        _globalBuffer = new ComputeBuffer(maxTotalPoints, STRIDE);
        _kernelMerge = mergeComputeShader.FindKernel("MergePoints");
    }

    private void LateUpdate()
    {
        switch (outputMode)
        {
            case OutputMode.MergeAll:
                ProcessMergeAll();
                break;
            case OutputMode.SingleCamera:
                ProcessSingleCamera();
                break;
            case OutputMode.None:
                CurrentTotalCount = 0;
                break;
        }
    }

    private void ProcessMergeAll()
    {
        int currentTotalCount = 0;

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;

            int copiedCount = DispatchCopy(renderer, currentTotalCount);
            currentTotalCount += copiedCount;

            if (currentTotalCount >= maxTotalPoints) break;
        }

        CurrentTotalCount = currentTotalCount;
    }

    private void ProcessSingleCamera()
    {
        if (debugCameraIndex < 0 || debugCameraIndex >= renderers.Count)
        {
            CurrentTotalCount = 0;
            return;
        }

        var targetRenderer = renderers[debugCameraIndex];

        int copiedCount = DispatchCopy(targetRenderer, 0);

        CurrentTotalCount = copiedCount;
    }

    private int DispatchCopy(RsPointCloudRenderer renderer, int dstOffset)
    {
        if (renderer == null) return 0;

        ComputeBuffer srcBuffer = renderer.GetRawBuffer();
        int count = renderer.GetLastVertexCount();

        if (srcBuffer == null || count <= 0) return 0;

        if (dstOffset + count > maxTotalPoints)
        {
            count = maxTotalPoints - dstOffset;
            if (count <= 0) return 0;
        }

        mergeComputeShader.SetBuffer(_kernelMerge, "_SourceBuffer", srcBuffer);
        mergeComputeShader.SetBuffer(_kernelMerge, "_DestinationBuffer", _globalBuffer);
        mergeComputeShader.SetInt("_CopyCount", count);
        mergeComputeShader.SetInt("_DstOffset", dstOffset);
        mergeComputeShader.SetVector("_Color", renderer.pointCloudColor);

        int threadGroups = Mathf.CeilToInt(count / 256.0f);
        mergeComputeShader.Dispatch(_kernelMerge, threadGroups, 1, 1);

        return count;
    }

    public ComputeBuffer GetGlobalBuffer()
    {
        return _globalBuffer;
    }

    private void OnDestroy()
    {
        _globalBuffer?.Release();
    }
}