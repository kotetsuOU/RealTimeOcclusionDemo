using System.Collections.Generic;
using UnityEngine;

namespace RealSense.DummyPointCloud
{
    /// <summary>
    /// 単一メッシュの面積計算および三角形表面への一様サンプリングを実行する Pure C# ヘルパー。
    /// </summary>
    internal static class RsMeshTriangleSampler
    {
        public static float CalculateMeshArea(Mesh mesh, Matrix4x4 localToWorld)
        {
            if (mesh == null || !mesh.isReadable) return 0f;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length == 0) return 0f;

            float totalArea = 0f;
            int triCount = triangles.Length / 3;
            for (int i = 0; i < triCount; i++)
            {
                Vector3 p0 = localToWorld.MultiplyPoint3x4(vertices[triangles[i * 3]]);
                Vector3 p1 = localToWorld.MultiplyPoint3x4(vertices[triangles[i * 3 + 1]]);
                Vector3 p2 = localToWorld.MultiplyPoint3x4(vertices[triangles[i * 3 + 2]]);
                totalArea += Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;
            }
            return totalArea;
        }

        public static void SampleMesh(
            Mesh mesh,
            Renderer renderer,
            Matrix4x4 localToWorld,
            PointDensityUnit densityUnit,
            float densityValue,
            PointColorMode colorMode,
            Color solidColor,
            List<Vector3> outPositions,
            List<Vector3> outNormals,
            List<Color> outColors,
            int maxPointLimit)
        {
            if (mesh == null || !mesh.isReadable) return;

            Vector3[] vertices = mesh.vertices;
            Vector3[] meshNormals = mesh.normals;
            int[] triangles = mesh.triangles;
            Color[] vertexColors = mesh.colors;

            if (vertices == null || vertices.Length == 0 || triangles == null || triangles.Length == 0)
                return;

            bool hasMeshNormals = meshNormals != null && meshNormals.Length == vertices.Length;
            bool hasVertexColors = vertexColors != null && vertexColors.Length == vertices.Length;
            Color baseMaterialColor = ResolveMaterialColor(renderer, colorMode, solidColor);

            int triCount = triangles.Length / 3;
            float[] triAreas = new float[triCount];
            float totalWorldAreaSquareMeters = 0f;

            for (int i = 0; i < triCount; i++)
            {
                Vector3 p0 = localToWorld.MultiplyPoint3x4(vertices[triangles[i * 3]]);
                Vector3 p1 = localToWorld.MultiplyPoint3x4(vertices[triangles[i * 3 + 1]]);
                Vector3 p2 = localToWorld.MultiplyPoint3x4(vertices[triangles[i * 3 + 2]]);

                float area = Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;
                triAreas[i] = area;
                totalWorldAreaSquareMeters += area;
            }

            if (totalWorldAreaSquareMeters <= 1e-8f) return;

            int targetTotalPoints = ComputeTargetPointCount(totalWorldAreaSquareMeters, densityUnit, densityValue, maxPointLimit);
            System.Random rand = new System.Random(42);
            int startCount = outPositions.Count;

            for (int i = 0; i < triCount; i++)
            {
                if (outPositions.Count - startCount >= maxPointLimit) break;

                float areaFrac = triAreas[i] / totalWorldAreaSquareMeters;
                int pointsForThisTri = Mathf.RoundToInt(targetTotalPoints * areaFrac);

                if (pointsForThisTri <= 0 && targetTotalPoints > 0 && rand.NextDouble() < areaFrac * targetTotalPoints)
                    pointsForThisTri = 1;

                if (pointsForThisTri <= 0) continue;

                int i0 = triangles[i * 3];
                int i1 = triangles[i * 3 + 1];
                int i2 = triangles[i * 3 + 2];

                Vector3 v0 = localToWorld.MultiplyPoint3x4(vertices[i0]);
                Vector3 v1 = localToWorld.MultiplyPoint3x4(vertices[i1]);
                Vector3 v2 = localToWorld.MultiplyPoint3x4(vertices[i2]);
                Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                Vector3 n0 = hasMeshNormals ? localToWorld.MultiplyVector(meshNormals[i0]).normalized : faceNormal;
                Vector3 n1 = hasMeshNormals ? localToWorld.MultiplyVector(meshNormals[i1]).normalized : faceNormal;
                Vector3 n2 = hasMeshNormals ? localToWorld.MultiplyVector(meshNormals[i2]).normalized : faceNormal;

                Color c0 = ResolveVertexColor(colorMode, solidColor, baseMaterialColor, hasVertexColors, vertexColors, i0);
                Color c1 = ResolveVertexColor(colorMode, solidColor, baseMaterialColor, hasVertexColors, vertexColors, i1);
                Color c2 = ResolveVertexColor(colorMode, solidColor, baseMaterialColor, hasVertexColors, vertexColors, i2);

                for (int p = 0; p < pointsForThisTri; p++)
                {
                    if (outPositions.Count - startCount >= maxPointLimit) break;

                    float r1 = (float)rand.NextDouble();
                    float r2 = (float)rand.NextDouble();
                    float sqrtR1 = Mathf.Sqrt(r1);
                    float u = 1f - sqrtR1;
                    float v = r2 * sqrtR1;
                    float w = 1f - u - v;

                    Vector3 norm = (u * n0 + v * n1 + w * n2).normalized;
                    if (norm.sqrMagnitude < 0.001f) norm = faceNormal;

                    outPositions.Add(u * v0 + v * v1 + w * v2);
                    outNormals?.Add(norm);
                    outColors.Add(u * c0 + v * c1 + w * c2);
                }
            }
        }

        private static Color ResolveMaterialColor(Renderer renderer, PointColorMode colorMode, Color solidColor)
        {
            if (colorMode != PointColorMode.MaterialColor || renderer.sharedMaterial == null)
                return solidColor;

            if (renderer.sharedMaterial.HasProperty("_Color"))
                return renderer.sharedMaterial.color;

            if (renderer.sharedMaterial.HasProperty("_BaseColor"))
                return renderer.sharedMaterial.GetColor("_BaseColor");

            return solidColor;
        }

        private static Color ResolveVertexColor(
            PointColorMode colorMode, Color solidColor, Color baseMaterialColor,
            bool hasVertexColors, Color[] vertexColors, int index)
        {
            if (colorMode == PointColorMode.SolidColor) return solidColor;
            if (colorMode == PointColorMode.VertexColor && hasVertexColors) return vertexColors[index];
            return baseMaterialColor;
        }

        private static int ComputeTargetPointCount(
            float totalWorldAreaSquareMeters, PointDensityUnit densityUnit, float densityValue, int maxPointLimit)
        {
            float areaMm2 = totalWorldAreaSquareMeters * 1000000f;
            float areaCm2 = totalWorldAreaSquareMeters * 10000f;
            int targetTotalPoints = 0;

            switch (densityUnit)
            {
                case PointDensityUnit.PointsPerMm2:
                    targetTotalPoints = Mathf.Max(1, Mathf.RoundToInt(areaMm2 * Mathf.Max(0.0001f, densityValue)));
                    break;
                case PointDensityUnit.PointsPerCm2:
                    targetTotalPoints = Mathf.Max(1, Mathf.RoundToInt(areaCm2 * Mathf.Max(0.0001f, densityValue)));
                    break;
                case PointDensityUnit.PointSpacingMm:
                    float spacingMm = Mathf.Max(0.1f, densityValue);
                    targetTotalPoints = Mathf.Max(1, Mathf.RoundToInt(areaMm2 * (1f / (spacingMm * spacingMm))));
                    break;
                case PointDensityUnit.TotalPointCount:
                    targetTotalPoints = Mathf.Max(1, Mathf.RoundToInt(densityValue));
                    break;
            }

            return Mathf.Min(targetTotalPoints, maxPointLimit);
        }
    }
}

