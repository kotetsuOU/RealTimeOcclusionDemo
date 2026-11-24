#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Features.Haptics.Debug
{
    /// <summary>
    /// 単焦点デバッグ照射（<see cref="Config.HAP_DebugFocusConfig"/>）の基準座標、実照射座標、
    /// および有効デバイスからの照射レイを Scene ビューへ Gizmo 描画するユーティリティクラス。
    /// </summary>
    public static class HAP_DebugFocusGizmoDrawer
    {
        private const float FocusSphereRadius = 0.008f;
        private const float CrosshairHalfSize = 0.018f;
        private const double DeviceCacheRefreshIntervalSeconds = 1.0;

        private static AUTD3Device[]? _cachedDevices;
        private static double _lastDeviceScanTime = -10.0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            _cachedDevices = null;
            _lastDeviceScanTime = -10.0;
        }

        /// <summary>
        /// コントローラーの単焦点デバッグ設定に基づき、Scene ビューに Gizmo を描画します。
        /// </summary>
        public static void Draw(HAP_AUTDHapticsController controller)
        {
            if (controller == null) return;

            var cfg = controller.debugFocusConfig;
            if (cfg == null || !cfg.showFocusGizmo) return;

            Matrix4x4 prevMatrix = Gizmos.matrix;
            Color prevColor = Gizmos.color;
            Gizmos.matrix = Matrix4x4.identity;

            try
            {
                Vector3 rawPos = cfg.GetRawFocusPosition();
                Vector3 calOffset = controller.offset;
                Vector3 effectivePos = cfg.GetEffectiveFocusPosition(calOffset);
                bool isEmitting = Application.isPlaying && cfg.enableDebugFocus && !controller.bypassHaptics;

                // 1. 基準焦点位置（ユーザー指定座標）
                Color rawColor = isEmitting ? new Color(1f, 0.85f, 0.1f, 0.95f) : new Color(0.2f, 0.9f, 1f, 0.9f);
                Gizmos.color = rawColor;
                Gizmos.DrawWireSphere(rawPos, FocusSphereRadius);
                DrawCrosshair(rawPos, CrosshairHalfSize);

                // 2. オフセット適用後の実照射座標
                bool hasOffsetShift = cfg.applyCalibrationOffset && (effectivePos - rawPos).sqrMagnitude > 1e-9f;
                if (hasOffsetShift)
                {
                    Gizmos.color = new Color(1f, 0.35f, 0.85f, 0.95f);
                    Gizmos.DrawSphere(effectivePos, FocusSphereRadius * 0.45f);
                    Gizmos.DrawWireSphere(effectivePos, FocusSphereRadius * 1.15f);
                    Gizmos.DrawLine(rawPos, effectivePos);
                }
                else if (isEmitting)
                {
                    Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.45f);
                    Gizmos.DrawSphere(rawPos, FocusSphereRadius * 0.4f);
                }

                // 3. 有効な各デバイス開口面中心からの照射レイ
                DrawDeviceRays(controller, effectivePos, isEmitting);

                // 4. 正解位置（リファレンス）との差分表示
                if (cfg.referenceTrueTarget != null)
                {
                    Vector3 truePos = cfg.referenceTrueTarget.position;
                    Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.95f);
                    Gizmos.DrawWireSphere(truePos, FocusSphereRadius);
                    DrawCrosshair(truePos, CrosshairHalfSize * 0.8f);
                    Gizmos.DrawLine(effectivePos, truePos);
                }

#if UNITY_EDITOR
                DrawEditorLabels(rawPos, effectivePos, calOffset, cfg, isEmitting, hasOffsetShift);
#endif
            }
            finally
            {
                Gizmos.color = prevColor;
                Gizmos.matrix = prevMatrix;
            }
        }

        private static void DrawCrosshair(Vector3 center, float halfSize)
        {
            Gizmos.DrawLine(center + Vector3.left * halfSize, center + Vector3.right * halfSize);
            Gizmos.DrawLine(center + Vector3.down * halfSize, center + Vector3.up * halfSize);
            Gizmos.DrawLine(center + Vector3.back * halfSize, center + Vector3.forward * halfSize);
        }

        private static void DrawDeviceRays(HAP_AUTDHapticsController controller, Vector3 effectivePos, bool isEmitting)
        {
            IReadOnlyList<AUTD3Device> devices = ResolveDevices(controller);
            if (devices.Count == 0) return;

            var disabler = controller.debugDisabler
                        ?? controller.GetComponent<HAP_AUTDDebugDisabler>()
                        ?? controller.GetComponentInChildren<HAP_AUTDDebugDisabler>();

            Gizmos.color = isEmitting ? new Color(1f, 0.7f, 0.15f, 0.45f) : new Color(0.2f, 0.85f, 1f, 0.25f);
            for (int i = 0; i < devices.Count; i++)
            {
                var dev = devices[i];
                if (dev == null || !dev.gameObject.activeInHierarchy) continue;
                if (disabler != null && disabler.IsDisabled(dev.ID)) continue;

                Vector3 apertureCenter = dev.transform.TransformPoint(new Vector3(0.096f, 0.075f, 0f));
                Gizmos.DrawLine(apertureCenter, effectivePos);
            }
        }

        private static IReadOnlyList<AUTD3Device> ResolveDevices(HAP_AUTDHapticsController controller)
        {
            if (Application.isPlaying && controller.hardwareController != null && controller.hardwareController.ConnectedDevices.Count > 0)
            {
                return controller.hardwareController.ConnectedDevices;
            }

            double now = GetTimestamp();
            bool needsRefresh = _cachedDevices == null || (now - _lastDeviceScanTime) >= DeviceCacheRefreshIntervalSeconds;
            if (!needsRefresh)
            {
                for (int i = 0; i < _cachedDevices!.Length; i++)
                {
                    if (_cachedDevices[i] == null)
                    {
                        needsRefresh = true;
                        break;
                    }
                }
            }

            if (needsRefresh)
            {
                _lastDeviceScanTime = now;
                _cachedDevices = Object.FindObjectsByType<AUTD3Device>(FindObjectsSortMode.None);
            }

            return _cachedDevices ?? System.Array.Empty<AUTD3Device>();
        }

        private static double GetTimestamp()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorApplication.timeSinceStartup;
#else
            return Time.realtimeSinceStartupAsDouble;
#endif
        }

#if UNITY_EDITOR
        private static void DrawEditorLabels(
            Vector3 rawPos,
            Vector3 effectivePos,
            Vector3 calOffset,
            Config.HAP_DebugFocusConfig cfg,
            bool isEmitting,
            bool hasOffsetShift)
        {
            string stateLabel = isEmitting ? "[EMITTING]" : (cfg.enableDebugFocus ? "[ARMED]" : "[PREVIEW]");
            string label = $"Debug Focus {stateLabel}\nPos: ({rawPos.x:F3}, {rawPos.y:F3}, {rawPos.z:F3}) m";
            if (hasOffsetShift)
            {
                label += $"\nSent (+Offset): ({effectivePos.x:F3}, {effectivePos.y:F3}, {effectivePos.z:F3}) m";
            }

            UnityEditor.Handles.color = isEmitting ? Color.yellow : Color.cyan;
            UnityEditor.Handles.Label(rawPos + Vector3.up * 0.022f, label);

            if (cfg.referenceTrueTarget != null)
            {
                Vector3 truePos = cfg.referenceTrueTarget.position;
                float diffMm = Vector3.Distance(effectivePos, truePos) * 1000f;
                UnityEditor.Handles.color = Color.green;
                UnityEditor.Handles.Label(truePos + Vector3.up * 0.02f, $"True Target (Diff: {diffMm:F1} mm)");
            }
        }
#endif
    }
}

