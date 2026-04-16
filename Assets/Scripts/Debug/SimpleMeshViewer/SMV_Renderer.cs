using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SMV_Renderer : MonoBehaviour
{
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh mesh;

    private void EnsureInitialized()
    {
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = "SMV_PreviewMesh";
            mesh.hideFlags = HideFlags.DontSave; // ★シーンファイルへの保存を禁止する
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // Allow large meshes
            meshFilter.sharedMesh = mesh;
        }
    }

    public void UpdateMesh(Vector3[] vertices, int[] indices, Color[] colors, Material material)
    {
        EnsureInitialized();

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.colors = colors;
        mesh.SetIndices(indices, MeshTopology.Triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Assign mesh to filter if it got disconnected
        if (meshFilter.sharedMesh != mesh)
        {
            meshFilter.sharedMesh = mesh;
        }

        if (material != null)
        {
            meshRenderer.sharedMaterial = material;
        }
    }

    public void ClearMesh()
    {
        if (mesh != null)
        {
            mesh.Clear();
        }
        if (meshFilter != null)
        {
            meshFilter.sharedMesh = null;
        }
    }

    private void OnDestroy()
    {
        if (mesh != null)
        {
            // 動的メッシュのメモリリークを防ぐため、オブジェクト破棄時に明示的にDestroy
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }
    }
}
