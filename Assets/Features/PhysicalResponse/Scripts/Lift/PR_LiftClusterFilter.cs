using System.Collections.Generic;
using UnityEngine;
using Features.HapticsCollision;

namespace Features.PhysicalResponse
{
    /// <summary>
    /// HCD_Pipeline から取得された点群クラスタを足平面領域でフィルタリングし、
    /// 重心の選定および瞬断バッファリングを管理するクラス。
    /// </summary>
    public class PR_LiftClusterFilter
    {
        public const int MAX_LOST_CONTACT_FRAMES = 4;

        private bool isContacting = false;
        private int lostContactFrames = 0;
        private Vector3 previousCentroid;

        public bool IsContacting => isContacting;
        public int LostContactFrames => lostContactFrames;
        public Vector3 PreviousCentroid => previousCentroid;

        /// <summary>
        /// 接触状態をリセットします。
        /// </summary>
        public void ResetContact()
        {
            isContacting = false;
            lostContactFrames = 0;
            previousCentroid = Vector3.zero;
        }

        /// <summary>
        /// トラッキングクラスタを評価し、有効な接触重心を選定します。
        /// </summary>
        /// <param name="clusters">HCD_Pipeline から取得されたクラスタリスト</param>
        /// <param name="targetTransform">対象オブジェクトの Transform</param>
        /// <param name="plane">計算された足平面データ</param>
        /// <param name="underPlaneDepth">平面下部の深さ閾値</param>
        /// <param name="upperMargin">平面上部の許容マージン</param>
        /// <param name="planeMargin">四角形外側マージン</param>
        /// <param name="maxCentroidJump">重心ジャンプ限界値</param>
        /// <param name="selectedCentroid">選定された重心座標（出力）</param>
        /// <returns>有効な追従継続または新規接触が成立した場合は true</returns>
        public bool EvaluateClusters(
            IReadOnlyList<TrackedCluster> clusters,
            Transform targetTransform,
            in PR_LiftPlaneCalculator.FootPlaneData plane,
            float underPlaneDepth,
            float upperMargin,
            float planeMargin,
            float maxCentroidJump,
            out Vector3 selectedCentroid)
        {
            selectedCentroid = Vector3.zero;

            // 1. クラスタリスト自体の存在チェック
            if (clusters == null || clusters.Count == 0)
            {
                if (isContacting && ++lostContactFrames <= MAX_LOST_CONTACT_FRAMES)
                {
                    // 瞬断バッファ: 一時的な点群途切れ時は直前の重心を維持
                    selectedCentroid = previousCentroid;
                    return true;
                }

                ResetContact();
                return false;
            }

            // 2. 追従中はマージンをやや広げて手のブレや遅れを許容
            float activeDepthThreshold = isContacting ? (underPlaneDepth + 0.04f) : underPlaneDepth;
            float activeUpperMargin = isContacting ? (upperMargin + 0.015f) : upperMargin;
            float extraMargin = isContacting ? 0.02f : 0f;

            float effectiveMinX = plane.MinX - extraMargin;
            float effectiveMaxX = plane.MaxX + extraMargin;
            float effectiveMinZ = plane.MinZ - extraMargin;
            float effectiveMaxZ = plane.MaxZ + extraMargin;

            int validClusterCount = 0;
            float minDistanceToPlane = float.MaxValue;
            float minDistanceToPrev = float.MaxValue;
            Vector3 closestToPlaneCentroid = Vector3.zero;
            Vector3 bestCentroid = Vector3.zero;

            for (int i = 0; i < clusters.Count; i++)
            {
                var cluster = clusters[i];
                if (!cluster.IsAlive) continue;

                // 平面との符号付き距離 (法線方向)
                float signedDist = Vector3.Dot(cluster.Centroid - plane.Origin, plane.Normal);

                // 平面より上部（体側）は除外、平面より下部かつ深さ閾値以内のみ抽出
                if (signedDist < -activeDepthThreshold || signedDist > activeUpperMargin)
                {
                    continue;
                }

                // ローカル座標で水平マージン内かチェック
                Vector3 localCentroid = targetTransform.InverseTransformPoint(cluster.Centroid);
                if (localCentroid.x < effectiveMinX || localCentroid.x > effectiveMaxX ||
                    localCentroid.z < effectiveMinZ || localCentroid.z > effectiveMaxZ)
                {
                    continue;
                }

                float absDist = Mathf.Abs(signedDist);
                if (absDist < minDistanceToPlane)
                {
                    minDistanceToPlane = absDist;
                    closestToPlaneCentroid = cluster.Centroid;
                }

                if (isContacting)
                {
                    float distToPrev = Vector3.Distance(cluster.Centroid, previousCentroid);
                    if (distToPrev < minDistanceToPrev)
                    {
                        minDistanceToPrev = distToPrev;
                        bestCentroid = cluster.Centroid;
                    }
                }

                validClusterCount++;
            }

            // 3. 有効クラスタなし時の処理
            if (validClusterCount == 0)
            {
                if (isContacting && ++lostContactFrames <= MAX_LOST_CONTACT_FRAMES)
                {
                    selectedCentroid = previousCentroid;
                    return true;
                }

                ResetContact();
                return false;
            }

            // 4. 急激な重心ジャンプ判定
            if (isContacting && minDistanceToPrev > maxCentroidJump)
            {
                if (++lostContactFrames <= MAX_LOST_CONTACT_FRAMES)
                {
                    selectedCentroid = previousCentroid;
                    return true;
                }

                ResetContact();
                return false;
            }

            // 正常にクラスタが選定されたので瞬断カウンタをリセット
            lostContactFrames = 0;
            isContacting = true;
            selectedCentroid = bestCentroid != Vector3.zero ? bestCentroid : closestToPlaneCentroid;
            previousCentroid = selectedCentroid;
            return true;
        }
    }
}
