#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Features.HapticsCollision.Editor
{
    /// <summary>
    /// HCD_PipelineEditor の「Shaders（コンピュートシェーダー）」タブの専用描画クラス。
    /// 内部で利用する ComputeShader の割り当ておよび自動設定ユーティリティを提供します。
    /// </summary>
    public static class HCD_ShadersTabDrawer
    {
        public static void Draw(HCD_EditorContext ctx)
        {
            EditorGUILayout.LabelField("⚡ 内部コンピュートシェーダー設定", EditorStyles.boldLabel);

            bool distMissing = ctx.DistComputeShaderProp != null && ctx.DistComputeShaderProp.objectReferenceValue == null;
            bool clusterMissing = ctx.ClusterComputeShaderProp != null && ctx.ClusterComputeShaderProp.objectReferenceValue == null;
            bool anyMissing = distMissing || clusterMissing;

            if (anyMissing)
            {
                EditorGUILayout.HelpBox("コンピュートシェーダーが設定されていません。以下のフィールドに割り当てるか、「標準 Compute Shader を自動割り当て」ボタンを押してください。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("すべてのコンピュートシェーダーが正常に設定されています。", MessageType.Info);
            }

            EditorGUILayout.Space(4);

            if (ctx.DistComputeShaderProp != null)
            {
                EditorGUILayout.PropertyField(ctx.DistComputeShaderProp, new GUIContent("Distance Compute Shader"));
            }

            if (ctx.ClusterComputeShaderProp != null)
            {
                EditorGUILayout.PropertyField(ctx.ClusterComputeShaderProp, new GUIContent("Clustering Compute Shader"));
            }

            EditorGUILayout.Space(8);

            if (GUILayout.Button("標準 Compute Shader を自動割り当て", GUILayout.Height(28)))
            {
                AutoAssignComputeShaders(ctx);
            }
        }

        public static bool HasMissingShaders(HCD_EditorContext ctx)
        {
            bool distMissing = ctx.DistComputeShaderProp != null && ctx.DistComputeShaderProp.objectReferenceValue == null;
            bool clusterMissing = ctx.ClusterComputeShaderProp != null && ctx.ClusterComputeShaderProp.objectReferenceValue == null;
            return distMissing || clusterMissing;
        }

        private static void AutoAssignComputeShaders(HCD_EditorContext ctx)
        {
            bool modified = false;

            if (ctx.DistComputeShaderProp != null && ctx.DistComputeShaderProp.objectReferenceValue == null)
            {
                var distShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Features/HapticsCollision/ComputeShaders/HCD_Distance.compute");
                if (distShader != null)
                {
                    ctx.DistComputeShaderProp.objectReferenceValue = distShader;
                    modified = true;
                }
            }

            if (ctx.ClusterComputeShaderProp != null && ctx.ClusterComputeShaderProp.objectReferenceValue == null)
            {
                var clusterShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Features/HapticsCollision/ComputeShaders/HCD_SpatialClustering.compute");
                if (clusterShader != null)
                {
                    ctx.ClusterComputeShaderProp.objectReferenceValue = clusterShader;
                    modified = true;
                }
            }

            if (modified)
            {
                ctx.SerializedObject.ApplyModifiedProperties();
                UnityEngine.Debug.Log("[HCD_PipelineEditor] 標準 Compute Shader を自動割り当てしました。");
            }
        }
    }
}
#endif
