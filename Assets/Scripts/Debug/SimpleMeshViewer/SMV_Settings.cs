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
}
