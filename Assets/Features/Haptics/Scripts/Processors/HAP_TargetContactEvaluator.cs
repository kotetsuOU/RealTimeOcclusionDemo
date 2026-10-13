using UnityEngine;
using Features.HapticsCollision.Core;

#nullable enable

namespace Features.Haptics.Processors
{
    /// <summary>
    /// オブジェクト触覚ターゲット（部位）のアクティブ判定（空中判定・手との近接接触判定）を担当する評価クラス。
    /// コントローラー本体から判定ロジックを分離し、Pure C# 的な責務として集約しています。
    /// </summary>
    public static class HAP_TargetContactEvaluator
    {
        /// <summary>
        /// 指定されたターゲット情報が現在ハプティクス照射可能なアクティブ状態であるかどうかを判定します。
        /// </summary>
        public static bool IsTargetActive(
            HAP_BaseObjectHapticsController controller,
            HapticsTargetInfo info)
        {
            if (controller == null || info.Transform == null) return false;
            if (!info.IsEnabled) return false;

            // 1. 接地判定（空中判定。足などの空中判定対象パーツのみ適用）
            if (info.ShouldApplyAirborneCheck && controller.disableWhenInAir)
            {
                Transform? effectiveRoot = GetEffectiveRootTransform(controller, info.Transform);
                if (effectiveRoot != null)
                {
                    Vector3 pos = info.Transform.position + info.Offset;
                    float relHeight = pos.y - effectiveRoot.position.y;
                    if (relHeight > controller.airborneHeightThreshold) return false;
                }
            }

            // 2. 手との近接接触判定（個別しきい値が設定されていればそれを優先、なければ共通しきい値を使用）
            if (controller.onlyTargetHandContact)
            {
                float threshold = info.HandContactThreshold > 0f ? info.HandContactThreshold : controller.handContactThreshold;
                bool hasContact = false;
                HCD_Pipeline? pipeline = (controller.autdController != null) ? controller.autdController.hcdPipeline : null;
                if (pipeline == null) pipeline = HCD_Pipeline.Instance ?? Object.FindAnyObjectByType<HCD_Pipeline>();

                if (pipeline != null)
                {
                    var clusters = pipeline.GetTrackedClusters();
                    if (clusters != null)
                    {
                        Vector3 pos = info.Transform.position + info.Offset;
                        foreach (var c in clusters)
                        {
                            if (c.IsAlive && Vector3.Distance(c.Centroid, pos) <= threshold)
                            {
                                hasContact = true;
                                break;
                            }
                        }
                    }
                }
                if (!hasContact) return false;
            }

            return true;
        }

        /// <summary>
        /// 指定されたターゲットが現在ハプティクス照射可能なアクティブ状態であるかどうかを判定します。
        /// </summary>
        public static bool IsTargetActive(
            HAP_BaseObjectHapticsController controller,
            Transform? targetTransform,
            bool isEnabled,
            bool isTail,
            float customContactThreshold = -1f,
            bool? applyAirborneCheck = null)
        {
            if (controller == null || targetTransform == null) return false;
            if (!isEnabled) return false;

            // 1. 接地判定（空中判定。足などの非Tailパーツまたは明示的に空中判定が有効なパーツのみ適用）
            bool shouldCheckAirborne = applyAirborneCheck ?? !isTail;
            if (shouldCheckAirborne && controller.disableWhenInAir)
            {
                Transform? effectiveRoot = GetEffectiveRootTransform(controller, targetTransform);
                if (effectiveRoot != null)
                {
                    float relHeight = targetTransform.position.y - effectiveRoot.position.y;
                    if (relHeight > controller.airborneHeightThreshold) return false;
                }
            }

            // 2. 手との近接接触判定（個別しきい値指定があれば優先）
            if (controller.onlyTargetHandContact)
            {
                float threshold = customContactThreshold > 0f ? customContactThreshold : controller.handContactThreshold;
                bool hasContact = false;
                HCD_Pipeline? pipeline = (controller.autdController != null) ? controller.autdController.hcdPipeline : null;
                if (pipeline == null) pipeline = HCD_Pipeline.Instance ?? Object.FindAnyObjectByType<HCD_Pipeline>();

                if (pipeline != null)
                {
                    var clusters = pipeline.GetTrackedClusters();
                    if (clusters != null)
                    {
                        foreach (var c in clusters)
                        {
                            if (c.IsAlive && Vector3.Distance(c.Centroid, targetTransform.position) <= threshold)
                            {
                                hasContact = true;
                                break;
                            }
                        }
                    }
                }
                if (!hasContact) return false;
            }

            return true;
        }

        /// <summary>
        /// 接地判定（空中判定）の基準となる有効なキャラクターのルートTransformを取得します。
        /// </summary>
        public static Transform? GetEffectiveRootTransform(
            HAP_BaseObjectHapticsController controller,
            Transform? fallbackTarget = null)
        {
            if (controller == null) return fallbackTarget;

            if (controller.rootTransform != null &&
                controller.rootTransform != controller.transform &&
                controller.rootTransform.gameObject.activeInHierarchy)
            {
                return controller.rootTransform;
            }

            // fallbackTarget から親方向へモデルのルート（Animator または SkinnedMeshRenderer を持つ階層）を探索
            if (fallbackTarget != null && fallbackTarget.gameObject.activeInHierarchy)
            {
                var animator = fallbackTarget.GetComponentInParent<Animator>();
                if (animator != null && animator.gameObject.activeInHierarchy)
                {
                    controller.rootTransform = animator.transform;
                    return controller.rootTransform;
                }
                var smr = fallbackTarget.GetComponentInParent<SkinnedMeshRenderer>();
                if (smr != null && smr.gameObject.activeInHierarchy)
                {
                    controller.rootTransform = smr.transform;
                    return controller.rootTransform;
                }
                if (fallbackTarget.root != null &&
                    fallbackTarget.root != controller.transform.root &&
                    fallbackTarget.root.gameObject.activeInHierarchy)
                {
                    controller.rootTransform = fallbackTarget.root;
                    return controller.rootTransform;
                }
            }

            return controller.rootTransform != null ? controller.rootTransform : controller.transform;
        }
    }
}
