using UnityEngine;

public enum RsHandMeshColorMode
{
    RealSense,
    Skin,
    Black
}

public class RsHandMeshMeshRenderer : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("Assign a component that exposes LatestPositions/LatestColors/LatestIndices/LatestIndexCount (e.g., an adapter MonoBehaviour).")]
    public UnityEngine.Object Source;

    [Tooltip("The Transform of the camera/sensor that produced the positions (camera-local → world). If null, positions are assumed to be in world space.")]
    public Transform SourceTransform;

    [Header("Material")]
    public Material Material;

    [Header("Color")]
    public RsHandMeshColorMode ColorMode = RsHandMeshColorMode.RealSense;

    [Header("Update")]
    [Tooltip("If enabled, updates mesh in LateUpdate to reduce contention with other scripts.")]
    public bool UpdateInLateUpdate = true;

    [Tooltip("Limit how often the mesh is rebuilt (Hz). 0 = every frame.")]
    public float MaxUpdateRateHz = 0f;

    [Tooltip("If enabled, the mesh data is copied into internal arrays before building the mesh (recommended if producer can swap arrays).")]
    public bool CopySourceArrays = true;

    [Header("Bounds Filter")]
    [Tooltip("If enabled, triangles with any vertex outside the bounds are discarded.")]
    public bool UseBoundsFilter = false;

    [Tooltip("If set, uses the scan range from RsDeviceController (local space). Otherwise uses ManualBounds.")]
    public RsDeviceController BoundsSource;

    [Tooltip("Manual world-space bounds when BoundsSource is not set.")]
    public Bounds ManualBounds = new Bounds(Vector3.zero, Vector3.one);

    [Header("Debug")]
    public bool LogOnce;
    bool _loggedMesh;
    bool _loggedMissingProps;
    float _nextNoSourceLogTime;

    Mesh _mesh;
    MeshFilter _filter;
    MeshRenderer _renderer;
    MaterialPropertyBlock _propertyBlock;

    Vector3[] _positions;
    Color[] _colors;
    int[] _indices;
    int[] _filteredIndices;

    float _nextUpdateTime;

    System.Type _sourceType;
    System.Reflection.PropertyInfo _pPositions;
    System.Reflection.PropertyInfo _pColors;
    System.Reflection.PropertyInfo _pIndices;
    System.Reflection.PropertyInfo _pIndexCount;

    static readonly int s_useVertexColorId = Shader.PropertyToID("_UseVertexColor");
    static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");
    readonly Color _skinColor = new Color(241f / 255f, 187f / 255f, 147f / 255f, 1f);
    readonly Color _blackColor = Color.black;

    void OnEnable()
    {
        CacheSourceAccessors();

        if (LogOnce)
            Debug.Log($"[RsHandMeshMeshRenderer] Enabled. name={name} source={(Source != null ? Source.ToString() : "<null>")}");

        _filter = GetComponent<MeshFilter>();
        if (_filter == null) _filter = gameObject.AddComponent<MeshFilter>();

        _renderer = GetComponent<MeshRenderer>();
        if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();
        if (_propertyBlock == null) _propertyBlock = new MaterialPropertyBlock();

        if (Material != null)
            _renderer.sharedMaterial = Material;
        else if (_renderer.sharedMaterial == null)
        {
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
        ApplyColorMode();
    }

    void OnDisable()
    {
        if (_mesh != null)
        {
            Destroy(_mesh);
            _mesh = null;
        }
    }

    private bool _transformSynced = false;

    void Update()
    {
        if (!UpdateInLateUpdate)
        {
            if (!_transformSynced) SyncTransformToBlock();
            TryUpdateMesh();
        }
    }

    void LateUpdate()
    {
        if (UpdateInLateUpdate)
        {
            if (!_transformSynced) SyncTransformToBlock();
            TryUpdateMesh();
        }
    }

    void SyncTransformToBlock()
    {
        // 開発者の意図を反映し、カメラ位置が固定であることを前提にキャッシュへ直接書き込む（1回のみ）
        if (Source is RsHandMeshBlockSource src && src.Block is RsHandMeshBlock handBlock)
        {
            handBlock.SetCachedTransform(SourceTransform != null ? SourceTransform.localToWorldMatrix : Matrix4x4.identity);
            _transformSynced = true;
        }
    }

    void TryUpdateMesh()
    {
        bool useSourceColors = ColorMode == RsHandMeshColorMode.RealSense;

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

        if (_pPositions == null || _pIndices == null || _pIndexCount == null || (useSourceColors && _pColors == null))
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
        var srcColors = useSourceColors ? _pColors.GetValue(Source, null) as Color[] : null;
        var srcIndices = _pIndices.GetValue(Source, null) as int[];
        var indexCountObj = _pIndexCount.GetValue(Source, null);
        var indexCount = indexCountObj is int i ? i : 0;

        if (srcPositions == null || srcIndices == null || indexCount <= 0)
        {
            if (_mesh != null)
                _mesh.Clear(false);
            
            if (_renderer != null && _renderer.enabled)
                _renderer.enabled = false;
            
            return;
        }

        if (_renderer != null && !_renderer.enabled)
            _renderer.enabled = true;
        
        
        if (useSourceColors && srcColors == null) return;

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
                if (useSourceColors)
                    _colors = new Color[srcPositions.Length];
            }

            if (useSourceColors && (_colors == null || _colors.Length != srcColors.Length))
                _colors = new Color[srcColors.Length];

            if (_indices == null || _indices.Length != indexCount)
                _indices = new int[indexCount];

            System.Array.Copy(srcPositions, _positions, _positions.Length);
            if (useSourceColors)
                System.Array.Copy(srcColors, _colors, _colors.Length);
            System.Array.Copy(srcIndices, _indices, indexCount);
        }
        else
        {
            _positions = srcPositions;
            _colors = useSourceColors ? srcColors : _colors;
            _indices = srcIndices;
        }

        // Filter triangles by bounds (positions are still in camera-local space here)
        if (UseBoundsFilter)
        {
            Bounds effectiveBounds = GetEffectiveBounds();
            bool useBoundsSourceLocal = BoundsSource != null;
            Matrix4x4 camL2W = SourceTransform != null ? SourceTransform.localToWorldMatrix : Matrix4x4.identity;
            // Convert camera-local → bounds-local in one step
            Matrix4x4 toBoundsSpace = useBoundsSourceLocal
                ? BoundsSource.transform.worldToLocalMatrix * camL2W
                : camL2W;

            int filteredCount = 0;
            if (_filteredIndices == null || _filteredIndices.Length < indexCount)
                _filteredIndices = new int[indexCount];

            for (int ti = 0; ti + 2 < indexCount; ti += 3)
            {
                Vector3 p0 = toBoundsSpace.MultiplyPoint3x4(_positions[_indices[ti]]);
                Vector3 p1 = toBoundsSpace.MultiplyPoint3x4(_positions[_indices[ti + 1]]);
                Vector3 p2 = toBoundsSpace.MultiplyPoint3x4(_positions[_indices[ti + 2]]);

                if (effectiveBounds.Contains(p0) && effectiveBounds.Contains(p1) && effectiveBounds.Contains(p2))
                {
                    _filteredIndices[filteredCount++] = _indices[ti];
                    _filteredIndices[filteredCount++] = _indices[ti + 1];
                    _filteredIndices[filteredCount++] = _indices[ti + 2];
                }
            }

            indexCount = filteredCount;
            _indices = _filteredIndices;
        }

        // Convert positions: camera-local → world → mesh-local
        // sourceL2W: camera-local to world (identity if no SourceTransform)
        // w2l: world to this mesh object's local space
        Matrix4x4 sourceL2W = SourceTransform != null ? SourceTransform.localToWorldMatrix : Matrix4x4.identity;
        var toMeshLocal = transform.worldToLocalMatrix * sourceL2W;
        for (int vi = 0; vi < _positions.Length; vi++)
            _positions[vi] = toMeshLocal.MultiplyPoint3x4(_positions[vi]);

        _mesh.Clear(false);
        _mesh.vertices = _positions;
        if (useSourceColors && _colors != null)
            _mesh.colors = _colors;
        _mesh.SetIndices(_indices, 0, indexCount, MeshTopology.Triangles, 0, true);
        
        // Recalculate normals for proper lighting
        _mesh.RecalculateNormals();
        
        _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
        ApplyColorMode();
    }

    public void ChangeColorMode(RsHandMeshColorMode mode)
    {
        ColorMode = mode;
        ApplyColorMode();
    }

    public void ApplyColorMode()
    {
        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();

        if (_renderer == null)
            return;

        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        _renderer.GetPropertyBlock(_propertyBlock);

        switch (ColorMode)
        {
            case RsHandMeshColorMode.RealSense:
                _propertyBlock.SetFloat(s_useVertexColorId, 1f);
                _propertyBlock.SetColor(s_baseColorId, Color.white);
                break;

            case RsHandMeshColorMode.Skin:
                _propertyBlock.SetFloat(s_useVertexColorId, 0f);
                _propertyBlock.SetColor(s_baseColorId, _skinColor);
                break;

            case RsHandMeshColorMode.Black:
                _propertyBlock.SetFloat(s_useVertexColorId, 0f);
                _propertyBlock.SetColor(s_baseColorId, _blackColor);
                break;
        }

        _renderer.SetPropertyBlock(_propertyBlock);
    }

    void CacheSourceAccessors()
    {
        if (Source is GameObject go)
        {
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

    Bounds GetEffectiveBounds()
    {
        if (BoundsSource == null)
            return ManualBounds;

        float margin = BoundsSource.FrameWidth;
        Vector3 scanRange = BoundsSource.RealSenseScanRange;
        Vector3 min = new Vector3(margin, margin, margin);
        Vector3 max = scanRange - min;
        Vector3 size = max - min;
        size.x = Mathf.Max(0f, size.x);
        size.y = Mathf.Max(0f, size.y);
        size.z = Mathf.Max(0f, size.z);
        Vector3 center = min + size * 0.5f;
        return new Bounds(center, size);
    }
}
