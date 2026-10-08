using UnityEngine;
using Features.Haptics.Processors;
using Features.HapticsCollision.Core;

#nullable enable

namespace Features.Haptics.Debug
{
    /// <summary>
    /// HAP_BaseObjectHapticsController 派生クラスのターゲット部位、接地判定線、手との接触判定球などの
    /// Sceneビュー Gizmo 描画処理を担当するヘルパークラス。
    /// コントローラー本体から Gizmo 描画の肥大化したコードを分離します。
    /// </summary>
    public static class HAP_ObjectGizmoDrawer
    {
        /// <summary>
        /// 指定されたオブジェクト触覚コントローラーの全ターゲット Gizmo を描画します。
        /// </summary>
        public static void Draw(HAP_BaseObjectHapticsController controller)
        {
            if (controller == null || !ShouldDrawGizmos(controller)) return;

            foreach (var info in controller.TargetInfos)
            {
                DrawTargetGizmo(controller, info);
            }
        }

        /// <summary>
        /// Gizmo描画を行うべき状態（drawGizmos, enabled, activeInHierarchy かつ sourceMode == ObjectTarget）であるかを判定します。
        /// </summary>
        public static bool ShouldDrawGizmos(HAP_BaseObjectHapticsController controller)
        {
            if (!controller.drawGizmos || !controller.enabled || !controller.gameObject.activeInHierarchy) return false;

            HAP_AUTDHapticsController? ctrl = controller.autdController;
#if UNITY_EDITOR
            if (ctrl == null)
            {
                ctrl = Object.FindAnyObjectByType<HAP_AUTDHapticsController>();
            }
#endif
            if (ctrl != null && ctrl.sourceMode != HapticsSourceMode.ObjectTarget)
            {
                return false;
            }

            return true;
        }

        private static void DrawTargetGizmo(HAP_BaseObjectHapticsController controller, HapticsTargetInfo info)
        {
            if (info.Transform == null) return;

            Vector3 pos = info.Transform.position + info.Offset;
            bool isEnabled = info.IsEnabled;
            bool active = controller.IsTargetActive(info.Transform, isEnabled, info.IsTail);
            bool isGrounded = true;

            Transform? effectiveRoot = controller.GetEffectiveRootTransform(info.Transform);

            if (!info.IsTail && controller.disableWhenInAir && effectiveRoot != null)
            {
                float relHeight = pos.y - effectiveRoot.position.y;
                if (relHeight > controller.airborneHeightThreshold)
                {
                    isGrounded = false;
                    active = false;
                }
            }

            if (controller.onlyTargetHandContact)
            {
                bool hasContact = false;
                HCD_Pipeline? pipeline = (controller.autdController != null) ? controller.autdController.hcdPipeline : null;
                if (pipeline == null) pipeline = Object.FindAnyObjectByType<HCD_Pipeline>();

                if (pipeline != null)
                {
                    var clusters = pipeline.GetTrackedClusters();
                    if (clusters != null)
                    {
                        foreach (var c in clusters)
                        {
                            if (c.IsAlive && Vector3.Distance(c.Centroid, pos) <= controller.handContactThreshold)
                            {
                                hasContact = true;
                                break;
                            }
                        }
                    }
                }
                if (!hasContact)
                {
                    active = false;
                }
            }

            Color baseColor = isEnabled ? controller.activeColor : controller.inactiveColor;

            // 1. 接地判定（高さ判定）の可視化線を描画 (足のみ適用)
            if (!info.IsTail && controller.disableWhenInAir && effectiveRoot != null)
            {
                Vector3 groundPt = new Vector3(pos.x, effectiveRoot.position.y, pos.z);
                Vector3 threshPt = new Vector3(pos.x, effectiveRoot.position.y + controller.airborneHeightThreshold, pos.z);

                // 許容高さしきい値を示す小さな十字を描画
                Gizmos.color = baseColor;
                Gizmos.DrawLine(threshPt - Vector3.left * 0.01f, threshPt + Vector3.left * 0.01f);
                Gizmos.DrawLine(threshPt - Vector3.forward * 0.01f, threshPt + Vector3.forward * 0.01f);

                if (isGrounded)
                {
                    Gizmos.color = baseColor;
                    Gizmos.DrawLine(pos, groundPt);
                }
                else
                {
                    Gizmos.color = baseColor;
                    Gizmos.DrawLine(threshPt, groundPt);

                    Gizmos.color = Color.red;
                    Gizmos.DrawLine(pos, threshPt);
                }
            }

            // 2. 手の接触判定の可視化
            if (controller.onlyTargetHandContact)
            {
                // 接触判定の許容球（接触中: 緑、未接触: 黄色ワイヤー球）を描画
                Gizmos.color = active ? new Color(0f, 1f, 0f, 0.4f) : new Color(1f, 0.9f, 0f, 0.35f);
                Gizmos.DrawWireSphere(pos, controller.handContactThreshold);

                HCD_Pipeline? pipeline = (controller.autdController != null) ? controller.autdController.hcdPipeline : null;
                if (pipeline == null) pipeline = Object.FindAnyObjectByType<HCD_Pipeline>();

                if (pipeline != null)
                {
                    var clusters = pipeline.GetTrackedClusters();
                    if (clusters != null)
                    {
                        foreach (var c in clusters)
                        {
                            if (c.IsAlive && Vector3.Distance(c.Centroid, pos) <= controller.handContactThreshold)
                            {
                                Gizmos.color = controller.activeColor;
                                Gizmos.DrawLine(pos, c.Centroid);
                                Gizmos.DrawWireSphere(c.Centroid, 0.015f);
                                break;
                            }
                        }
                    }
                }
            }

            // 3. 照射ターゲット位置の描画
            if (active)
            {
                Gizmos.color = controller.activeColor;
                Gizmos.DrawSphere(pos, 0.01f);
            }
            else
            {
                Gizmos.color = baseColor;
                Gizmos.DrawWireSphere(pos, 0.01f);
            }
        }
    }
}
