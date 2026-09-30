using UnityEngine;

namespace Features.PhysicalResponse
{
    /// <summary>
    /// 持ち上げによる変位量計算、感度乗算、Transform への差分加算、
    /// および落下減衰処理を担当するプロセッサクラス。
    /// オブジェクトの前進アニメーションや自律移動を上書きしない加算方式を採用しています。
    /// </summary>
    public class PR_LiftMotionApplier
    {
        private float appliedLiftOffset = 0f;
        private Vector3 appliedHorizontalOffset = Vector3.zero;
        private Vector3 contactStartHandCentroid;
        private Vector3 lastPlaneNormal = Vector3.up;

        public float AppliedLiftOffset => appliedLiftOffset;
        public Vector3 AppliedHorizontalOffset => appliedHorizontalOffset;
        public Vector3 ContactStartHandCentroid => contactStartHandCentroid;
        public Vector3 LastPlaneNormal => lastPlaneNormal;

        /// <summary>
        /// 状態をリセットします。
        /// </summary>
        public void Reset()
        {
            appliedLiftOffset = 0f;
            appliedHorizontalOffset = Vector3.zero;
            contactStartHandCentroid = Vector3.zero;
            lastPlaneNormal = Vector3.up;
        }

        /// <summary>
        /// 接触開始時に呼び出され、空中再接触時のオフセット引き継ぎまたは初期化を行います。
        /// </summary>
        public void OnContactStart(Vector3 currentCentroid, Vector3 planeNormal, float liftSensitivity)
        {
            lastPlaneNormal = planeNormal;

            if (appliedLiftOffset > 0.005f)
            {
                // 既に空中にいる場合: 現在の浮遊高さを基準として引き継ぎ、地面へのワープ（ストン落ち）を防止
                float safeSensitivity = Mathf.Max(0.001f, liftSensitivity);
                float requiredHandLift = appliedLiftOffset / safeSensitivity;
                contactStartHandCentroid = currentCentroid - planeNormal * requiredHandLift;
            }
            else
            {
                appliedLiftOffset = 0f;
                appliedHorizontalOffset = Vector3.zero;
                contactStartHandCentroid = currentCentroid;
            }
        }

        /// <summary>
        /// 接触中の追従移動計算を実行します（差分加算方式）。
        /// </summary>
        public void ApplyLiftTracking(
            Transform targetTransform,
            Vector3 currentCentroid,
            Vector3 previousCentroid,
            Vector3 planeNormal,
            LiftCalculationMode mode,
            float liftSensitivity,
            float minLiftHeight,
            float maxLiftHeight,
            float maxLiftDelta,
            bool followHorizontalHand)
        {
            if (targetTransform == null) return;
            lastPlaneNormal = planeNormal;

            if (mode == LiftCalculationMode.InitialPositionPlusLift)
            {
                // モードA: 初期接触位置からの総変位加算
                Vector3 totalDelta = currentCentroid - contactStartHandCentroid;

                // 持ち上げ方向（法線方向）の成分
                float liftAmount = Vector3.Dot(totalDelta, planeNormal);

                // 上方向への移動のみ liftSensitivity を乗算（下方向への移動には増幅をかけない）
                float scaledLift = liftAmount > 0f ? (liftAmount * liftSensitivity) : liftAmount;

                // 上下変位のクランプ
                float targetLift = Mathf.Clamp(scaledLift, minLiftHeight, maxLiftHeight);

                // 【加算方式】前フレームからの差分のみを加算（前進移動を阻害しない）
                float deltaLift = targetLift - appliedLiftOffset;
                targetTransform.position += planeNormal * deltaLift;
                appliedLiftOffset = targetLift;

                // 手の水平移動にも追従する場合（差分加算）
                if (followHorizontalHand)
                {
                    Vector3 targetHorizontal = totalDelta - Vector3.Project(totalDelta, planeNormal);
                    Vector3 deltaHorizontal = targetHorizontal - appliedHorizontalOffset;
                    targetTransform.position += deltaHorizontal;
                    appliedHorizontalOffset = targetHorizontal;
                }
            }
            else
            {
                // モードB: 従来のフレーム間差分積算モード（加算方式）
                Vector3 delta = currentCentroid - previousCentroid;
                float liftAmount = Vector3.Dot(delta, planeNormal);
                float scaledLift = liftAmount > 0f ? (liftAmount * liftSensitivity) : liftAmount;
                float clampedLift = Mathf.Clamp(scaledLift, -maxLiftDelta, maxLiftDelta);

                targetTransform.position += planeNormal * clampedLift;
                appliedLiftOffset = Mathf.Max(0f, appliedLiftOffset + clampedLift);
            }
        }

        /// <summary>
        /// 非接触時に持ち上げオフセットを 0（接地）に向けて減衰させます（落下処理）。
        /// </summary>
        public void ApplyFall(Transform targetTransform, float fallSpeed, float deltaTime, bool followHorizontalHand)
        {
            if (targetTransform == null) return;

            // 加算していた持ち上げ高さを 0 へ減算していく
            if (appliedLiftOffset > 0.0001f)
            {
                float newLift = Mathf.MoveTowards(appliedLiftOffset, 0f, fallSpeed * deltaTime);
                float deltaLift = newLift - appliedLiftOffset;
                targetTransform.position += lastPlaneNormal * deltaLift;
                appliedLiftOffset = newLift;
            }
            else
            {
                appliedLiftOffset = 0f;
            }

            // 水平オフセットも減衰
            if (followHorizontalHand && appliedHorizontalOffset.sqrMagnitude > 1e-6f)
            {
                Vector3 newHorizontal = Vector3.MoveTowards(appliedHorizontalOffset, Vector3.zero, fallSpeed * deltaTime);
                Vector3 deltaHorizontal = newHorizontal - appliedHorizontalOffset;
                targetTransform.position += deltaHorizontal;
                appliedHorizontalOffset = newHorizontal;
            }
        }
    }
}
