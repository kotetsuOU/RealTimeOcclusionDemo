#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Features.HapticsCollision.Editor
{
    /// <summary>
    /// HCD_PipelineEditor の「Tracking（クラスタ追跡・接触力）」タブの専用描画クラス。
    /// フレーム間マッチング、接触力（Force）計算、スムージング係数を描画します。
    /// 実行時（Play Mode）はリアルタイムの追跡クラスタ状況をモニタリング表示します。
    /// </summary>
    public static class HCD_TrackingTabDrawer
    {
        private static bool _showRuntimeDetails = true;

        public static void Draw(HCD_EditorContext ctx)
        {
            if (ctx.CtProp == null) return;

            EditorGUILayout.LabelField("⏱️ フレーム間クラスタ追跡設定", EditorStyles.boldLabel);

            // マッチング設定
            EditorGUILayout.LabelField("クラスタマッチング", EditorStyles.miniBoldLabel);
            if (ctx.MatchRadiusProp != null) EditorGUILayout.PropertyField(ctx.MatchRadiusProp);
            if (ctx.MaxMissingFramesProp != null) EditorGUILayout.PropertyField(ctx.MaxMissingFramesProp);

            EditorGUILayout.Space(8);

            // 接触力 (Force) & スムージング設定
            EditorGUILayout.LabelField("接触力 (Force) 計算 & スムージング", EditorStyles.boldLabel);
            if (ctx.ForceMinCountProp != null) EditorGUILayout.PropertyField(ctx.ForceMinCountProp);
            if (ctx.ForceMaxCountProp != null) EditorGUILayout.PropertyField(ctx.ForceMaxCountProp);

            EditorGUILayout.Space(4);
            if (ctx.ForceSmoothingFactorProp != null) EditorGUILayout.PropertyField(ctx.ForceSmoothingFactorProp);
            if (ctx.VelocitySmoothingFactorProp != null) EditorGUILayout.PropertyField(ctx.VelocitySmoothingFactorProp);

            // 実行時リアルタイムモニタリング
            if (Application.isPlaying)
            {
                EditorGUILayout.Space(10);
                DrawRuntimeTrackingMonitor(ctx.Pipeline);
            }
        }

        private static void DrawRuntimeTrackingMonitor(HCD_Pipeline pipeline)
        {
            if (pipeline == null) return;

            var clusters = pipeline.GetTrackedClusters();
            int count = clusters?.Count ?? 0;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _showRuntimeDetails = EditorGUILayout.Foldout(_showRuntimeDetails, $"リアルタイム追跡モニタ ({count} 件)", true, EditorStyles.boldLabel);

            if (_showRuntimeDetails && clusters != null && count > 0)
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < count; i++)
                {
                    var c = clusters[i];
                    string status = c.IsAlive ? "● Active" : "○ Missing";
                    string info = $"[ID:{c.Id}] {status} | Contacts:{c.ContactCount} | Force:{c.Force:F2} | Age:{c.Age} frames";
                    EditorGUILayout.LabelField(info, EditorStyles.miniLabel);
                }
                EditorGUI.indentLevel--;
            }
            else if (_showRuntimeDetails && count == 0)
            {
                EditorGUILayout.LabelField("現在追跡中のクラスタはありません（接触なし）。", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }
    }
}
#endif
