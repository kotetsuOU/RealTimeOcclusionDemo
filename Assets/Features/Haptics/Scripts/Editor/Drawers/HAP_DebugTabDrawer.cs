#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Linq;

namespace Features.Haptics.Editor
{
    /// <summary>
    /// HAP_AUTDHapticsController の「Debug & Profiling」タブ描画クラス。
    /// パフォーマンスプロファイラ、Scene Gizmo可視化、特定デバイスの強制停止（ミュート）、およびAppLogManager連携を提供します。
    /// </summary>
    public static class HAP_DebugTabDrawer
    {
        public static void Draw(HAP_EditorContext ctx)
        {
            EditorGUILayout.LabelField("⏱️ パフォーマンス計測 & デバッグツール", EditorStyles.boldLabel);

            // ─── 1. パフォーマンスプロファイリング ───
            EditorGUILayout.LabelField("処理時間プロファイリング", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ctx.EnableProfilingProp != null) EditorGUILayout.PropertyField(ctx.EnableProfilingProp, new GUIContent("プロファイリング有効化"));

                if (ctx.EnableProfilingProp != null && ctx.EnableProfilingProp.boolValue)
                {
                    EditorGUI.indentLevel++;
                    if (ctx.SynchronousSendProp != null) EditorGUILayout.PropertyField(ctx.SynchronousSendProp, new GUIContent("同期送信 (メインスレッド同期)"));
                    if (ctx.ProfilingLogIntervalProp != null) EditorGUILayout.PropertyField(ctx.ProfilingLogIntervalProp, new GUIContent("ログ出力間隔 (フレーム数)"));
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.Space(8);

            // ─── 2. 視覚化設定 (Gizmos) ───
            EditorGUILayout.LabelField("Scene ビュー可視化 (Gizmos)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ctx.VisualizeDevicesProp != null)
                {
                    EditorGUILayout.PropertyField(ctx.VisualizeDevicesProp, new GUIContent("デバイス枠線を表示 (青色枠)"));
                }
            }

            EditorGUILayout.Space(8);

            // ─── 3. デバイス別ミュート（DebugDisabler） ───
            EditorGUILayout.LabelField("特定デバイスの強制停止 (Debug Mute)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var disabler = ctx.Controller.debugDisabler;
                if (disabler == null)
                {
                    disabler = ctx.Controller.GetComponent<HAP_AUTDDebugDisabler>();
                    if (disabler == null)
                    {
                        if (GUILayout.Button("DebugDisabler をアタッチして有効化", GUILayout.Height(24)))
                        {
                            ctx.Controller.debugDisabler = ctx.Controller.gameObject.AddComponent<HAP_AUTDDebugDisabler>();
                            EditorUtility.SetDirty(ctx.Controller);
                            ctx.RefreshLinkedObjects();
                        }
                    }
                    else
                    {
                        ctx.Controller.debugDisabler = disabler;
                    }
                }

                if (disabler != null)
                {
                    EditorGUILayout.HelpBox("チェックを入れたデバイスは、あらゆる出力において強制停止（Null）が出力されます。", MessageType.None);
                    EditorGUILayout.Space(2);

                    var devices = Object.FindObjectsByType<AUTD3Device>(FindObjectsSortMode.None).OrderBy(d => d.ID).ToArray();
                    while (disabler.disabledDevices.Count < devices.Length)
                    {
                        disabler.disabledDevices.Add(false);
                    }

                    EditorGUI.BeginChangeCheck();
                    for (int i = 0; i < devices.Length; i++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        bool currentVal = disabler.disabledDevices[i];
                        if (currentVal) GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);

                        disabler.disabledDevices[i] = EditorGUILayout.ToggleLeft($"Disable Device {devices[i].ID}", currentVal);

                        GUI.backgroundColor = Color.white;
                        EditorGUILayout.EndHorizontal();
                    }

                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorUtility.SetDirty(disabler);
                    }
                }
            }

            EditorGUILayout.Space(12);

            // ─── 4. ログ一元管理 (AppLogManager) ───
            EditorGUILayout.LabelField("ログ統合管理 (AppLogger)", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("AppLogManager を選択（ログ出力制御）", GUILayout.Height(28)))
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
}
#endif
