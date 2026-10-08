#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Features.HapticsCollision.Processors;

namespace Features.HapticsCollision.Editor
{
    /// <summary>
    /// HCD_PipelineEditor の「Distance（距離・対象設定）」タブの専用描画クラス。
    /// 対象オブジェクト設定、自動同期検知、および距離判定しきい値を描画します。
    /// </summary>
    public static class HCD_DistanceTabDrawer
    {
        private static bool _showLockedTargets = false;

        // Reflection キャッシュ
        private static System.Type s_vomType;
        private static System.Type s_animCtrlType;
        private static bool s_typesResolved = false;

        public static void Draw(HCD_EditorContext ctx)
        {
            if (ctx.DpProp == null) return;

            // ─── 1. 対象オブジェクト & 検出モード ───
            EditorGUILayout.LabelField("🎯 接触判定対象の設定", EditorStyles.boldLabel);

            bool isAutoLinked = CheckAutoLinkStatus(out string managerName);

            if (isAutoLinked)
            {
                EditorGUILayout.HelpBox($"🔒 {managerName} により対象オブジェクトが自動管理（同期）されています。", MessageType.Info);
                GUI.enabled = false;
                if (ctx.DetModeProp != null) EditorGUILayout.PropertyField(ctx.DetModeProp);
                GUI.enabled = true;

                _showLockedTargets = EditorGUILayout.Foldout(_showLockedTargets, "同期中の対象オブジェクトを表示", true);
                if (_showLockedTargets)
                {
                    EditorGUI.indentLevel++;
                    GUI.enabled = false;
                    DrawTargetProperties(ctx);
                    GUI.enabled = true;
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                if (ctx.DetModeProp != null) EditorGUILayout.PropertyField(ctx.DetModeProp);
                DrawTargetProperties(ctx);
            }

            EditorGUILayout.Space(8);

            // ─── 2. 距離判定モード & しきい値 ───
            EditorGUILayout.LabelField("📏 判定距離パラメータ (Distance Thresholds)", EditorStyles.boldLabel);

            if (ctx.DistModeProp != null) EditorGUILayout.PropertyField(ctx.DistModeProp);

            int distMode = ctx.DistModeProp != null ? ctx.DistModeProp.enumValueIndex : 0;
            if (distMode == (int)HCD_DistanceProcessor.DistanceMode.ViewDirection)
            {
                if (ctx.ViewCamProp != null) EditorGUILayout.PropertyField(ctx.ViewCamProp);
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("視線方向モードしきい値", EditorStyles.miniBoldLabel);
                if (ctx.VisSurfProp != null) EditorGUILayout.PropertyField(ctx.VisSurfProp);
                if (ctx.VisBackProp != null) EditorGUILayout.PropertyField(ctx.VisBackProp);
                if (ctx.OccSurfProp != null) EditorGUILayout.PropertyField(ctx.OccSurfProp);
                if (ctx.OccBackProp != null) EditorGUILayout.PropertyField(ctx.OccBackProp);
            }
            else
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("メッシュ表面モードしきい値", EditorStyles.miniBoldLabel);
                if (ctx.MeshSurfProp != null) EditorGUILayout.PropertyField(ctx.MeshSurfProp);
                if (ctx.MeshBackProp != null) EditorGUILayout.PropertyField(ctx.MeshBackProp);
            }
        }

        private static void DrawTargetProperties(HCD_EditorContext ctx)
        {
            int detMode = ctx.DetModeProp != null ? ctx.DetModeProp.enumValueIndex : 0;

            if (detMode == (int)HCD_DistanceProcessor.DetectionMode.TransformOnly)
            {
                if (ctx.TargetObjProp != null) EditorGUILayout.PropertyField(ctx.TargetObjProp);
                if (ctx.TargetTransformsProp != null) EditorGUILayout.PropertyField(ctx.TargetTransformsProp, true);
            }
            else if (detMode == (int)HCD_DistanceProcessor.DetectionMode.SkinnedMeshRenderer)
            {
                if (ctx.TargetSkinnedProp != null) EditorGUILayout.PropertyField(ctx.TargetSkinnedProp, true);
            }
            else if (detMode == (int)HCD_DistanceProcessor.DetectionMode.MeshFilter)
            {
                if (ctx.TargetMeshFilterProp != null) EditorGUILayout.PropertyField(ctx.TargetMeshFilterProp, true);
            }
            else if (detMode == (int)HCD_DistanceProcessor.DetectionMode.FootPlane)
            {
                EditorGUILayout.LabelField("FootPlane 設定", EditorStyles.miniBoldLabel);
                if (ctx.FootPlaneTargetProp != null) EditorGUILayout.PropertyField(ctx.FootPlaneTargetProp);
                if (ctx.FootPlaneBoundsMinProp != null) EditorGUILayout.PropertyField(ctx.FootPlaneBoundsMinProp);
                if (ctx.FootPlaneBoundsMaxProp != null) EditorGUILayout.PropertyField(ctx.FootPlaneBoundsMaxProp);
                if (ctx.FootPlaneRefYProp != null) EditorGUILayout.PropertyField(ctx.FootPlaneRefYProp);
                if (ctx.FootPlaneDepthThresholdProp != null) EditorGUILayout.PropertyField(ctx.FootPlaneDepthThresholdProp);
                if (ctx.FootPlaneUpperMarginProp != null) EditorGUILayout.PropertyField(ctx.FootPlaneUpperMarginProp);
                if (ctx.FootPlaneNormalProp != null) EditorGUILayout.PropertyField(ctx.FootPlaneNormalProp);
            }
        }

        private static void EnsureTypesResolved()
        {
            if (s_typesResolved) return;

            s_vomType = System.Type.GetType("Features.Animation.PR_VirtualObjectManager, Assembly-CSharp") 
                     ?? System.Type.GetType("PR_VirtualObjectManager, Assembly-CSharp");
            s_animCtrlType = System.Type.GetType("Features.Animation.PR_AnimationController, Assembly-CSharp") 
                          ?? System.Type.GetType("PR_AnimationController, Assembly-CSharp");

            s_typesResolved = true;
        }

        private static bool CheckAutoLinkStatus(out string managerName)
        {
            EnsureTypesResolved();
            managerName = null;

            if (s_vomType != null)
            {
                var vom = Object.FindFirstObjectByType(s_vomType);
                if (vom != null)
                {
                    var vomSO = new SerializedObject(vom);
                    var syncProp = vomSO.FindProperty("syncWithHcd");
                    if (syncProp != null && syncProp.boolValue)
                    {
                        managerName = "PR_VirtualObjectManager";
                        return true;
                    }
                }
            }

            if (s_animCtrlType != null)
            {
                var animCtrl = Object.FindFirstObjectByType(s_animCtrlType);
                if (animCtrl != null)
                {
                    var animCtrlSO = new SerializedObject(animCtrl);
                    var autoUpdateProp = animCtrlSO.FindProperty("autoUpdateCollisionTarget");
                    if (autoUpdateProp != null && autoUpdateProp.boolValue)
                    {
                        managerName = "PR_AnimationController";
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
#endif
