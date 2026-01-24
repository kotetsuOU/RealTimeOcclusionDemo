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

    public enum PCAMode
    {
        Individual,
        Integrated
    }

    [Header("Settings")]
    public ComputeShader mergeComputeShader;
    public int maxTotalPoints = 3000000;

    [Header("Debug Options")]
    [Tooltip("出力モードを選択")]
    public OutputMode outputMode = OutputMode.MergeAll;

    [Tooltip("SingleCameraモード時に表示するカメラのインデックス")]
    public int debugCameraIndex = 0;

    [Header("PCA Settings")]
    [Tooltip("PCA推定モード：Individual=各カメラ個別、Integrated=統合後")]
    public PCAMode pcaMode = PCAMode.Integrated;

    [Header("References")]
    public List<RsPointCloudRenderer> renderers = new List<RsPointCloudRenderer>();

    private ComputeBuffer _globalBuffer;
    private int _kernelMerge;
    private const int STRIDE = 28; // float3 pos(12) + float3 col(12) + uint type(4)

    private Vector3 _integratedLinePoint = Vector3.zero;
    private Vector3 _integratedLineDir = Vector3.forward;
    private readonly List<SamplingResult> _samplingResults = new List<SamplingResult>();

    public int CurrentTotalCount { get; private set; } = 0;

    public Vector3 IntegratedLinePoint => _integratedLinePoint;

    public Vector3 IntegratedLineDir => _integratedLineDir;

    public bool IsIntegratedPCAMode => pcaMode == PCAMode.Integrated;

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

        if (pcaMode == PCAMode.Integrated)
        {
            ComputeIntegratedPCA();
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

    private void ComputeIntegratedPCA()
    {
        _samplingResults.Clear();

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;

            var samplingResult = renderer.GetLastSamplingResult();
            if (samplingResult.IsValid)
            {
                _samplingResults.Add(samplingResult);
            }
        }

        if (_samplingResults.Count > 0)
        {
            var (point, dir) = PointCloudCompute.EstimateLineFromMergedSamples(_samplingResults);
            _integratedLinePoint = point;
            _integratedLineDir = dir;
        }
    }

    public (Vector3 point, Vector3 dir) GetLineEstimation()
    {
        return (_integratedLinePoint, _integratedLineDir);
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