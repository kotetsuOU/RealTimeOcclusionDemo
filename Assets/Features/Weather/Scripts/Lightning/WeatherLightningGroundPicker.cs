using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 落雷の着弾地点を矩形平面または前方円環エリアからサンプリングし、
    /// Physics Raycast および Linecast (視界見通し) により適切な着地面座標を選定する純粋幾何ロジッククラス。
    /// 完全な Zero-GC で動作します。
    /// </summary>
    public class WeatherLightningGroundPicker
    {
        public bool IsFixedBounds { get; set; } = true;
        public bool FollowViewer { get; set; } = false;
        public Vector2 StrikeAreaCenter { get; set; } = Vector2.zero;
        public Vector2 StrikeAreaSize { get; set; } = new Vector2(0.55f, 0.35f);
        public float StrikeMinRadius { get; set; } = 0.05f;
        public float StrikeMaxRadius { get; set; } = 0.35f;
        public float StrikeFov { get; set; } = 80f;
        public bool RequireLineOfSight { get; set; } = true;
        public LayerMask EnvironmentMask { get; set; } = ~0;

        /// <summary>
        /// 指定した空間設定および視点情報に基づき、最適な着地面座標を算出します。
        /// </summary>
        /// <param name="viewer">視点 Transform (null の場合は defaultOrigin を使用)</param>
        /// <param name="defaultOrigin">フォールバック基準 Transform</param>
        /// <param name="cloudY">雲の高度 (Raycast 開始 Y 座標)</param>
        /// <param name="groundY">想定床面 Y 座標</param>
        /// <param name="at">明示的な指定座標 (null の場合は自動選定)</param>
        /// <param name="ground">算出された着地点座標 (出力)</param>
        /// <returns>着地点の選定に成功したか</returns>
        public bool TryPickGround(
            Transform viewer,
            Transform defaultOrigin,
            float cloudY,
            float groundY,
            Vector3? at,
            out Vector3 ground)
        {
            Vector3 center = viewer != null ? viewer.position : (defaultOrigin != null ? defaultOrigin.position : Vector3.zero);
            Vector3 forward = viewer != null ? viewer.forward : (defaultOrigin != null ? defaultOrigin.forward : Vector3.forward);
            forward.y = 0f;
            float baseYaw = forward.sqrMagnitude > 1e-5f ? Mathf.Atan2(forward.x, forward.z) : 0f;

            int attempts = at.HasValue ? 1 : 8;
            for (int k = 0; k < attempts; k++)
            {
                Vector3 xz;
                if (at.HasValue)
                {
                    xz = at.Value;
                }
                else if (IsFixedBounds)
                {
                    // General の Shared Space Bounds (X, Z 矩形平面) からサンプリング
                    float baseX = FollowViewer && viewer != null ? viewer.position.x : 0f;
                    float baseZ = FollowViewer && viewer != null ? viewer.position.z : 0f;

                    float halfX = Mathf.Max(0.05f, StrikeAreaSize.x * 0.5f);
                    float halfZ = Mathf.Max(0.05f, StrikeAreaSize.y * 0.5f);
                    float randX = Random.Range(-halfX, halfX);
                    float randZ = Random.Range(-halfZ, halfZ);
                    xz = new Vector3(baseX + StrikeAreaCenter.x + randX, 0f, baseZ + StrikeAreaCenter.y + randZ);
                }
                else
                {
                    // 円環モードであっても表示空間 (SRDisplay 0.55m) を逸脱しないよう [0.02m, 0.30m] に安全クランプ
                    float safeMin = Mathf.Clamp(StrikeMinRadius, 0.02f, 0.15f);
                    float safeMax = Mathf.Clamp(StrikeMaxRadius, safeMin + 0.05f, 0.30f);
                    float r = Mathf.Sqrt(Random.Range(safeMin * safeMin, safeMax * safeMax));
                    float yaw = baseYaw + Random.Range(-0.5f, 0.5f) * StrikeFov * Mathf.Deg2Rad;
                    xz = new Vector3(center.x + r * Mathf.Sin(yaw), 0f, center.z + r * Mathf.Cos(yaw));
                }

                Vector3 rayStart = new Vector3(xz.x, cloudY, xz.z);
                float rayDist = Mathf.Max(0.5f, cloudY - groundY + 0.5f);

                ground = Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, rayDist, EnvironmentMask, QueryTriggerInteraction.Ignore)
                    ? hit.point
                    : new Vector3(xz.x, groundY, xz.z);

                if (at.HasValue || !RequireLineOfSight || viewer == null)
                {
                    return true;
                }

                // 視点から着弾点が見通せるかチェック (遮蔽物の向こう側に落ちるのを防止)
                if (!Physics.Linecast(viewer.position, ground + Vector3.up * 0.05f, EnvironmentMask, QueryTriggerInteraction.Ignore))
                {
                    return true;
                }
            }

            // フォールバック: 指定エリアの中央に確実に着弾させる
            float fbX = (IsFixedBounds && FollowViewer && viewer != null) ? (viewer.position.x + StrikeAreaCenter.x) : StrikeAreaCenter.x;
            float fbZ = (IsFixedBounds && FollowViewer && viewer != null) ? (viewer.position.z + StrikeAreaCenter.y) : StrikeAreaCenter.y;
            ground = new Vector3(fbX, groundY, fbZ);
            return true;
        }
    }
}
