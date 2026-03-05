using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(SMV_Settings), typeof(SMV_DataManager), typeof(SMV_Renderer))]
public class SMV_Controller : MonoBehaviour
{
    private SMV_Settings settings;
    private SMV_DataManager dataManager;
    private SMV_Renderer meshRenderer;

    private void Awake()
    {
        InitializeComponents();
    }

    private void Start()
    {
        // Clear mesh when starting play mode, build when in edit mode
        if (Application.isPlaying)
        {
            if (meshRenderer != null) meshRenderer.ClearMesh();
        }
        else
        {
            RebuildMesh();
        }
    }

    private void Update()
    {
        if (Application.isPlaying) return;

        if (settings != null && settings.isDirty)
        {
            RebuildMesh();
            settings.isDirty = false;
        }
    }

    private void InitializeComponents()
    {
        if (settings == null) settings = GetComponent<SMV_Settings>();
        if (dataManager == null) dataManager = GetComponent<SMV_DataManager>();
        if (meshRenderer == null) meshRenderer = GetComponent<SMV_Renderer>();

        settings.hideFlags = HideFlags.HideInInspector;
        meshRenderer.hideFlags = HideFlags.HideInInspector;
    }

    public void RebuildMesh()
    {
        InitializeComponents();

        if (settings == null || settings.fileEntries == null || settings.fileEntries.Count == 0)
        {
            Debug.LogWarning("[SMV_Controller] No file entries are set.");
            return;
        }

        Vector3[] vertices;
        int[] indices;
        Color[] colors;

        dataManager.LoadAndProcessData(settings.fileEntries, settings.edgeThreshold, out vertices, out indices, out colors);

        if (vertices != null && indices != null && vertices.Length > 0)
        {
            meshRenderer.UpdateMesh(vertices, indices, colors, settings.meshMaterial);
            Debug.Log($"[SMV_Controller] Mesh generated from {settings.fileEntries.Count} files. Vertices: {vertices.Length}, Triangles: {indices.Length / 3}");
        }
        else
        {
            meshRenderer.ClearMesh();
            Debug.LogWarning("[SMV_Controller] Generated mesh was empty.");
        }
    }
}
