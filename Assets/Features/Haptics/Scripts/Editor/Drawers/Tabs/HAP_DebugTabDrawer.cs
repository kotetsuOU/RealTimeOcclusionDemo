#if UNITY_EDITOR
#nullable enable
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Features.Haptics.Editor
{
    /// <summary>
    /// HAP_AUTDHapticsController の「Debug」タブ描画クラス。
    /// Gizmo 表示切り替え、特定デバイスの強制停止（Debug Mute）、パフォーマンス計測、および AppLogManager 連携を提供します。
    /// </summary>
    public static class HAP_DebugTabDrawer
    {
        private const string PrefFoldGizmos = "HAP_DebugTab_FoldGizmos";
        private const string PrefFoldMute = "HAP_DebugTab_FoldMute";
        private const string PrefFoldPerf = "HAP_DebugTab_FoldPerf";

        /// <summary>
        /// Debug タブの Inspector UI を描画します。
        /// </summary>
        public static void Draw(HAP_EditorContext ctx)
        {
            EditorGUILayout.LabelField("⏱️ デバッグ & パフォーマンスツール", EditorStyles.boldLabel);

            DrawGizmosSection(ctx);
            EditorGUILayout.Space(6);

            DrawDeviceMuteSection(ctx);
            EditorGUILayout.Space(6);

            DrawProfilingAndLoggingSection(ctx);
        }

        private static void DrawGizmosSection(HAP_EditorContext ctx)
        {
            bool fold = SessionState.GetBool(PrefFoldGizmos, true);
            fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, "👁️ Scene ビュー可視化 (Gizmos)");
            SessionState.SetBool(PrefFoldGizmos, fold);

            if (fold)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (ctx.VisualizeDevicesProp != null)
                    {
                        EditorGUILayout.PropertyField(ctx.VisualizeDevicesProp, new GUIContent("AUTD デバイス枠線・グルーピングを表示"));
                    }
                    if (ctx.DbgShowGizmoProp != null)
                    {
                        EditorGUILayout.PropertyField(ctx.DbgShowGizmoProp, new GUIContent("キャリブレーション焦点 Gizmo / ハンドルを表示"));
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void DrawDeviceMuteSection(HAP_EditorContext ctx)
        {
            bool fold = SessionState.GetBool(PrefFoldMute, true);
            fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, "🔇 特定デバイスの強制停止 (Debug Mute)");
            SessionState.SetBool(PrefFoldMute, fold);

            if (fold)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var disabler = EnsureDebugDisabler(ctx);
                    if (disabler != null)
                    {
                        DrawDeviceMuteGrid(disabler);
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static HAP_AUTDDebugDisabler? EnsureDebugDisabler(HAP_EditorContext ctx)
        {
            var disabler = ctx.Controller.debugDisabler ?? ctx.Controller.GetComponent<HAP_AUTDDebugDisabler>();
            if (disabler != null)
            {
                ctx.Controller.debugDisabler = disabler;
                return disabler;
            }

            if (GUILayout.Button("DebugDisabler をアタッチして有効化", GUILayout.Height(24)))
            {
                disabler = ctx.Controller.gameObject.AddComponent<HAP_AUTDDebugDisabler>();
                ctx.Controller.debugDisabler = disabler;
                EditorUtility.SetDirty(ctx.Controller);
                ctx.RefreshLinkedObjects();
            }
            return disabler;
        }

        private static void DrawDeviceMuteGrid(HAP_AUTDDebugDisabler disabler)
        {
            var devices = Object.FindObjectsByType<AUTD3Device>(FindObjectsSortMode.None)
                .OrderBy(d => d.ID)
                .ToArray();

            if (devices.Length == 0)
            {
                EditorGUILayout.HelpBox("シーン内に有効な AUTD3Device が見つかりません。", MessageType.Info);
                return;
            }

            while (disabler.disabledDevices.Count < devices.Length)
            {
                disabler.disabledDevices.Add(false);
            }

            EditorGUILayout.HelpBox("ON にしたデバイスはすべての出力で強制ミュート (Null) になります。", MessageType.None);

            EditorGUI.BeginChangeCheck();
            for (int i = 0; i < devices.Length; i += 2)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawSingleDeviceMuteToggle(disabler, i, devices[i].ID);
                    if (i + 1 < devices.Length)
                    {
                        DrawSingleDeviceMuteToggle(disabler, i + 1, devices[i + 1].ID);
                    }
                    else
                    {
                        GUILayout.FlexibleSpace();
                    }
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(disabler);
            }
        }

        private static void DrawSingleDeviceMuteToggle(HAP_AUTDDebugDisabler disabler, int index, int deviceId)
        {
            bool currentVal = disabler.disabledDevices[index];
            Color prevBg = GUI.backgroundColor;
            if (currentVal) GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);

            disabler.disabledDevices[index] = EditorGUILayout.ToggleLeft($"Mute AUTD #{deviceId}", currentVal);
            GUI.backgroundColor = prevBg;
        }

        private static void DrawProfilingAndLoggingSection(HAP_EditorContext ctx)
        {
            bool fold = SessionState.GetBool(PrefFoldPerf, false);
            fold = EditorGUILayout.BeginFoldoutHeaderGroup(fold, "⏱️ パフォーマンス計測 & 統合ログ (AppLogger)");
            SessionState.SetBool(PrefFoldPerf, fold);

            if (fold)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (ctx.EnableProfilingProp != null)
                    {
                        EditorGUILayout.PropertyField(ctx.EnableProfilingProp, new GUIContent("プロファイリング有効化"));
                    }

                    if (ctx.EnableProfilingProp != null && ctx.EnableProfilingProp.boolValue)
                    {
                        EditorGUI.indentLevel++;
                        if (ctx.SynchronousSendProp != null) EditorGUILayout.PropertyField(ctx.SynchronousSendProp, new GUIContent("同期送信 (メインスレッド同期)"));
                        if (ctx.ProfilingLogIntervalProp != null) EditorGUILayout.PropertyField(ctx.ProfilingLogIntervalProp, new GUIContent("ログ出力間隔 (フレーム数)"));
                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.Space(4);
                    if (GUILayout.Button("AppLogManager を選択（ログ出力制御）", GUILayout.Height(24)))
                    {
                        var logManagerType = System.Type.GetType("Core.Logging.AppLogManager, Assembly-CSharp")
                                          ?? System.Type.GetType("AppLogManager, Assembly-CSharp");
                        if (logManagerType != null)
                        {
                            var logManagerObj = Object.FindFirstObjectByType(logManagerType);
                            if (logManagerObj != null)
                            {
                                Selection.activeObject = logManagerObj;
                            }
                        }
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
    }
}
#endif
