using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class SMV_FileEntry
{
    public bool useFile = true;
    public string binFilePath;
    public string jsonFilePath;
    public GameObject targetPointCloudObject;
}

[System.Serializable]
public class SMV_Settings : MonoBehaviour
{
    [Header("Data Files")]
    public List<SMV_FileEntry> fileEntries = new List<SMV_FileEntry>();

    [Header("Mesh Generation")]
    [Tooltip("Discard edges longer than this (meters)")]
    public float edgeThreshold = 0.05f;
    public bool useBoundsFilter = false;
    public Bounds generationBounds = new Bounds(Vector3.zero, Vector3.one);
    public bool useRsDeviceControllerBounds = true;
    public RsDeviceController rsDeviceController;

    [Header("Preview")]
    public bool showBoundsPreview = true;
    public bool showPointsPreview = true;
    public Color boundsPreviewColor = Color.green;
    public Color pointsPreviewColor = Color.cyan;
    [Range(0.001f, 0.05f)] public float previewPointSize = 0.003f;
    [Min(1)] public int maxPreviewPointCount = 10000;

    [Header("Rendering")]
    public Material meshMaterial;

    [HideInInspector]
    public bool isDirty = false;

    public void MarkDirty()
    {
        isDirty = true;
    }

    private void OnValidate()
    {
        MarkDirty();
    }

    public Bounds GetEffectiveBounds()
    {
        if (!useRsDeviceControllerBounds || rsDeviceController == null)
            return generationBounds;

        float margin = rsDeviceController.FrameWidth;
        Vector3 scanRange = rsDeviceController.RealSenseScanRange;
        Vector3 min = new Vector3(margin, margin, margin);
        Vector3 max = scanRange - new Vector3(margin, margin, margin);
        Vector3 size = max - min;

        size.x = Mathf.Max(0f, size.x);
        size.y = Mathf.Max(0f, size.y);
        size.z = Mathf.Max(0f, size.z);

        Vector3 center = min + (size * 0.5f);
        return new Bounds(center, size);
    }

    public bool IsPointInsideEffectiveBounds(Vector3 worldPoint)
    {
        if (!useBoundsFilter)
            return true;

        Bounds effectiveBounds = GetEffectiveBounds();
        if (useRsDeviceControllerBounds && rsDeviceController != null)
        {
            Vector3 localPoint = rsDeviceController.transform.InverseTransformPoint(worldPoint);
            return effectiveBounds.Contains(localPoint);
        }

        return effectiveBounds.Contains(worldPoint);
    }
}
