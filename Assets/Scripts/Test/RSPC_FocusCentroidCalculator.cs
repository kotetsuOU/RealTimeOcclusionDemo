using UnityEngine;

/// <summary>
/// NewRsPointCloudRenderer から *GPUバッファ* を直接取得し、
/// ComputeShader を使用して焦点オブジェクト周辺の点群の重心を計算します。
/// (このスクリプトは計算のみを担当し、結果は Public プロパティで公開します)
/// </summary>
public class RSPC_FocusCentroidCalculator : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("ワールド座標の点群データを提供する RsPointCloudRenderer")]
    [SerializeField]
    private NewRsPointCloudRenderer pointCloudRenderer;

    [Tooltip("重心計算の基準点（焦点）となるオブジェクトの Transform")]
    [SerializeField]
    private Transform focusTransform;

    [Tooltip("重心計算を実行するコンピュートシェダー")]
    [SerializeField]
    private ComputeShader centroidComputeShader;

    [Header("Settings")]
    [Tooltip("重心計算に含める点を探す半径 (メートル)")]
    [SerializeField]
    private float searchRadius = 0.01f;

    // --- Compute Shader Buffers ---
    private ComputeBuffer _resultBuffer;
    private int _kernelID;
    private const int THREAD_GROUP_SIZE = 256;
    private Vector4[] _resultData;

    // --- Public Properties for Aggregator ---
    /// <summary>
    /// このCalculatorが計算した焦点内の点のワールド座標の合計
    /// </summary>
    public Vector3 TotalSum { get; private set; } = Vector3.zero;

    /// <summary>
    /// このCalculatorが計算した焦点内の点の合計数
    /// </summary>
    public int TotalCount { get; private set; } = 0;


    void Start()
    {
        if (!ValidateDependencies())
        {
            this.enabled = false;
            return;
        }
        _kernelID = centroidComputeShader.FindKernel("CSMain");
    }

    private bool ValidateDependencies()
    {
        if (pointCloudRenderer == null)
        {
            UnityEngine.Debug.LogError("RSPC_FocusCentroidCalculator: 'Point Cloud Renderer' が設定されていません。", this);
            return false;
        }
        if (focusTransform == null)
        {
            UnityEngine.Debug.LogError("RSPC_FocusCentroidCalculator: 'Focus Transform' が設定されていません。", this);
            return false;
        }
        if (centroidComputeShader == null)
        {
            UnityEngine.Debug.LogError("RSPC_FocusCentroidCalculator: 'Centroid Compute Shader' が設定されていません。", this);
            return false;
        }
        return true;
    }

    void LateUpdate()
    {
        if (pointCloudRenderer == null || focusTransform == null || centroidComputeShader == null)
        {
            return;
        }

        // 1. NewRsPointCloudRenderer から GPU バッファと頂点数を直接取得
        ComputeBuffer inputVerticesBuffer = pointCloudRenderer.GetGpuFilteredVerticesBuffer();
        int vertexCount = pointCloudRenderer.FinalVertexCount;

        if (inputVerticesBuffer == null || vertexCount == 0)
        {
            // データがない場合は、集計結果をリセットする
            TotalSum = Vector3.zero;
            TotalCount = 0;
            return;
        }

        // 2. Compute Shader の出力バッファを準備
        int numThreadGroups = (vertexCount + THREAD_GROUP_SIZE - 1) / THREAD_GROUP_SIZE;
        EnsureComputeBuffer(ref _resultBuffer, numThreadGroups, sizeof(float) * 4);

        if (_resultData == null || _resultData.Length != numThreadGroups)
        {
            _resultData = new Vector4[numThreadGroups];
        }

        // 3. Compute Shader にパラメータを設定
        float radiusSq = searchRadius * searchRadius;
        centroidComputeShader.SetBuffer(_kernelID, "_InputVertices", inputVerticesBuffer);
        centroidComputeShader.SetBuffer(_kernelID, "_ResultBuffer", _resultBuffer);
        centroidComputeShader.SetVector("_FocusPoint", focusTransform.position);
        centroidComputeShader.SetFloat("_RadiusSq", radiusSq);
        centroidComputeShader.SetInt("_VertexCount", vertexCount);

        // 4. Compute Shader を実行
        centroidComputeShader.Dispatch(_kernelID, numThreadGroups, 1, 1);

        // 5. 結果を GPU から取得
        _resultBuffer.GetData(_resultData);

        // 6. CPU 側で結果を集計し、Publicプロパティに格納
        ProcessResultsAndUpdateProperties(_resultData);
    }

    /// <summary>
    /// GPUからの結果を集計し、Publicプロパティを更新します。
    /// </summary>
    private void ProcessResultsAndUpdateProperties(Vector4[] groupResults)
    {
        Vector3 totalSum = Vector3.zero;
        float totalCountFloat = 0; // CSからの書き戻しがfloat (w) のため

        foreach (var result in groupResults)
        {
            totalSum.x += result.x;
            totalSum.y += result.y;
            totalSum.z += result.z;
            totalCountFloat += result.w;
        }

        // Public プロパティを更新
        this.TotalSum = totalSum;
        this.TotalCount = (int)totalCountFloat;
    }

    // (EnsureComputeBuffer, OnDestroy は変更なし)
    private void EnsureComputeBuffer(ref ComputeBuffer buffer, int count, int stride)
    {
        int bufferCount = Mathf.Max(1, count);
        if (buffer == null || !buffer.IsValid() || buffer.count < bufferCount)
        {
            buffer?.Release();
            buffer = new ComputeBuffer(bufferCount, stride, ComputeBufferType.Structured);
        }
    }

    void OnDestroy()
    {
        _resultBuffer?.Release();
    }
}