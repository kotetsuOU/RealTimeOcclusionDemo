using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// RsGlobalPointCloudManager で統合された点群データに対して、指定した物体やメッシュが接触しているかを計算するクラス。
/// ComputeShader を使用し、大量の点群計算（数百万点規模）でもパフォーマンスを落とさずに接触判定を行います。
/// 複数接触点対応: 球を8オクタントに分割し、各方向の接触位置を個別に検出します。
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

    [Tooltip("点群との衝突を計算するための Compute Shader ファイル")]
    public ComputeShader collisionComputeShader;

    [Header("Debug")]
    [Tooltip("Play中にインスペクタ上で現在接触しているかどうかがリアルタイムに表示されます")]
    public string debugCollisionStatus = "Not Colliding";

    [Tooltip("接触時にシーンビュー上で境界枠や球（緑/赤）をGizmoとして表示するかどうか")]
    public bool showDebugGizmo = true;

    [Tooltip("接触点(HitPositions)を示すGizmo球の半径")]
    public float hitPointGizmoSize = 0.01f;

    private const int NUM_SECTORS = 8;

    /// <summary>現在、点群がターゲットに接触しているかどうか</summary>
    public bool IsColliding { get; private set; }

    /// <summary>最初のオクタントの接触位置（後方互換用）</summary>
    public Vector3 HitPosition { get; private set; }

    /// <summary>最初のオクタントの接触法線（後方互換用）</summary>
    public Vector3 HitNormal { get; private set; }

    /// <summary>全オクタントの接触位置リスト（複数方向への変形に使用）</summary>
    public List<Vector3> HitPositions { get; private set; } = new List<Vector3>();

    [System.Serializable]
    private struct HitResult
    {
        public int isColliding;
        public Vector3 hitPoint;
        public Vector3 hitNormal;
    }

    private ComputeBuffer _resultBuffer;
    private HitResult[] _resultData = new HitResult[NUM_SECTORS];

    private int _kernelTransform;
    private int _kernelMesh;

    private Mesh _bakedMesh;
    private ComputeBuffer _meshVerticesBuffer;
    private ComputeBuffer _meshNormalsBuffer;
    private Vector3[] _meshVertices;
    private Vector3[] _meshNormals;

    private void Start()
    {
        if (collisionComputeShader != null)
        {
            _kernelTransform = collisionComputeShader.FindKernel("CheckCollision");
            _kernelMesh = collisionComputeShader.FindKernel("CheckCollisionMesh");
        }

        // int(4) + float3(12) + float3(12) = 28 bytes × NUM_SECTORS
        _resultBuffer = new ComputeBuffer(NUM_SECTORS, 28);
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

        // 全セクターをリセット
        for (int i = 0; i < NUM_SECTORS; i++)
            _resultData[i] = new HitResult { isColliding = 0, hitPoint = Vector3.zero, hitNormal = Vector3.zero };
        _resultBuffer.SetData(_resultData);

        if (detectionMode == DetectionMode.TransformOnly)
        {
            if (targetObject == null) return;

            collisionComputeShader.SetBuffer(_kernelTransform, "PointCloudBuffer", globalBuffer);
            collisionComputeShader.SetBuffer(_kernelTransform, "Result", _resultBuffer);
            collisionComputeShader.SetInt("PointsCount", pointsCount);
            collisionComputeShader.SetVector("TargetPosition", targetObject.position);
            collisionComputeShader.SetVector("SphereCenterWorld", targetObject.position);
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
            collisionComputeShader.SetBuffer(_kernelMesh, "MeshVerticesBuffer", _meshVerticesBuffer);
            collisionComputeShader.SetBuffer(_kernelMesh, "MeshNormalsBuffer", _meshNormalsBuffer);
            collisionComputeShader.SetInt("PointsCount", pointsCount);
            collisionComputeShader.SetInt("MeshVerticesCount", _meshVertices.Length);
            collisionComputeShader.SetMatrix("LocalToWorldMatrix", targetSkinnedMesh.transform.localToWorldMatrix);
            collisionComputeShader.SetVector("SphereCenterWorld", targetSkinnedMesh.bounds.center);

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
            collisionComputeShader.SetBuffer(_kernelMesh, "MeshVerticesBuffer", _meshVerticesBuffer);
            collisionComputeShader.SetBuffer(_kernelMesh, "MeshNormalsBuffer", _meshNormalsBuffer);
            collisionComputeShader.SetInt("PointsCount", pointsCount);
            collisionComputeShader.SetInt("MeshVerticesCount", _meshVertices.Length);
            collisionComputeShader.SetMatrix("LocalToWorldMatrix", targetMeshFilter.transform.localToWorldMatrix);
            collisionComputeShader.SetVector("SphereCenterWorld", targetMeshFilter.transform.position);

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

        // 全セクターの結果を収集
        _resultBuffer.GetData(_resultData);
        HitPositions.Clear();
        bool anyCollision = false;

        for (int i = 0; i < NUM_SECTORS; i++)
        {
            if (_resultData[i].isColliding > 0)
            {
                anyCollision = true;
                HitPositions.Add(_resultData[i].hitPoint);
            }
        }

        UpdateCollisionResult(anyCollision);

        if (anyCollision)
        {
            HitPosition = HitPositions[0];
            HitNormal = _resultData[System.Array.FindIndex(_resultData, r => r.isColliding > 0)].hitNormal;
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
        _meshVerticesBuffer?.Release();
        _meshNormalsBuffer?.Release();
        if (_bakedMesh != null) Destroy(_bakedMesh);
    }
}
