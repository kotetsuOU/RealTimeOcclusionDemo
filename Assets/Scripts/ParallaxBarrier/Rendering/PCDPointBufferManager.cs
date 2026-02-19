using System.Collections.Generic;
using UnityEngine;

public class PCDPointBufferManager
{
    public struct Point
    {
        public Vector3 position;
        public Vector3 color;
        public uint originType; // 0 = PointCloud, 1 = StaticMesh
    }

    private class MeshTransformPair
    {
        public Mesh mesh;
        public Transform transform;
        public PCDProcessingMode mode;
    }

    // --- Internal Buffer Management (Static Meshes & CPU PointCloud) ---
    private ComputeBuffer _pointBuffer;
    private int _pointCount = 0;
    private Point[] _pointsCache;
    private bool _isDataDirty = false;

    // --- External Buffer Management (GPU Integration) ---
    private ComputeBuffer _externalPointBuffer;
    private int _externalPointCount = 0;
    private bool _useExternalBuffer = false;

    // --- Combined Buffer (External + Internal) ---
    private ComputeBuffer _combinedBuffer;

    private PCV_Data _dynamicData;
    private List<MeshTransformPair> _staticMeshes = new List<MeshTransformPair>();
    private const int STRIDE = 28; // sizeof(float)*3 + sizeof(float)*3 + sizeof(uint)

    public ComputeBuffer PointBuffer => _pointBuffer;
    public int PointCount => _pointCount;
    public ComputeBuffer ExternalPointBuffer => _externalPointBuffer;
    public int ExternalPointCount => _externalPointCount;
    public bool UseExternalBuffer => _useExternalBuffer;
    public ComputeBuffer CombinedBuffer => _combinedBuffer;
    public bool IsDataDirty => _isDataDirty; // Exposed for debugging or optimization checks

    public void SetExternalBuffer(ComputeBuffer buffer, int count)
    {
        bool prevUse = _useExternalBuffer;

        if (buffer != null && buffer.IsValid())
        {
            _externalPointBuffer = buffer;
            _externalPointCount = count;
            _useExternalBuffer = true;
        }
        else
        {
            _useExternalBuffer = false;
            _externalPointBuffer = null;
            _externalPointCount = 0;
        }

        if (prevUse != _useExternalBuffer)
        {
            _isDataDirty = true;
        }
    }

    public void SetPointCloudData(PCV_Data data)
    {
        if (_dynamicData != data || (data != null && _dynamicData != null && _dynamicData.PointCount != data.PointCount))
        {
            _dynamicData = data;
            _isDataDirty = true;
        }
        else if (data == null && _dynamicData != null)
        {
            _dynamicData = null;
            _isDataDirty = true;
        }
    }

    public void AddStaticMesh(Mesh mesh, Transform transform, PCDProcessingMode mode)
    {
        if (mesh != null && transform != null)
        {
            var existing = _staticMeshes.Find(p => p.mesh == mesh && p.transform == transform);
            if (existing == null)
            {
                _staticMeshes.Add(new MeshTransformPair { mesh = mesh, transform = transform, mode = mode });
                _isDataDirty = true;
                UnityEngine.Debug.Log($"[PCDPointBufferManager] Static mesh '{mesh.name}' added from Transform '{transform.name}'.");
            }
            else if (existing.mode != mode)
            {
                existing.mode = mode;
                _isDataDirty = true;
            }
        }
    }

    public void RemoveStaticMesh(Mesh mesh, Transform transform)
    {
        var pair = _staticMeshes.Find(p => p.mesh == mesh && p.transform == transform);
        if (pair != null)
        {
            _staticMeshes.Remove(pair);
            _isDataDirty = true;
            UnityEngine.Debug.Log($"[PCDPointBufferManager] Static mesh '{mesh.name}' removed from Transform '{transform.name}'.");
        }
    }

    public bool HasDepthMapMeshes()
    {
        return _staticMeshes.Exists(p => p.mode == PCDProcessingMode.DepthMap);
    }

    public bool HasPointCloudMeshes()
    {
        return _staticMeshes.Exists(p => p.mode == PCDProcessingMode.PointCloud);
    }
    
    public void Update()
    {
        if (_isDataDirty)
        {
            MergeAndCachePoints();
            UpdateComputeBuffer();
        }
    }

    public void EnsureCombinedBuffer(int totalCount)
    {
        if (_combinedBuffer == null || _combinedBuffer.count < totalCount || !_combinedBuffer.IsValid())
        {
            _combinedBuffer?.Release();
            _combinedBuffer = new ComputeBuffer(totalCount, STRIDE);
        }
    }

    private void MergeAndCachePoints()
    {
        int dataPointCount = 0;
        if (!_useExternalBuffer && _dynamicData != null && _dynamicData.PointCount > 0)
        {
            dataPointCount = _dynamicData.PointCount;
        }

        int totalMeshPointCount = 0;
        foreach (var pair in _staticMeshes)
        {
            if (pair.mesh == null || pair.transform == null) continue;
            if (!pair.mesh.isReadable) continue;
            // Only add PointCloud mode meshes to point buffer
            if (pair.mode != PCDProcessingMode.PointCloud) continue;
            totalMeshPointCount += pair.mesh.vertexCount;
        }

        _pointCount = dataPointCount + totalMeshPointCount;

        if (_pointCount == 0)
        {
            _pointsCache = null;
            return;
        }

        if (_pointsCache == null || _pointsCache.Length != _pointCount)
        {
            _pointsCache = new Point[_pointCount];
        }

        int cacheIndex = 0;

        if (dataPointCount > 0)
        {
            for (int i = 0; i < dataPointCount; i++)
            {
                _pointsCache[cacheIndex] = new Point
                {
                    position = _dynamicData.Vertices[i],
                    color = new Vector3(_dynamicData.Colors[i].r, _dynamicData.Colors[i].g, _dynamicData.Colors[i].b),
                    originType = 0
                };
                cacheIndex++;
            }
        }

        Vector3 defaultColor = new Vector3(1.0f, 1.0f, 1.0f);
        foreach (var pair in _staticMeshes)
        {
            if (pair.mesh == null || !pair.mesh.isReadable || pair.transform == null) continue;
            // Only add PointCloud mode meshes to point buffer
            if (pair.mode != PCDProcessingMode.PointCloud) continue;

            int meshPointCount = pair.mesh.vertexCount;
            if (meshPointCount == 0) continue;

            Vector3[] meshVertices = pair.mesh.vertices;
            Color[] meshColors = pair.mesh.colors;
            bool hasMeshColors = meshColors != null && meshColors.Length == meshPointCount;

            Matrix4x4 localToWorld = pair.transform.localToWorldMatrix;

            for (int i = 0; i < meshPointCount; i++)
            {
                Vector3 color = hasMeshColors ? new Vector3(meshColors[i].r, meshColors[i].g, meshColors[i].b) : defaultColor;
                Vector3 worldPos = localToWorld.MultiplyPoint3x4(meshVertices[i]);

                _pointsCache[cacheIndex] = new Point
                {
                    position = worldPos,
                    color = color,
                    originType = 1
                };
                cacheIndex++;
            }
        }

        if (_isDataDirty)
        {
            string mode = _useExternalBuffer ? "External(GPU) + Static" : "Internal(CPU) + Static";
            // Reduce repetitive logs if needed, but keeping for parity
            // UnityEngine.Debug.Log($"[PCDPointBufferManager] Merged points [{mode}] - Dynamic(CPU): {dataPointCount}, Static Meshes: {totalMeshPointCount}, InternalTotal: {_pointCount}");
        }
    }

    private void UpdateComputeBuffer()
    {
        if (_pointCount == 0 || _pointsCache == null)
        {
            _pointBuffer?.Release();
            _pointBuffer = null;
            _isDataDirty = false;
            return;
        }

        if (_pointBuffer == null || !_pointBuffer.IsValid() || _pointBuffer.count != _pointCount)
        {
            _pointBuffer?.Release();
            _pointBuffer = new ComputeBuffer(_pointCount, STRIDE);
        }

        _pointBuffer.SetData(_pointsCache);
        if (_pointCount > 0 && _isDataDirty)
        {
            UnityEngine.Debug.Log($"[PCDPointBufferManager] ComputeBuffer updated with {_pointCount} points (Static/Internal).");
        }
        _isDataDirty = false;
    }
    
    public void Cleanup()
    {
        _pointBuffer?.Release();
        _pointBuffer = null;

        _combinedBuffer?.Release();
        _combinedBuffer = null;

        _pointsCache = null;
        _dynamicData = null;
        _staticMeshes.Clear();

        _externalPointBuffer = null;
        _useExternalBuffer = false;
    }
}
