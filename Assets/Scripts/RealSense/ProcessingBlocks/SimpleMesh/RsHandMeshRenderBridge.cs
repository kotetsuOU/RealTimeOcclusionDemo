using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 複数のRsHandMeshBlockからのデータを管理し、RenderFeatureに橋渡しするシングルトン
/// </summary>
public class RsHandMeshRenderBridge : MonoBehaviour
{
    public static RsHandMeshRenderBridge Instance { get; private set; }

    /// <summary>
    /// 各HandMeshのデータを保持するクラス
    /// </summary>
    public class HandMeshData
    {
        public ComputeBuffer VertexBuffer;
        public ComputeBuffer IndexBuffer;
        public ComputeBuffer ArgsBuffer;
        public MaterialPropertyBlock PropertyBlock;
        public Matrix4x4 LocalToWorld = Matrix4x4.identity;
        public bool HasData => VertexBuffer != null && IndexBuffer != null && ArgsBuffer != null;

        public HandMeshData()
        {
            PropertyBlock = new MaterialPropertyBlock();
        }

        public void UpdateBuffers(ComputeBuffer vertexBuffer, ComputeBuffer indexBuffer, ComputeBuffer argsBuffer)
        {
            VertexBuffer = vertexBuffer;
            IndexBuffer = indexBuffer;
            ArgsBuffer = argsBuffer;
            if (vertexBuffer != null)
            {
                PropertyBlock.SetBuffer("_VertexBuffer", vertexBuffer);
            }
            if (indexBuffer != null)
            {
                PropertyBlock.SetBuffer("_IndexBuffer", indexBuffer);
            }
            PropertyBlock.SetFloat("_UseProceduralBuffers", 1f);
        }
    }

    private readonly Dictionary<int, HandMeshData> _handMeshes = new Dictionary<int, HandMeshData>();

    /// <summary>
    /// 登録されている全HandMeshデータへの読み取り専用アクセス
    /// </summary>
    public IReadOnlyDictionary<int, HandMeshData> HandMeshes => _handMeshes;

    /// <summary>
    /// 少なくとも1つのHandMeshが有効なデータを持っているか
    /// </summary>
    public bool HasAnyData
    {
        get
        {
            foreach (var kvp in _handMeshes)
            {
                if (kvp.Value.HasData) return true;
            }
            return false;
        }
    }

    void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// 各 RsHandMeshBlock が自身のInstanceIDで登録
    /// </summary>
    public void UpdateBuffers(int sourceId, ComputeBuffer vertexBuffer, ComputeBuffer indexBuffer, ComputeBuffer argsBuffer)
    {
        if (!_handMeshes.TryGetValue(sourceId, out var data))
        {
            data = new HandMeshData();
            _handMeshes[sourceId] = data;
        }
        data.UpdateBuffers(vertexBuffer, indexBuffer, argsBuffer);
    }

    public void UpdateBuffers(int sourceId, ComputeBuffer vertexBuffer, ComputeBuffer indexBuffer, ComputeBuffer argsBuffer, Matrix4x4 localToWorld)
    {
        if (!_handMeshes.TryGetValue(sourceId, out var data))
        {
            data = new HandMeshData();
            _handMeshes[sourceId] = data;
        }
        data.UpdateBuffers(vertexBuffer, indexBuffer, argsBuffer);
        data.LocalToWorld = localToWorld;
    }

    /// <summary>
    /// ソースを登録解除
    /// </summary>
    public void RemoveSource(int sourceId)
    {
        _handMeshes.Remove(sourceId);
    }

    void OnDestroy()
    {
        _handMeshes.Clear();
        if (Instance == this) Instance = null;
    }
}