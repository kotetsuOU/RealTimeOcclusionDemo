#nullable enable
using System.Collections.Generic;
using UnityEngine;
using Features.Haptics.Hardware;

namespace Features.Haptics.Debug
{
    /// <summary>
    /// 触覚錯覚カスタムコントローラー群（<see cref="HAP_HapticsIllusionCustomController"/>、
    /// <see cref="HAP_HapticsIllusionFoxFootController"/>）の Scene ビュー Gizmo 描画を担当する専任クラス。
    /// </summary>
    public static class HAP_IllusionGizmoDrawer
    {
        private static readonly Color[] ActiveFocusColors = { Color.cyan, Color.magenta, Color.yellow, Color.green };

        /// <summary>
        /// <see cref="HAP_HapticsIllusionCustomController"/> の焦点位置・STM 軌跡・照射可否ステータスを描画します。
        /// </summary>
        public static void DrawCustomIllusionGizmos(HAP_HapticsIllusionCustomController controller)
        {
            HAP_AUTDHapticsController? hapticsCtrl = controller.autdController;
#if UNITY_EDITOR
            if (hapticsCtrl == null)
            {
                hapticsCtrl = Object.FindAnyObjectByType<HAP_AUTDHapticsController>();
            }
#endif
            HAP_AUTDDebugDisabler? disabler = hapticsCtrl != null
                ? hapticsCtrl.GetComponent<HAP_AUTDDebugDisabler>()
                : null;
            bool useDirectional = hapticsCtrl != null && hapticsCtrl.enableDirectionalGrouping;
            float angleThreshold = hapticsCtrl != null ? hapticsCtrl.directionalAngleThreshold : 45f;
            AUTD3Device[]? sceneDevices = useDirectional ? HAP_GizmoSceneTargetResolver.GetCachedDevices() : null;

            int idx = 0;
            foreach (var cfg in controller.focusConfigs)
            {
                if (cfg.targetTransform == null) continue;

                Vector3 centerPos = cfg.targetTransform.position + cfg.targetTransform.TransformDirection(cfg.offsetPosition);
                var deviceIds = cfg.assignedDeviceGroup != null
                    ? cfg.assignedDeviceGroup.SelectedDeviceIDs
                    : new List<int>();

                Color fallbackColor = ActiveFocusColors[idx % ActiveFocusColors.Length];
                Color gizmoColor = EvaluateFocusStatus(
                    cfg, deviceIds, disabler, useDirectional, angleThreshold, sceneDevices, fallbackColor, out string statusLabel);

                Gizmos.color = gizmoColor;
                Gizmos.DrawWireSphere(centerPos, 0.003f);

                if (cfg.useSTM && cfg.stmRadius > 0f)
                {
                    DrawStmCircle(centerPos, cfg.targetTransform.right, cfg.targetTransform.up, cfg.stmRadius, gizmoColor, 24);
                }

#if UNITY_EDITOR
                string deviceLabel = deviceIds.Count > 0 ? $"AUTD [{string.Join(",", deviceIds)}]" : "AUTD [none]";
                if (!string.IsNullOrEmpty(statusLabel)) deviceLabel += " " + statusLabel;
                UnityEditor.Handles.color = gizmoColor;
                UnityEditor.Handles.Label(centerPos + Vector3.up * 0.015f, deviceLabel);
#endif
                idx++;
            }
        }

        /// <summary>
        /// <see cref="HAP_HapticsIllusionFoxFootController"/> の接点側・反対側焦点および STM 軌跡を描画します。
        /// </summary>
        public static void DrawFoxFootIllusionGizmos(HAP_HapticsIllusionFoxFootController controller)
        {
            Vector3 normal = controller.footTargetTouchDirection.normalized;
            foreach (var info in controller.TargetInfos)
            {
                if (info.Transform == null) continue;

                bool isActive = controller.IsTargetActive(info.Transform, info.IsEnabled, info.IsTail);
                Vector3 footPos = info.Transform.position;

                Gizmos.color = isActive ? controller.activeColor : controller.inactiveColor;
                Vector3 contactPos = footPos + controller.contactOffset;
                Gizmos.DrawWireSphere(contactPos, 0.005f);

                if (isActive && controller.useSTM && controller.stmRadius > 0f)
                {
                    DrawStmCircleFromNormal(contactPos, normal, controller.stmRadius, Color.cyan, 20);
                }

                if (controller.enableOppositeFocus)
                {
                    Vector3 oppositePos = footPos + controller.oppositeOffset;
                    Gizmos.color = isActive ? Color.magenta : Color.gray;
                    Gizmos.DrawWireSphere(oppositePos, 0.005f);
                    Gizmos.DrawLine(contactPos, oppositePos);

                    if (isActive && controller.useSTM && controller.stmRadius > 0f)
                    {
                        DrawStmCircleFromNormal(oppositePos, -normal, controller.stmRadius, Color.magenta, 20);
                    }
                }
            }
        }

        private static Color EvaluateFocusStatus(
            HapticsIllusionTargetConfig cfg,
            List<int> deviceIds,
            HAP_AUTDDebugDisabler? disabler,
            bool useDirectional,
            float angleThreshold,
            AUTD3Device[]? sceneDevices,
            Color activeColor,
            out string statusLabel)
        {
            if (!cfg.isEnabled)
            {
                statusLabel = "[disabled]";
                return Color.gray;
            }

            if (disabler != null && deviceIds.Count > 0 && deviceIds.TrueForAll(id => disabler.IsDisabled(id)))
            {
                statusLabel = "[all devices disabled]";
                return new Color(1f, 0.2f, 0.2f, 1f);
            }

            if (useDirectional && deviceIds.Count > 0 && sceneDevices != null)
            {
                bool anyInRange = false;
                foreach (var dev in sceneDevices)
                {
                    if (dev == null) continue;
                    if (disabler != null && disabler.IsDisabled(dev.ID)) continue;
                    if (!deviceIds.Contains(dev.ID)) continue;

                    Vector3 devForward = HAP_DeviceOrientationHelper.GetAdjustedForward(dev);
                    if (Vector3.Angle(devForward, -cfg.targetTransform!.forward) <= angleThreshold)
                    {
                        anyInRange = true;
                        break;
                    }
                }

                if (!anyInRange)
                {
                    statusLabel = "[angle NG]";
                    return new Color(1f, 0.6f, 0f, 1f);
                }
            }

            statusLabel = "";
            return activeColor;
        }

        private static void DrawStmCircleFromNormal(Vector3 center, Vector3 normal, float radius, Color color, int segments)
        {
            Vector3 right = Vector3.Cross(normal, Vector3.up);
            if (right.sqrMagnitude < 0.001f)
            {
                right = Vector3.Cross(normal, Vector3.right);
            }
            right.Normalize();
            Vector3 up = Vector3.Cross(right, normal).normalized;
            DrawStmCircle(center, right, up, radius, color, segments);
        }

        private static void DrawStmCircle(Vector3 center, Vector3 right, Vector3 up, float radius, Color color, int segments)
        {
            Gizmos.color = color;
            Vector3 prev = center + right * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = (2.0f * Mathf.PI * i) / segments;
                Vector3 next = center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}

