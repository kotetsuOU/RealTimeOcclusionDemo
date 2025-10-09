using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PCV_Renderer : MonoBehaviour
{
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Material pointCloudMaterial;
    private Mesh pointCloudMesh;
    private bool isInitialized = false;

    private void Awake()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (isInitialized) return;

        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        if (meshFilter == null || meshRenderer == null)
        {
            UnityEngine.Debug.LogError("PCV_Rendererに必要なMeshFilterまたはMeshRendererコンポーネントが見つかりません。");
            return;
        }

        if (meshRenderer.sharedMaterial == null || meshRenderer.sharedMaterial.shader.name != "Unlit/PointCloudViewer")
        {
            pointCloudMaterial = new Material(Shader.Find("Unlit/PointCloudViewer"));
            meshRenderer.material = pointCloudMaterial;
        }
        else
        {
            pointCloudMaterial = meshRenderer.sharedMaterial;
        }

        isInitialized = true;
    }

    public void UpdateMesh(PCV_Data data)
    {
        Initialize();
        if (!isInitialized) return;

        ClearMesh();
        if (data != null && data.PointCount > 0)
        {
            pointCloudMesh = PCV_MeshGenerator.CreatePointCloudMesh(data.Vertices, data.Colors);
            meshFilter.mesh = pointCloudMesh;
        }
    }

    public void UpdatePointSize(float size)
    {
        Initialize();
        if (!isInitialized) return;

        if (pointCloudMaterial != null)
        {
            pointCloudMaterial.SetFloat("_PointSize", size);
        }
    }

    public void ClearMesh()
    {
        Initialize();
        if (!isInitialized) return;

        if (meshFilter == null) return;
        if (pointCloudMesh != null)
        {
            if (UnityEngine.Application.isEditor && !UnityEngine.Application.isPlaying)
            {
                DestroyImmediate(pointCloudMesh);
            }
            else
            {
                Destroy(pointCloudMesh);
            }
            pointCloudMesh = null;
        }
        meshFilter.mesh = null;
    }

    public void InitializeOutline(GameObject outlineObject, Color color)
    {
        Initialize();
        if (!isInitialized) return;

        if (outlineObject != null)
        {
            var renderers = outlineObject.GetComponentsInChildren<MeshRenderer>();
            foreach (var rend in renderers)
            {
                var mat = new Material(Shader.Find("Unlit/PointCloudViewerOutline"));
                mat.color = color;
                rend.material = mat;
            }
        }
    }

    public void HighlightPoints(int centerIndex, List<int> neighborIndices, PCV_Data currentData, Color highlightColor, Color neighborColor)
    {
        Initialize();
        if (!isInitialized) return;

        if (pointCloudMesh == null || currentData == null) return;
        var updatedColors = (Color[])currentData.Colors.Clone();

        if (centerIndex >= 0 && centerIndex < updatedColors.Length)
        {
            updatedColors[centerIndex] = highlightColor;
        }

        foreach (int index in neighborIndices)
        {
            if (index >= 0 && index < updatedColors.Length)
            {
                updatedColors[index] = neighborColor;
            }
        }
        pointCloudMesh.colors = updatedColors;
    }

    public void ResetHighlight(PCV_Data currentData)
    {
        Initialize();
        if (!isInitialized) return;

        if (pointCloudMesh != null && currentData != null && currentData.PointCount > 0)
        {
            pointCloudMesh.colors = currentData.Colors;
        }
    }
}
