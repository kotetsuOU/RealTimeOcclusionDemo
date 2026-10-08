#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Features.HapticsCollision.Debug;

namespace Features.HapticsCollision.Editor
{
    /// <summary>
    /// HCD_PipelineEditor の「Debug（デバッグ・可視化）」タブの専用描画クラス。
    /// Scene ビュー上の接触 Gizmos 可視化および AppLogManager（ログ一元管理）へのクイックアクセスを提供します。
    /// </summary>
    public static class HCD_DebugTabDrawer
    {
        public static void Draw(HCD_EditorContext ctx)
        {
            EditorGUILayout.LabelField("🔍 デバッグ & 視覚化ツール", EditorStyles.boldLabel);

            var visualizer = ctx.Pipeline.GetComponent<HCD_DebugVisualizer>();

            if (visualizer != null)
            {
                SerializedObject visualizerSO = new SerializedObject(visualizer);
                SerializedProperty gizmoProp = visualizerSO.FindProperty("showGizmos");
                if (gizmoProp != null)
                {
                    visualizerSO.Update();
                    EditorGUILayout.PropertyField(gizmoProp, new GUIContent("Scene Gizmos 可視化"));
                    visualizerSO.ApplyModifiedProperties();
                }

                EditorGUILayout.HelpBox("Scene ビュー上で接触重心（緑球）、点群実測点（青線）、メッシュ投影点（赤線）、法線（黄線）が可視化されます。", MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("HCD_DebugVisualizer がアタッチされていません（実行時に自動アタッチされます）。", MessageType.Info);
            }

            EditorGUILayout.Space(12);

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
