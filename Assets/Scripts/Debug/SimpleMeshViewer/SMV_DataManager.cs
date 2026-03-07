using System.IO;
using System.Collections.Generic;
using UnityEngine;

public class SMV_DataManager : MonoBehaviour
{
    [System.Serializable]
    private struct DepthMetaData
    {
        public int width;
        public int height;
        public float fx;
        public float fy;
        public float ppx;
        public float ppy;
        public float depthScale;
        public Matrix4x4 transformMatrix;
    }

    public void LoadAndProcessData(List<SMV_FileEntry> fileEntries, float edgeThreshold, bool useBoundsFilter, Bounds generationBounds, SMV_Settings settings, out Vector3[] outVertices, out int[] outIndices, out Color[] outColors)
    {
        List<Vector3> combinedVertices = new List<Vector3>();
        List<int> combinedIndices = new List<int>();
        List<Color> combinedColors = new List<Color>();

        int vertexOffset = 0;

        foreach (var entry in fileEntries)
        {
            if (!entry.useFile || string.IsNullOrEmpty(entry.binFilePath) || string.IsNullOrEmpty(entry.jsonFilePath))
                continue;

            if (!File.Exists(entry.binFilePath) || !File.Exists(entry.jsonFilePath))
            {
                Debug.LogWarning($"[SMV_DataManager] File not found: {entry.binFilePath} or {entry.jsonFilePath}");
                continue;
            }

            string jsonText = File.ReadAllText(entry.jsonFilePath);
            DepthMetaData meta = JsonUtility.FromJson<DepthMetaData>(jsonText);

            byte[] rawBytes = File.ReadAllBytes(entry.binFilePath);
            int expectedPixels = meta.width * meta.height;
            if (rawBytes.Length != expectedPixels * 2)
            {
                Debug.LogWarning($"[SMV_DataManager] Bin file size does not match JSON metadata for {entry.binFilePath}.");
                continue;
            }

            ushort[] depthData = new ushort[expectedPixels];
            System.Buffer.BlockCopy(rawBytes, 0, depthData, 0, rawBytes.Length);

            // Determine PointCloud local-to-world matrix
            Matrix4x4 localToWorld = Matrix4x4.identity;
            if (entry.targetPointCloudObject != null)
            {
                localToWorld = entry.targetPointCloudObject.transform.localToWorldMatrix;
            }
            
            // Combine internal RS transform with GameObject world transform
            Matrix4x4 finalTransform = localToWorld * meta.transformMatrix;

            Vector3[] localVertices = new Vector3[expectedPixels];
            Color[] localColors = new Color[expectedPixels];
            bool[] validVertexMask = new bool[expectedPixels];
            
            // Calculate 3D positions
            for (int y = 0; y < meta.height; y++)
            {
                for (int x = 0; x < meta.width; x++)
                {
                    int index = y * meta.width + x;
                    float z = depthData[index] * meta.depthScale;
                    
                    if (z > 0)
                    {
                        float vx = (x - meta.ppx) * z / meta.fx;
                        float vy = -(y - meta.ppy) * z / meta.fy; // Flip Y for Unity
                        
                        Vector3 pos = new Vector3(vx, vy, z);
                        pos = finalTransform.MultiplyPoint3x4(pos);

                        bool isInsideBounds = !useBoundsFilter || (settings != null ? settings.IsPointInsideEffectiveBounds(pos) : generationBounds.Contains(pos));
                        if (isInsideBounds)
                        {
                            localVertices[index] = pos;
                            localColors[index] = Color.white;
                            validVertexMask[index] = true;
                        }
                        else
                        {
                            localVertices[index] = Vector3.zero;
                            localColors[index] = Color.clear;
                            validVertexMask[index] = false;
                        }
                    }
                    else
                    {
                        localVertices[index] = Vector3.zero;
                        localColors[index] = Color.clear;
                        validVertexMask[index] = false;
                    }
                }
            }

            // Generate Indices
            List<int> validIndices = new List<int>();
            float sqrEdgeThreshold = edgeThreshold * edgeThreshold;

            for (int y = 0; y < meta.height - 1; y++)
            {
                for (int x = 0; x < meta.width - 1; x++)
                {
                    int tl = y * meta.width + x;
                    int tr = tl + 1;
                    int bl = (y + 1) * meta.width + x;
                    int br = bl + 1;

                    if (!validVertexMask[tl] || !validVertexMask[tr] || !validVertexMask[bl] || !validVertexMask[br])
                        continue;

                    Vector3 vTL = localVertices[tl];
                    Vector3 vTR = localVertices[tr];
                    Vector3 vBL = localVertices[bl];
                    Vector3 vBR = localVertices[br];

                    // Check edge length to discard background connection
                    if ((vTL - vTR).sqrMagnitude < sqrEdgeThreshold &&
                        (vTL - vBL).sqrMagnitude < sqrEdgeThreshold &&
                        (vTR - vBL).sqrMagnitude < sqrEdgeThreshold)
                    {
                        validIndices.Add(tl + vertexOffset);
                        validIndices.Add(tr + vertexOffset);
                        validIndices.Add(bl + vertexOffset);
                    }

                    if ((vTR - vBR).sqrMagnitude < sqrEdgeThreshold &&
                        (vBL - vBR).sqrMagnitude < sqrEdgeThreshold &&
                        (vTR - vBL).sqrMagnitude < sqrEdgeThreshold)
                    {
                        validIndices.Add(bl + vertexOffset);
                        validIndices.Add(tr + vertexOffset);
                        validIndices.Add(br + vertexOffset);
                    }
                }
            }

            // Combine
            combinedVertices.AddRange(localVertices);
            combinedColors.AddRange(localColors);
            combinedIndices.AddRange(validIndices);

            vertexOffset += expectedPixels;
        }

        outVertices = combinedVertices.ToArray();
        outIndices = combinedIndices.ToArray();
        outColors = combinedColors.ToArray();
    }
}
