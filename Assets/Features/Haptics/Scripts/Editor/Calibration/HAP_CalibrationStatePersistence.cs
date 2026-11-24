#if UNITY_EDITOR
#nullable enable
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Debug;

namespace Features.Haptics.Editor
{
    /// <summary>
    /// <see cref="HAP_AUTDCalibration"/> の PlayMode 中編集内容を EditorPrefs へ保存・復元する永続化ヘルパー。
    /// </summary>
    public static class HAP_CalibrationStatePersistence
    {
        private const string PrefsKey = "HAP_AUTDCalibration_State";

        [Serializable]
        private class CalibrationState
        {
            public bool enableCalibration;
            public bool[]? targetDevices;
            public bool useMultiFocus;
            public Vector3 singleFocusPosition;
            public Vector3[]? multiFocusPositions;
            public float focusAmplitude;
            public Vector3 autdOffset;
        }

        /// <summary>
        /// 現在のキャリブレーションパラメータと全体オフセットを EditorPrefs に保存します。
        /// </summary>
        public static void SaveState(HAP_AUTDCalibration? script)
        {
            if (script == null) return;

            var state = new CalibrationState
            {
                enableCalibration = script.enableCalibration,
                targetDevices = script.targetDevices.ToArray(),
                useMultiFocus = script.useMultiFocus,
                singleFocusPosition = script.singleFocusPosition,
                multiFocusPositions = script.multiFocusPositions.ToArray(),
                focusAmplitude = script.focusAmplitude,
                autdOffset = script.transformLoader != null ? script.transformLoader.offset : Vector3.zero
            };

            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(state));
        }

        /// <summary>
        /// 保存されていたキャリブレーション状態を EditMode 復帰後に復元し、保存済みキーを削除します。
        /// </summary>
        public static void RestoreState(HAP_AUTDCalibration? script, SerializedObject serializedObject)
        {
            if (script == null || !EditorPrefs.HasKey(PrefsKey)) return;

            string json = EditorPrefs.GetString(PrefsKey);
            try
            {
                var state = JsonUtility.FromJson<CalibrationState>(json);
                if (state != null)
                {
                    Undo.RecordObject(script, "Restore Calibration State");

                    script.enableCalibration = state.enableCalibration;
                    if (state.targetDevices != null) script.targetDevices = state.targetDevices.ToList();
                    script.useMultiFocus = state.useMultiFocus;
                    script.singleFocusPosition = state.singleFocusPosition;
                    if (state.multiFocusPositions != null) script.multiFocusPositions = state.multiFocusPositions.ToList();
                    script.focusAmplitude = state.focusAmplitude;

                    if (script.transformLoader != null)
                    {
                        Undo.RecordObject(script.transformLoader, "Restore AUTD Offset");
                        script.transformLoader.offset = state.autdOffset;
                        EditorUtility.SetDirty(script.transformLoader);
                    }

                    EditorUtility.SetDirty(script);
                    serializedObject.Update();
                }

                EditorPrefs.DeleteKey(PrefsKey);
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning(script, HAP_LogTriggers.TagCalibration,
                    $"[Calibration] Failed to restore calibration state: {ex.Message}");
            }
        }
    }
}
#endif

