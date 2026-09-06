using System;
using System.Collections.Generic;
using UnityEngine;

#nullable enable

namespace Features.Experiment.VR
{
    /// <summary>
    /// 棒状Toolの点群データから、ロバストに実効到達距離・先端位置を推定する計算クラス。
    /// <para>
    /// <b>アルゴリズム説明:</b><br/>
    /// 単純な最遠点（$\max_i d_i$）ではなく、根元基準点 $\mathbf{p}_{\mathrm{base}}$ からの距離分布 $d_i$ において
    /// 最遠側5%のノイズ・外れ値を除外した95パーセンタイル値 $d_{\mathrm{tip}} = d_{(\lfloor 0.95N \rfloor)}$ を
    /// 実効到達距離として導出します。
    /// </para>
    /// </summary>
    public static class EXP_ToolReachEstimator
    {
        /// <summary>
        /// 推定結果を格納する構造体
        /// </summary>
        public struct ReachResult
        {
            /// <summary>有効点数 N</summary>
            public int totalPoints;

            /// <summary>95パーセンタイル等の実効到達距離 [m]</summary>
            public float reachDistance;

            /// <summary>推定された実効先端の3D座標</summary>
            public Vector3 estimatedTipPosition;

            /// <summary>計算が成功したかどうか</summary>
            public bool isValid;
        }

        /// <summary>
        /// 点群配列から、根元基準位置に基づき指定パーセンタイル（既定0.95 = 上位5%除外）の到達距離と先端座標を推定します。
        /// </summary>
        /// <param name="points">Toolの3D点群配列</param>
        /// <param name="pointCount">使用する点数</param>
        /// <param name="basePosition">Tool根元の基準3D座標 $\mathbf{p}_{\mathrm{base}}$</param>
        /// <param name="axisDirection">（任意）棒の伸びる中心軸方向。指定された場合は軸投影距離で計算します。</param>
        /// <param name="percentile">カットオフとするパーセンタイル（0.0～1.0。例: 0.95 = 最遠5%を除外）</param>
        /// <returns>推定結果構造体</returns>
        public static ReachResult EstimateReach(
            Vector3[] points,
            int pointCount,
            Vector3 basePosition,
            Vector3? axisDirection = null,
            float percentile = 0.95f)
        {
            if (points == null || pointCount <= 0)
            {
                return new ReachResult { isValid = false };
            }

            int validCount = Mathf.Min(points.Length, pointCount);
            if (validCount == 0)
            {
                return new ReachResult { isValid = false };
            }

            float clampedPercentile = Mathf.Clamp(percentile, 0.0f, 1.0f);
            bool useProjection = axisDirection.HasValue && axisDirection.Value.sqrMagnitude > 1e-6f;
            Vector3 normAxis = useProjection ? axisDirection!.Value.normalized : Vector3.forward;

            // 各点の距離を計算して格納
            List<KeyValuePair<float, Vector3>> distancePointPairs = new List<KeyValuePair<float, Vector3>>(validCount);

            for (int i = 0; i < validCount; i++)
            {
                Vector3 p = points[i];
                Vector3 diff = p - basePosition;

                float dist = useProjection ? Vector3.Dot(diff, normAxis) : diff.magnitude;

                distancePointPairs.Add(new KeyValuePair<float, Vector3>(dist, p));
            }

            // 距離の昇順にソート
            distancePointPairs.Sort((a, b) => a.Key.CompareTo(b.Key));

            // 指定パーセンタイルのインデックスを算出
            int targetIdx = Mathf.Clamp(Mathf.FloorToInt((validCount - 1) * clampedPercentile), 0, validCount - 1);

            float targetDistance = distancePointPairs[targetIdx].Key;
            Vector3 targetPosition = useProjection 
                ? basePosition + normAxis * targetDistance 
                : distancePointPairs[targetIdx].Value;

            return new ReachResult
            {
                totalPoints = validCount,
                reachDistance = targetDistance,
                estimatedTipPosition = targetPosition,
                isValid = true
            };
        }

        /// <summary>
        /// 空間分割（セクター分割）別の到達距離計算結果
        /// </summary>
        public struct SectorReachResult
        {
            /// <summary>分割セクター数（既定 16）</summary>
            public int sectorCount;

            /// <summary>蓄積された合計点数</summary>
            public int accumulatedTotalPoints;

            /// <summary>各セクターごとの 95% パーセンタイル到達距離 [m]</summary>
            public float[] sectorReachDistances;

            /// <summary>全体での 95% パーセンタイル到達距離 [m]</summary>
            public float overallReachDistance;

            /// <summary>全体での推定実効先端3D座標</summary>
            public Vector3 overallEstimatedTipPosition;

            /// <summary>各セクター到達距離の平均値 [m]</summary>
            public float meanSectorReach;

            /// <summary>計算が成功したかどうか</summary>
            public bool isValid;
        }

        /// <summary>
        /// 試行開始から終了までに蓄積された全点群データに対し、中心軸の周りを K 個のセクター（空間分割）に分割し、
        /// セクターごとの 95% パーセンタイル到達距離および全体到達距離を算出します。
        /// </summary>
        /// <param name="accumulatedPoints">試行全体で蓄積された点群リスト</param>
        /// <param name="basePosition">Tool根元の基準3D座標 $\mathbf{p}_{\mathrm{base}}$</param>
        /// <param name="axisDirection">棒の伸びる中心軸方向</param>
        /// <param name="sectorCount">空間分割数（既定 16方向）</param>
        /// <param name="percentile">カットオフとするパーセンタイル（既定 0.95 = 最遠5%除外）</param>
        /// <returns>空間分割別の到達距離解析結果</returns>
        public static SectorReachResult AnalyzeAccumulatedReach(
            IReadOnlyList<Vector3> accumulatedPoints,
            Vector3 basePosition,
            Vector3 axisDirection,
            int sectorCount = 16,
            float percentile = 0.95f)
        {
            if (accumulatedPoints == null || accumulatedPoints.Count == 0)
            {
                return new SectorReachResult { isValid = false };
            }

            int count = accumulatedPoints.Count;
            float clampedPercentile = Mathf.Clamp(percentile, 0.0f, 1.0f);
            Vector3 normAxis = axisDirection.sqrMagnitude > 1e-6f ? axisDirection.normalized : Vector3.forward;

            // 軸と直交する基準平面（Up/Right）の基底ベクトルを計算
            Vector3 ortho1 = Vector3.Cross(normAxis, Vector3.up);
            if (ortho1.sqrMagnitude < 1e-4f)
            {
                ortho1 = Vector3.Cross(normAxis, Vector3.right);
            }
            ortho1.Normalize();
            Vector3 ortho2 = Vector3.Cross(normAxis, ortho1).normalized;

            // セクターごとの距離リストの確保
            int numSectors = Mathf.Max(1, sectorCount);
            List<float>[] sectorDistances = new List<float>[numSectors];
            for (int i = 0; i < numSectors; i++)
            {
                sectorDistances[i] = new List<float>();
            }

            List<float> allDistances = new List<float>(count);

            for (int i = 0; i < count; i++)
            {
                Vector3 diff = accumulatedPoints[i] - basePosition;
                float projDist = Vector3.Dot(diff, normAxis);
                allDistances.Add(projDist);

                // 軸直交平面への投影から角度 θ（セクター）を判定
                Vector3 perpComponent = diff - projDist * normAxis;
                if (perpComponent.sqrMagnitude > 1e-6f)
                {
                    float x = Vector3.Dot(perpComponent, ortho1);
                    float y = Vector3.Dot(perpComponent, ortho2);
                    float angle = Mathf.Atan2(y, x);
                    if (angle < 0) angle += 2 * Mathf.PI; // 0 ～ 2π

                    int sectorIdx = Mathf.Clamp(Mathf.FloorToInt((angle / (2 * Mathf.PI)) * numSectors), 0, numSectors - 1);
                    sectorDistances[sectorIdx].Add(projDist);
                }
            }

            // 1. 全体での 95% パーセンタイル到達距離
            allDistances.Sort();
            int overallIdx = Mathf.Clamp(Mathf.FloorToInt((allDistances.Count - 1) * clampedPercentile), 0, allDistances.Count - 1);
            float overallReach = allDistances[overallIdx];
            Vector3 overallTipPos = basePosition + normAxis * overallReach;

            // 2. 空間分割（セクター）ごとの 95% パーセンタイル到達距離
            float[] sectorReaches = new float[numSectors];
            float sectorSum = 0f;
            int validSectorCount = 0;

            for (int s = 0; s < numSectors; s++)
            {
                var sDists = sectorDistances[s];
                if (sDists.Count > 0)
                {
                    sDists.Sort();
                    int sIdx = Mathf.Clamp(Mathf.FloorToInt((sDists.Count - 1) * clampedPercentile), 0, sDists.Count - 1);
                    sectorReaches[s] = sDists[sIdx];
                    sectorSum += sectorReaches[s];
                    validSectorCount++;
                }
                else
                {
                    sectorReaches[s] = 0f;
                }
            }

            float meanReach = validSectorCount > 0 ? sectorSum / validSectorCount : 0f;

            return new SectorReachResult
            {
                sectorCount = numSectors,
                accumulatedTotalPoints = count,
                sectorReachDistances = sectorReaches,
                overallReachDistance = overallReach,
                overallEstimatedTipPosition = overallTipPos,
                meanSectorReach = meanReach,
                isValid = true
            };
        }
    }
}
