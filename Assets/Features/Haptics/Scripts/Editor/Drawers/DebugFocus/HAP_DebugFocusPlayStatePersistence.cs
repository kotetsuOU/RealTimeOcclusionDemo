#if UNITY_EDITOR
#nullable enable
using System;
using UnityEditor;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Debug;

namespace Features.Haptics.Editor
{
    /// <summary>
    /// 単焦点デバッグ照射における PlayMode 中の座標・設定・Calibration Offset 変更内容を
    /// Play 停止後も維持するためのスナップショット永続化クラス。
    /// </summary>
    [InitializeOnLoad]
    public static class HAP_DebugFocusPlayStatePersistence
    {
        private const string PrefPreserveKey = "HAP_DebugFocus_PreservePlayChanges";
        private const string PrefStateKey = "HAP_DebugFocus_SavedPlayState";

        [Serializable]
        private class PlayModeCalibrationSnapshot
        {
            public string controllerObjectName = "";
            public bool enableDebugFocus;
            public Vector3 focusPosition;
            public bool maximizeDuty;
            public float focusIntensityPascal;
            public bool forceSineModulation;
            public float debugSineFrequency;
            public bool applyCalibrationOffset;
            public bool showFocusGizmo;
            public Vector3 calibrationOffset;
        }

        static HAP_DebugFocusPlayStatePersistence()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// Play モード中の編集内容を終了後も自動保持するかどうかを取得／設定します。
        /// </summary>
        public static bool PreservePlayChanges
        {
            get => EditorPrefs.GetBool(PrefPreserveKey, true);
            set
            {
                EditorPrefs.SetBool(PrefPreserveKey, value);
                if (!value)
                {
                    EditorPrefs.DeleteKey(PrefStateKey);
                }
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                RestorePlayStateIfNeeded();
            }
        }

        /// <summary>
        /// 現在の単焦点設定および全体オフセットを EditorPrefs へスナップショット保存します。
        /// </summary>
        public static void SavePlayState(HAP_AUTDHapticsController? ctrl)
        {
            if (ctrl == null) return;

            var snapshot = new PlayModeCalibrationSnapshot
            {
                controllerObjectName = ctrl.gameObject.name,
                enableDebugFocus = ctrl.debugFocusConfig.enableDebugFocus,
                focusPosition = ctrl.debugFocusConfig.focusPosition,
                maximizeDuty = ctrl.debugFocusConfig.maximizeDuty,
                focusIntensityPascal = ctrl.debugFocusConfig.focusIntensityPascal,
                forceSineModulation = ctrl.debugFocusConfig.forceSineModulation,
                debugSineFrequency = ctrl.debugFocusConfig.debugSineFrequency,
                applyCalibrationOffset = ctrl.debugFocusConfig.applyCalibrationOffset,
                showFocusGizmo = ctrl.debugFocusConfig.showFocusGizmo,
                calibrationOffset = ctrl.offset,
            };

            EditorPrefs.SetString(PrefStateKey, JsonUtility.ToJson(snapshot));
        }

        /// <summary>
        /// EditMode 復帰時に、保存されていたスナップショットをコントローラーおよび TransformLoader へ復元します。
        /// </summary>
        public static void RestorePlayStateIfNeeded()
        {
            if (!EditorPrefs.HasKey(PrefStateKey)) return;

            string json = EditorPrefs.GetString(PrefStateKey);
            EditorPrefs.DeleteKey(PrefStateKey);

            if (!PreservePlayChanges || string.IsNullOrEmpty(json)) return;

            try
            {
                var snapshot = JsonUtility.FromJson<PlayModeCalibrationSnapshot>(json);
                if (snapshot == null) return;

                HAP_AUTDHapticsController? ctrl = null;
                if (!string.IsNullOrEmpty(snapshot.controllerObjectName))
                {
                    var candidates = UnityEngine.Object.FindObjectsByType<HAP_AUTDHapticsController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    foreach (var candidate in candidates)
                    {
                        if (candidate != null && candidate.gameObject.name == snapshot.controllerObjectName)
                        {
                            ctrl = candidate;
                            break;
                        }
                    }
                }
                ctrl ??= UnityEngine.Object.FindAnyObjectByType<HAP_AUTDHapticsController>();
                if (ctrl == null) return;

                Undo.RecordObject(ctrl, "Preserve Debug Focus PlayMode Changes");
                ctrl.debugFocusConfig.enableDebugFocus = snapshot.enableDebugFocus;
                ctrl.debugFocusConfig.focusPosition = snapshot.focusPosition;
                ctrl.debugFocusConfig.maximizeDuty = snapshot.maximizeDuty;
                ctrl.debugFocusConfig.focusIntensityPascal = snapshot.focusIntensityPascal;
                ctrl.debugFocusConfig.forceSineModulation = snapshot.forceSineModulation;
                ctrl.debugFocusConfig.debugSineFrequency = snapshot.debugSineFrequency > 0f ? snapshot.debugSineFrequency : 150f;
                ctrl.debugFocusConfig.applyCalibrationOffset = snapshot.applyCalibrationOffset;
                ctrl.debugFocusConfig.showFocusGizmo = snapshot.showFocusGizmo;
                EditorUtility.SetDirty(ctrl);

                if (ctrl.transformLoader != null)
                {
                    Undo.RecordObject(ctrl.transformLoader, "Preserve Calibration Offset");
                    ctrl.transformLoader.offset = snapshot.calibrationOffset;
                    EditorUtility.SetDirty(ctrl.transformLoader);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagCalibration,
                    $"[HAP_DebugFocusPlayStatePersistence] Failed to restore PlayMode state: {ex.Message}");
            }
        }
    }
}
#endif

