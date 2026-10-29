using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// RsGlobalPointCloudManager で統合された点群データに対して、指定した物体やメッシュが接触しているかを計算するクラス。
/// ComputeShader を使用し、大量の点群計算（数百万点規模）でもパフォーマンスを落とさずに接触判定を行います。
/// 複数接触点対応: アトミックカウンタでスロットを確保し、最大 maxContactPoints 件まで接触点を検出します。
/// 1点群点につき最大1メッシュ頂点（最初に見つかったもの）のみ登録されます。
/// </summary>
public class HapCollisionDetectors : MonoBehaviour
{
    public enum DetectionMode
    {
        TransformOnly,       // 単一のオブジェクト(Transform)の座標と半径で判定する
        SkinnedMeshRenderer, // アニメーションする SkinnedMeshRenderer の表面と半径で判定する
        MeshFilter           // MeshFilter のメッシュ形状と半径で判定する（変形追従対応）
    }

    [Header("Settings")]
    [Tooltip("判定に使用するモードを選択します")]
    public DetectionMode detectionMode = DetectionMode.TransformOnly;

    [Tooltip("TransformOnly モードの際に判定基準とするオブジェクト (中心座標として使用)")]
    public Transform targetObject;

    [Tooltip("SkinnedMeshRenderer モードの際に判定基準とするターゲットメッシュ")]
    public SkinnedMeshRenderer targetSkinnedMesh;

    [Tooltip("MeshFilter モードの際に判定基準とするターゲット MeshFilter")]
    public MeshFilter targetMeshFilter;

    [Tooltip("接触判定を行う半径 (ターゲットとの距離の閾値)")]
    public float collisionRadius = 0.5f;

    [Tooltip("高速化のためのの境界箱に持たせる余白(メートル)。アニメーションの激しさによっては少し広めに取ると安定します。")]
    public float boundsPadding = 0.1f;

    [Tooltip("メッシュ表面での計算時に、検証を間引く頂点数。大きいほど計算が軽いですが精度が落ちます。(例: 10 なら10頂点ごとに計算)")]
    [Range(1, 100)]
    public int vertexSamplingStep = 10;

    [Tooltip("1フレームに記録する接触点の最大数")]
    [Range(1, 64)]
    public int maxContactPoints = 16;

    [Tooltip("点群との衝突を計算するための Compute Shader ファイル")]
    public ComputeShader collisionComputeShader;

    [Header("Debug")]
    [Tooltip("Play中にインスペクタ上で現在接触しているかどうかがリアルタイムに表示されます")]
    public string debugCollisionStatus = "Not Colliding";

    [Tooltip("各接触点について、対応する点群インデックス/メッシュ頂点インデックスをコンソールに出力します")]
    public bool logContactDetails = false;

    [Tooltip("接触点の情報をCSVファイルに保存します")]
    public bool logToCsv = false;

    [Tooltip("CSVの保存先フォルダ。空欄の場合は Application.persistentDataPath/HapCollisionLogs に保存されます")]
    public string csvOutputDirectory = "";

    [Tooltip("接触時にシーンビュー上で境界枠や球（緑/赤）をGizmoとして表示するかどうか")]
    public bool showDebugGizmo = true;

    [Tooltip("接触点(HitPositions)を示すGizmo球の半径")]
    public float hitPointGizmoSize = 0.01f;

    /// <summary>現在、点群がターゲットに接触しているかどうか</summary>
    public bool IsColliding { get; private set; }

    /// <summary>最初の接触位置（後方互換用）</summary>
    public Vector3 HitPosition { get; private set; }

    /// <summary>最初の接触法線（後方互換用）</summary>
    public Vector3 HitNormal { get; private set; }

    /// <summary>全接触点のワールド座標リスト（複数方向への変形に使用）</summary>
    public List<Vector3> HitPositions { get; private set; } = new List<Vector3>();

    /// <summary>各接触点の (pointIndex, vertIndex) のペア。デバッグ・分布確認用</summary>
    public List<Vector2Int> HitDetails { get; private set; } = new List<Vector2Int>();

    [System.Serializable]
    private struct HitResult
    {
        public int isColliding;
        public Vector3 hitPoint;
        public Vector3 hitNormal;
        public int pointIndex;
        public int vertIndex;
    }

    // int(4) + float3(12) + float3(12) + int(4) + int(4) = 36 bytes
    private const int HIT_RESULT_STRIDE = 36;

    private ComputeBuffer _resultBuffer;
    private ComputeBuffer _counterBuffer;
    private HitResult[] _resultData;
    private readonly int[] _counterData = new int[1];
    private readonly int[] _zeroCounter = new int[1];

    private int _kernelTransform;
    private int _kernelMesh;

    private Mesh _bakedMesh;
    private ComputeBuffer _meshVerticesBuffer;
    private ComputeBuffer _meshNormalsBuffer;
    private Vector3[] _meshVertices;
    private Vector3[] _meshNormals;

    private StreamWriter _csvWriter;
    private int _csvFrameIndex;

    private void Start()
    {
        if (collisionComputeShader != null)
        {
            _kernelTransform = collisionComputeShader.FindKernel("CheckCollision");
            _kernelMesh = collisionComputeShader.FindKernel("CheckCollisionMesh");
        }

        _resultData = new HitResult[maxContactPoints];
        _resultBuffer = new ComputeBuffer(maxContactPoints, HIT_RESULT_STRIDE);
        _counterBuffer = new ComputeBuffer(1, sizeof(int));

        if (logToCsv)
        {
            string dir = string.IsNullOrEmpty(csvOutputDirectory)
                ? Path.Combine(Application.persistentDataPath, "HapCollisionLogs")
                : csvOutputDirectory;
            Directory.CreateDirectory(dir);
            string fileName = $"contacts_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string path = Path.Combine(dir, fileName);

            _csvWriter = new StreamWriter(path, false);
            _csvWriter.WriteLine("frame,time,hitIndex,pointIndex,vertIndex,hitX,hitY,hitZ,normalX,normalY,normalZ");
            Debug.Log($"[HapCollisionDetectors] CSVログ出力先: {path}");
        }
    }

    private void Update()
    {
        if (RsGlobalPointCloudManager.Instance == null || collisionComputeShader == null)
            return;

        var globalBuffer = RsGlobalPointCloudManager.Instance.GetGlobalBuffer();
        int pointsCount = RsGlobalPointCloudManager.Instance.CurrentTotalCount;

        if (globalBuffer == null || pointsCount == 0)
        {
            UpdateCollisionResult(false);
            return;
        }

        // スロット確保用カウンタをリセット
        _counterBuffer.SetData(_zeroCounter);

        if (detectionMode == DetectionMode.TransformOnly)
        {
            if (targetObject == null) return;

            collisionComputeShader.SetBuffer(_kernelTransform, "PointCloudBuffer", globalBuffer);
            collisionComputeShader.SetBuffer(_kernelTransform, "Result", _resultBuffer);
            collisionComputeShader.SetBuffer(_kernelTransform, "HitCounter", _counterBuffer);
            collisionComputeShader.SetInt("PointsCount", pointsCount);
            collisionComputeShader.SetInt("MaxHits", maxContactPoints);
            collisionComputeShader.SetVector("TargetPosition", targetObject.position);
            collisionComputeShader.SetFloat("RadiusSqr", collisionRadius * collisionRadius);
            collisionComputeShader.SetFloat("Radius", collisionRadius);

            int threadGroups = Mathf.CeilToInt(pointsCount / 256.0f);
            collisionComputeShader.Dispatch(_kernelTransform, threadGroups, 1, 1);
        }
        else if (detectionMode == DetectionMode.SkinnedMeshRenderer)
        {
            if (targetSkinnedMesh == null) return;

            if (_bakedMesh == null) _bakedMesh = new Mesh();
            targetSkinnedMesh.BakeMesh(_bakedMesh, true);

            _meshVertices = _bakedMesh.vertices;
            _meshNormals = _bakedMesh.normals;
            if (_meshVertices == null || _meshVertices.Length == 0) return;

            if (_meshVerticesBuffer == null || _meshVerticesBuffer.count != _meshVertices.Length)
            {
                _meshVerticesBuffer?.Release();
                _meshNormalsBuffer?.Release();
                _meshVerticesBuffer = new ComputeBuffer(_meshVertices.Length, sizeof(float) * 3);
                _meshNormalsBuffer = new ComputeBuffer(_meshVertices.Length, sizeof(float) * 3);
            }

            _meshVerticesBuffer.SetData(_meshVertices);
            if (_meshNormals != null && _meshNormals.Length == _meshVertices.Length)
                _meshNormalsBuffer.SetData(_meshNormals);

            collisionComputeShader.SetBuffer(_kernelMesh, "PointCloudBuffer", globalBuffer);
            collisionComputeShader.SetBuffer(_kernelMesh, "Result", _resultBuffer);
            collisionComputeShader.SetBuffer(_kernelMesh, "HitCounter", _counterBuffer);
            collisionComputeShader.SetBuffer(_kernelMesh, "MeshVerticesBuffer", _meshVerticesBuffer);
            collisionComputeShader.SetBuffer(_kernelMesh, "MeshNormalsBuffer", _meshNormalsBuffer);
            collisionComputeShader.SetInt("PointsCount", pointsCount);
            collisionComputeShader.SetInt("MaxHits", maxContactPoints);
            collisionComputeShader.SetInt("MeshVerticesCount", _meshVertices.Length);
            collisionComputeShader.SetMatrix("LocalToWorldMatrix", targetSkinnedMesh.transform.localToWorldMatrix);

            Bounds bounds = targetSkinnedMesh.bounds;
            float totalPadding = collisionRadius + boundsPadding;
            collisionComputeShader.SetVector("MeshBoundsMin", bounds.min - Vector3.one * totalPadding);
            collisionComputeShader.SetVector("MeshBoundsMax", bounds.max + Vector3.one * totalPadding);
            collisionComputeShader.SetFloat("Radius", collisionRadius);
            collisionComputeShader.SetFloat("RadiusSqr", collisionRadius * collisionRadius);
            collisionComputeShader.SetInt("VertexSubstep", vertexSamplingStep);

            int threadGroups = Mathf.CeilToInt(pointsCount / 256.0f);
            collisionComputeShader.Dispatch(_kernelMesh, threadGroups, 1, 1);
        }
        else if (detectionMode == DetectionMode.MeshFilter)
        {
            if (targetMeshFilter == null) return;

            _meshVertices = targetMeshFilter.mesh.vertices;
            _meshNormals = targetMeshFilter.mesh.normals;
            if (_meshVertices == null || _meshVertices.Length == 0) return;

            if (_meshVerticesBuffer == null || _meshVerticesBuffer.count != _meshVertices.Length)
            {
                _meshVerticesBuffer?.Release();
                _meshNormalsBuffer?.Release();
                _meshVerticesBuffer = new ComputeBuffer(_meshVertices.Length, sizeof(float) * 3);
                _meshNormalsBuffer = new ComputeBuffer(_meshVertices.Length, sizeof(float) * 3);
            }

            _meshVerticesBuffer.SetData(_meshVertices);
            if (_meshNormals != null && _meshNormals.Length == _meshVertices.Length)
                _meshNormalsBuffer.SetData(_meshNormals);

            collisionComputeShader.SetBuffer(_kernelMesh, "PointCloudBuffer", globalBuffer);
            collisionComputeShader.SetBuffer(_kernelMesh, "Result", _resultBuffer);
            collisionComputeShader.SetBuffer(_kernelMesh, "HitCounter", _counterBuffer);
            collisionComputeShader.SetBuffer(_kernelMesh, "MeshVerticesBuffer", _meshVerticesBuffer);
            collisionComputeShader.SetBuffer(_kernelMesh, "MeshNormalsBuffer", _meshNormalsBuffer);
            collisionComputeShader.SetInt("PointsCount", pointsCount);
            collisionComputeShader.SetInt("MaxHits", maxContactPoints);
            collisionComputeShader.SetInt("MeshVerticesCount", _meshVertices.Length);
            collisionComputeShader.SetMatrix("LocalToWorldMatrix", targetMeshFilter.transform.localToWorldMatrix);

            var rend = targetMeshFilter.GetComponent<Renderer>();
            Bounds bounds = rend != null ? rend.bounds : new Bounds(targetMeshFilter.transform.position, Vector3.one);
            float totalPadding = collisionRadius + boundsPadding;
            collisionComputeShader.SetVector("MeshBoundsMin", bounds.min - Vector3.one * totalPadding);
            collisionComputeShader.SetVector("MeshBoundsMax", bounds.max + Vector3.one * totalPadding);
            collisionComputeShader.SetFloat("Radius", collisionRadius);
            collisionComputeShader.SetFloat("RadiusSqr", collisionRadius * collisionRadius);
            collisionComputeShader.SetInt("VertexSubstep", vertexSamplingStep);

            int threadGroups = Mathf.CeilToInt(pointsCount / 256.0f);
            collisionComputeShader.Dispatch(_kernelMesh, threadGroups, 1, 1);
        }

        // カウンタとスロットの結果を取得
        _counterBuffer.GetData(_counterData);
        int hitCount = Mathf.Min(_counterData[0], maxContactPoints);

        _resultBuffer.GetData(_resultData);
        HitPositions.Clear();
        HitDetails.Clear();

        for (int i = 0; i < hitCount; i++)
        {
            HitPositions.Add(_resultData[i].hitPoint);
            HitDetails.Add(new Vector2Int(_resultData[i].pointIndex, _resultData[i].vertIndex));
        }

        bool anyCollision = hitCount > 0;
        UpdateCollisionResult(anyCollision);

        if (anyCollision)
        {
            HitPosition = _resultData[0].hitPoint;
            HitNormal = _resultData[0].hitNormal;

            if (logContactDetails)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"[HapCollisionDetectors] {hitCount} 点接触: ");
                for (int i = 0; i < hitCount; i++)
                    sb.Append($"(pointIdx={HitDetails[i].x}, vertIdx={HitDetails[i].y}) ");
                Debug.Log(sb.ToString());
            }
        }

        if (_csvWriter != null)
        {
            for (int i = 0; i < hitCount; i++)
            {
                Vector3 p = _resultData[i].hitPoint;
                Vector3 n = _resultData[i].hitNormal;
                _csvWriter.WriteLine(
                    $"{_csvFrameIndex},{Time.time:F4},{i},{HitDetails[i].x},{HitDetails[i].y}," +
                    $"{p.x:F5},{p.y:F5},{p.z:F5},{n.x:F5},{n.y:F5},{n.z:F5}");
            }
            _csvFrameIndex++;
        }
    }

    private void UpdateCollisionResult(bool isColliding)
    {
        IsColliding = isColliding;
        debugCollisionStatus = isColliding
            ? $"COLLIDING! ({HitPositions.Count} 点)"
            : "Not Colliding...";
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showDebugGizmo) return;

        Gizmos.color = IsColliding ? new Color(1f, 0f, 0f, 0.4f) : new Color(0f, 1f, 0f, 0.4f);

        if (detectionMode == DetectionMode.TransformOnly && targetObject != null)
        {
            Gizmos.DrawWireSphere(targetObject.position, collisionRadius);
            Gizmos.color = IsColliding ? new Color(1f, 0f, 0f, 0.2f) : new Color(0f, 1f, 0f, 0.2f);
            Gizmos.DrawSphere(targetObject.position, collisionRadius);
        }
        else if (detectionMode == DetectionMode.SkinnedMeshRenderer && targetSkinnedMesh != null)
        {
            Bounds bounds = targetSkinnedMesh.bounds;
            bounds.Expand(collisionRadius * 2f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
            Gizmos.color = IsColliding ? new Color(1f, 0f, 0f, 0.2f) : new Color(0f, 1f, 0f, 0.2f);
            Gizmos.DrawCube(bounds.center, bounds.size);
        }
        else if (detectionMode == DetectionMode.MeshFilter && targetMeshFilter != null)
        {
            var rend = targetMeshFilter.GetComponent<Renderer>();
            if (rend != null)
            {
                Bounds bounds = rend.bounds;
                bounds.Expand(collisionRadius * 2f);
                Gizmos.DrawWireCube(bounds.center, bounds.size);
                Gizmos.color = IsColliding ? new Color(1f, 0f, 0f, 0.2f) : new Color(0f, 1f, 0f, 0.2f);
                Gizmos.DrawCube(bounds.center, bounds.size);
            }
        }

        // 各接触点を個別に表示
        foreach (var pos in HitPositions)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(pos, hitPointGizmoSize);
        }
    }
#endif

    private void OnDestroy()
    {
        _resultBuffer?.Release();
        _counterBuffer?.Release();
        _meshVerticesBuffer?.Release();
        _meshNormalsBuffer?.Release();
        if (_bakedMesh != null) Destroy(_bakedMesh);

        if (_csvWriter != null)
        {
            _csvWriter.Flush();
            _csvWriter.Close();
            _csvWriter = null;
        }
    }
}
