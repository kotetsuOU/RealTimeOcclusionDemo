using System.Collections.Generic;
using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 空中始点・地上着地点に基づき、主幹（Trunk）、枝（Branches）、着地点放電スパーク（ImpactSparks）の
    /// パス幾何計算および各十字リボンメッシュへの頂点更新ディスパッチを担当する Pure C# クラス。
    /// 完全 Zero-GC で動作します。
    /// </summary>
    public static class WeatherBoltGeometryDispatcher
    {
        /// <summary>
        /// 主幹、枝、着地点放電スパークのパスを計算し、各リボンメッシュを更新します。
        /// </summary>
        public static void DispatchGeometry(
            Vector3 sky,
            Vector3 ground,
            WeatherLightningGeometry geometry,
            WeatherBoltRibbon trunk,
            IReadOnlyList<WeatherBoltRibbon> branches,
            WeatherBoltRibbon impactSparks,
            Vector3[] trunkPointsBuffer,
            float trunkWidth,
            int maxBranches)
        {
            if (geometry == null || trunk == null) return;

            float length = Vector3.Distance(sky, ground);

            // 1. 主幹パス (Trunk) を計算・メッシュ更新
            int trunkCount = geometry.GeneratePath(sky, ground, length * 0.12f, 6, trunkPointsBuffer);
            WeatherLightningMeshBuilder.UpdateRibbonMesh(trunk, trunkPointsBuffer, trunkCount, trunkWidth);

            // 2. 枝パス (Branches) を計算・メッシュ更新
            if (branches != null)
            {
                int activeBranchCount = Random.Range(1, maxBranches + 1);
                for (int i = 0; i < branches.Count; i++)
                {
                    var br = branches[i];
                    if (br == null) continue;

                    if (i < activeBranchCount)
                    {
                        float branchLen = length * Random.Range(0.2f, 0.4f);
                        int brCount = geometry.GenerateBranch(trunkPointsBuffer, trunkCount, branchLen, 4, br.Points);
                        WeatherLightningMeshBuilder.UpdateRibbonMesh(br, br.Points, brCount, trunkWidth * 0.55f);
                        br.Root.SetActive(true);
                    }
                    else
                    {
                        br.Root.SetActive(false);
                    }
                }
            }

            // 3. 着地点放電スパーク (Ground Impact Sparks) を計算・メッシュ更新
            if (impactSparks != null)
            {
                int sparkCount = WeatherLightningMeshBuilder.BuildImpactSparkPoints(impactSparks.Points, ground, trunkWidth);
                WeatherLightningMeshBuilder.UpdateRibbonMesh(impactSparks, impactSparks.Points, sparkCount, trunkWidth * 0.65f);
                impactSparks.Root.SetActive(true);
            }
        }
    }
}
