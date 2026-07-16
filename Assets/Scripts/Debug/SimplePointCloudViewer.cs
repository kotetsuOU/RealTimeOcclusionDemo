using System.Collections.Generic;
using Intel.RealSense;
using UnityEngine;

// GameObjectのTransformにそのまま追従する、素朴な点群デバッグビューア。
// 頂点はカメラのローカル座標のままメッシュに詰め、ワールド変換はUnity標準のレンダリングに任せる
// (RsPointCloudRendererのようにComputeShaderで独自にlocalToWorldを焼き込んだり、
//  ワールド座標の固定範囲でカリングしたりしない)。
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SimplePointCloudViewer : MonoBehaviour
{
    [Header("Dependencies")]
    public RsProcessingPipe processingPipe;

    [Header("Point Filter (local space, カメラからの距離)")]
    public float minDistance = 0.3f;
    public float maxDistance = 3f;

    [Header("Appearance")]
    public Color pointColor = new Color(241f / 255f, 187f / 255f, 147f / 255f, 1f);

    private RsDataProvider _dataProvider;
    private MeshFilter _meshFilter;
    private Mesh _mesh;

    private Vector3[] _rawVertices;
    private readonly List<Vector3> _filteredVertices = new List<Vector3>();
    private readonly List<Color> _colors = new List<Color>();
    private readonly List<int> _indices = new List<int>();

    void Start()
    {
        _meshFilter = GetComponent<MeshFilter>();

        var meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer.sharedMaterial == null)
        {
            meshRenderer.material = new Material(Shader.Find("Unlit/PointCloudViewer"));
        }

        _mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        _meshFilter.mesh = _mesh;

        _dataProvider = new RsDataProvider(processingPipe);
        _dataProvider.Start();
        processingPipe.OnStart += OnStartStreaming;
    }

    private void OnStartStreaming(PipelineProfile profile)
    {
        int rsLength = _dataProvider.FrameWidth * _dataProvider.FrameHeight;
        _rawVertices = new Vector3[rsLength];

        _filteredVertices.Capacity = rsLength;
        _colors.Capacity = rsLength;
        _indices.Capacity = rsLength;
    }

    void Update()
    {
        if (_dataProvider == null || _rawVertices == null) return;
        if (!_dataProvider.PollForFrame(out var points)) return;

        using (points)
        {
            points.CopyVertices(_rawVertices);
        }

        float minSq = minDistance * minDistance;
        float maxSq = maxDistance * maxDistance;

        _filteredVertices.Clear();
        _colors.Clear();
        _indices.Clear();

        for (int i = 0; i < _rawVertices.Length; i++)
        {
            Vector3 v = _rawVertices[i];
            float distSq = v.sqrMagnitude;
            if (distSq < minSq || distSq > maxSq) continue;

            _indices.Add(_filteredVertices.Count);
            _filteredVertices.Add(v);
            _colors.Add(pointColor);
        }

        _mesh.Clear();
        _mesh.SetVertices(_filteredVertices);
        _mesh.SetColors(_colors);
        _mesh.SetIndices(_indices, MeshTopology.Points, 0);
        _mesh.RecalculateBounds();
    }

    void OnDestroy()
    {
        if (processingPipe != null) processingPipe.OnStart -= OnStartStreaming;
        _dataProvider?.Dispose();
    }
}
