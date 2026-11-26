using UnityEngine;

namespace RealSense.DummyPointCloud
{
    public enum PointDensityUnit
    {
        PointsPerMm2,   // 1 mm^2 あたりの点数
        PointsPerCm2,   // 1 cm^2 あたりの点数
        PointSpacingMm, // 点間隔 (mm)
        TotalPointCount // 指定の合計点数
    }

    public enum PointColorMode
    {
        SolidColor,    // 単色指定
        MaterialColor, // マテリアル/メインカラー
        VertexColor    // メッシュの頂点カラー
    }

    public struct SampledPointCloudData
    {
        public Vector3[] Positions; // ワールド座標
        public Vector3[] Normals;   // ワールド法線
        public Color[] Colors;      // 各点の色
        public int PointCount;
    }
}

