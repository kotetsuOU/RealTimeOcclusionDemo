using UnityEngine;
using System.Collections.Generic;

namespace Features.HapticsCollision.Processors
{
    /// <summary>
    /// SkinnedMeshRenderer や MeshFilter のメッシュ結合・Bake・頂点データ抽出および Bounds 計算を担当するクラス。
    /// </summary>
    public class HCD_MeshBaker
    {
        private Mesh _bakedMesh;
        private Mesh[] _tempBakedMeshes;
        private CombineInstance[] _combineInstances;

        public Vector3[] Vertices { get; private set; }
        public Vector3[] Normals { get; private set; }
        public int[] Triangles { get; private set; }
        public Bounds MeshBounds { get; private set; }
        public Transform TargetTransform { get; private set; }
        public int TrianglesCount => Triangles != null ? Triangles.Length / 3 : 0;
        public bool HasValidMeshData => Vertices != null && Vertices.Length > 0 && Triangles != null && Triangles.Length > 0;
        public int ValidInstanceCount => _combineInstances != null ? _combineInstances.Length : 0;

        public bool BakeAndCombine(
            SkinnedMeshRenderer[] targetSkinnedMeshes,
            MeshFilter[] targetMeshFilters,
            Transform fallbackTargetObject)
        {
            TargetTransform = null;
            MeshBounds = default;
            bool boundsInitialized = false;

            if (_bakedMesh == null) _bakedMesh = new Mesh();

            if (targetSkinnedMeshes != null && targetSkinnedMeshes.Length > 0 && targetSkinnedMeshes[0] != null)
            {
                TargetTransform = targetSkinnedMeshes[0].transform;
            }
            else if (targetMeshFilters != null && targetMeshFilters.Length > 0 && targetMeshFilters[0] != null)
            {
                TargetTransform = targetMeshFilters[0].transform;
            }

            if (TargetTransform == null && fallbackTargetObject != null)
            {
                TargetTransform = fallbackTargetObject;
            }

            if (TargetTransform == null) return false;

            var validInstances = new List<CombineInstance>();

            if (targetSkinnedMeshes != null && targetSkinnedMeshes.Length > 0)
            {
                if (_tempBakedMeshes == null || _tempBakedMeshes.Length != targetSkinnedMeshes.Length)
                {
                    if (_tempBakedMeshes != null)
                    {
                        foreach (var m in _tempBakedMeshes) if (m != null) Object.Destroy(m);
                    }
                    _tempBakedMeshes = new Mesh[targetSkinnedMeshes.Length];
                    for (int i = 0; i < targetSkinnedMeshes.Length; i++) _tempBakedMeshes[i] = new Mesh();
                }

                for (int i = 0; i < targetSkinnedMeshes.Length; i++)
                {
                    var smr = targetSkinnedMeshes[i];
                    if (smr == null) continue;

                    smr.BakeMesh(_tempBakedMeshes[i], false);
                    
                    CombineInstance ci = new CombineInstance();
                    ci.mesh = _tempBakedMeshes[i];
                    ci.transform = TargetTransform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                    validInstances.Add(ci);

                    if (!boundsInitialized)
                    {
                        MeshBounds = smr.bounds;
                        boundsInitialized = true;
                    }
                    else
                    {
                        Bounds current = MeshBounds;
                        current.Encapsulate(smr.bounds);
                        MeshBounds = current;
                    }
                }
            }

            if (targetMeshFilters != null && targetMeshFilters.Length > 0)
            {
                for (int i = 0; i < targetMeshFilters.Length; i++)
                {
                    var mf = targetMeshFilters[i];
                    if (mf == null || mf.sharedMesh == null) continue;
                    
                    if (!mf.sharedMesh.isReadable)
                    {
                        UnityEngine.Debug.LogWarning($"[HCD_DistanceProcessor] Mesh '{mf.sharedMesh.name}' on '{mf.gameObject.name}' is not readable. Please enable 'Read/Write Enabled' in the import settings. Skipping this mesh for haptic collision.");
                        continue;
                    }

                    CombineInstance ci = new CombineInstance();
                    ci.mesh = mf.sharedMesh;
                    ci.transform = TargetTransform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    validInstances.Add(ci);

                    var renderer = mf.GetComponent<MeshRenderer>();
                    var mfBounds = renderer != null ? renderer.bounds : new Bounds(mf.transform.position, mf.transform.lossyScale);

                    if (!boundsInitialized)
                    {
                        MeshBounds = mfBounds;
                        boundsInitialized = true;
                    }
                    else
                    {
                        Bounds current = MeshBounds;
                        current.Encapsulate(mfBounds);
                        MeshBounds = current;
                    }
                }
            }

            if (validInstances.Count == 0) return false;

            _combineInstances = validInstances.ToArray();
            _bakedMesh.CombineMeshes(_combineInstances, true, true);
            
            Vertices = _bakedMesh.vertices;
            Normals = _bakedMesh.normals;
            Triangles = _bakedMesh.triangles;

            // smr.bounds の遅延・キャッシュ狂いによる判定欠落を防ぐため、実際のBake頂点から真のワールドBoundsを補正
            if (Vertices != null && Vertices.Length > 0 && TargetTransform != null)
            {
                Vector3 firstWp = TargetTransform.TransformPoint(Vertices[0]);
                Vector3 min = firstWp;
                Vector3 max = firstWp;
                int step = Mathf.Max(1, Vertices.Length / 128);
                for (int v = 0; v < Vertices.Length; v += step)
                {
                    Vector3 wp = TargetTransform.TransformPoint(Vertices[v]);
                    min = Vector3.Min(min, wp);
                    max = Vector3.Max(max, wp);
                }
                Vector3 lastWp = TargetTransform.TransformPoint(Vertices[Vertices.Length - 1]);
                min = Vector3.Min(min, lastWp);
                max = Vector3.Max(max, lastWp);

                Bounds calculatedWorldBounds = new Bounds((min + max) * 0.5f, max - min);
                if (boundsInitialized)
                {
                    calculatedWorldBounds.Encapsulate(MeshBounds);
                }
                MeshBounds = calculatedWorldBounds;
            }

            return HasValidMeshData;
        }

        public void Release()
        {
            if (_bakedMesh != null) Object.Destroy(_bakedMesh);
            if (_tempBakedMeshes != null)
            {
                foreach (var m in _tempBakedMeshes) if (m != null) Object.Destroy(m);
            }
        }
    }
}
