using UnityEngine;

public class RsHandMeshMeshRenderer : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("Assign a component that exposes LatestPositions/LatestColors/LatestIndices/LatestIndexCount (e.g., an adapter MonoBehaviour).")]
    public UnityEngine.Object Source;

    [Header("Material")]
    public Material Material;

    [Header("Update")]
    [Tooltip("If enabled, updates mesh in LateUpdate to reduce contention with other scripts.")]
    public bool UpdateInLateUpdate = true;

    [Tooltip("Limit how often the mesh is rebuilt (Hz). 0 = every frame.")]
    public float MaxUpdateRateHz = 0f;

    [Tooltip("If enabled, the mesh data is copied into internal arrays before building the mesh (recommended if producer can swap arrays).")]
    public bool CopySourceArrays = true;

    [Header("Debug")]
    public bool LogOnce;
    bool _loggedMesh;
    bool _loggedMissingProps;
    float _nextNoSourceLogTime;

    Mesh _mesh;
    MeshFilter _filter;
    MeshRenderer _renderer;

    Vector3[] _positions;
    Color[] _colors;
    int[] _indices;

    float _nextUpdateTime;

    System.Type _sourceType;
    System.Reflection.PropertyInfo _pPositions;
    System.Reflection.PropertyInfo _pColors;
    System.Reflection.PropertyInfo _pIndices;
    System.Reflection.PropertyInfo _pIndexCount;

    void OnEnable()
    {
        CacheSourceAccessors();

        if (LogOnce)
            Debug.Log($"[RsHandMeshMeshRenderer] Enabled. name={name} source={(Source != null ? Source.ToString() : "<null>")}");

        _filter = GetComponent<MeshFilter>();
        if (_filter == null) _filter = gameObject.AddComponent<MeshFilter>();

        _renderer = GetComponent<MeshRenderer>();
        if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();

        if (Material != null)
            _renderer.sharedMaterial = Material;
        else if (_renderer.sharedMaterial == null)
        {
            // Use vertex color shader for proper mesh rendering
            var shader = Shader.Find("Custom/RsHandMeshVertexColor");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            _renderer.sharedMaterial = new Material(shader);
        }

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "RsHandMesh" };
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _mesh.MarkDynamic();
        }

        _filter.sharedMesh = _mesh;
    }

    void OnDisable()
    {
        if (_mesh != null)
        {
            Destroy(_mesh);
            _mesh = null;
        }
    }

    void Update()
    {
        if (!UpdateInLateUpdate)
            TryUpdateMesh();
    }

    void LateUpdate()
    {
        if (UpdateInLateUpdate)
            TryUpdateMesh();
    }

    void TryUpdateMesh()
    {
        if (Source == null || _mesh == null)
        {
            if (LogOnce && Source == null && Time.unscaledTime >= _nextNoSourceLogTime)
            {
                _nextNoSourceLogTime = Time.unscaledTime + 1f;
                Debug.LogWarning($"[RsHandMeshMeshRenderer] Source is null. name={name}");
            }
            return;
        }

        if (_sourceType == null)
            CacheSourceAccessors();

        if (_pPositions == null || _pColors == null || _pIndices == null || _pIndexCount == null)
        {
            if (LogOnce && !_loggedMissingProps)
            {
                _loggedMissingProps = true;
                Debug.LogWarning($"[RsHandMeshMeshRenderer] Missing source properties. SourceType={_sourceType}");
            }
            return;
        }

        if (MaxUpdateRateHz > 0f)
        {
            if (Time.unscaledTime < _nextUpdateTime) return;
            _nextUpdateTime = Time.unscaledTime + (1f / MaxUpdateRateHz);
        }

        var srcPositions = _pPositions.GetValue(Source, null) as Vector3[];
        var srcColors = _pColors.GetValue(Source, null) as Color[];
        var srcIndices = _pIndices.GetValue(Source, null) as int[];
        var indexCountObj = _pIndexCount.GetValue(Source, null);
        var indexCount = indexCountObj is int i ? i : 0;

        if (srcPositions == null || srcColors == null || srcIndices == null) return;
        if (indexCount <= 0) return;

        if (LogOnce && !_loggedMesh)
        {
            _loggedMesh = true;
            
            // Calculate bounding box of source positions
            Vector3 minPos = srcPositions[0];
            Vector3 maxPos = srcPositions[0];
            int validCount = 0;
            for (int vi = 0; vi < srcPositions.Length; vi++)
            {
                var p = srcPositions[vi];
                if (p.sqrMagnitude > 0.0001f) // Skip zero positions
                {
                    validCount++;
                    minPos = Vector3.Min(minPos, p);
                    maxPos = Vector3.Max(maxPos, p);
                }
            }
        }

        if (CopySourceArrays)
        {
            if (_positions == null || _positions.Length != srcPositions.Length)
            {
                _positions = new Vector3[srcPositions.Length];
                _colors = new Color[srcColors.Length];
            }

            if (_indices == null || _indices.Length != indexCount)
                _indices = new int[indexCount];

            System.Array.Copy(srcPositions, _positions, _positions.Length);
            System.Array.Copy(srcColors, _colors, _colors.Length);
            System.Array.Copy(srcIndices, _indices, indexCount);
        }
        else
        {
            _positions = srcPositions;
            _colors = srcColors;
            _indices = srcIndices;
        }

        // Mesh vertices should be in this object's local space.
        var w2l = transform.worldToLocalMatrix;
        for (int vi = 0; vi < _positions.Length; vi++)
            _positions[vi] = w2l.MultiplyPoint3x4(_positions[vi]);

        _mesh.Clear(false);
        _mesh.vertices = _positions;
        _mesh.colors = _colors;
        _mesh.SetIndices(_indices, 0, indexCount, MeshTopology.Triangles, 0, true);
        
        // Recalculate normals for proper lighting
        _mesh.RecalculateNormals();
        
        // Set a large bounding box to avoid frustum culling issues
        _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
    }

    void CacheSourceAccessors()
    {
        // If user assigned a GameObject, try to find a suitable component on it.
        if (Source is GameObject go)
        {
            // Prefer RsHandMeshBlockSource if present
            var mb = go.GetComponent<MonoBehaviour>();
            foreach (var c in go.GetComponents<MonoBehaviour>())
            {
                if (c != null && c.GetType().Name == "RsHandMeshBlockSource")
                {
                    mb = c;
                    break;
                }
            }

            if (mb != null)
                Source = mb;
        }

        _sourceType = Source != null ? Source.GetType() : null;
        if (_sourceType == null)
        {
            _pPositions = null;
            _pColors = null;
            _pIndices = null;
            _pIndexCount = null;
            return;
        }

        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
        _pPositions = _sourceType.GetProperty("LatestPositions", flags);
        _pColors = _sourceType.GetProperty("LatestColors", flags);
        _pIndices = _sourceType.GetProperty("LatestIndices", flags);
        _pIndexCount = _sourceType.GetProperty("LatestIndexCount", flags);
    }
}
