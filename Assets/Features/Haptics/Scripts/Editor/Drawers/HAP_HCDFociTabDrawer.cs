#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Features.Haptics.Editor
{
    /// <summary>
    /// HAP_AUTDHapticsController の「HCD Foci」タブ描画クラス。
    /// 手指接触領域に対する焦点生成方式（Simplified / Precision）および各ソース（重心・楕円・ランダム）を一元管理します。
    /// </summary>
    public static class HAP_HCDFociTabDrawer
    {
        public static void Draw(HAP_EditorContext ctx)
        {
            EditorGUILayout.LabelField("🎯 HCD 接触焦点生成設定 (Foci Generation)", EditorStyles.boldLabel);

            if (ctx.Controller.hcdFociSettings == null)
            {
                EditorGUILayout.HelpBox("HAP_HCDFociSettings が設定されていません。以下のボタンから自動取得またはアタッチしてください。", MessageType.Warning);
                if (GUILayout.Button("HAP_HCDFociSettings を自動取得 / アタッチ", GUILayout.Height(28)))
                {
                    AutoAssignFociSettings(ctx);
                }
                return;
            }

            EditorGUILayout.PropertyField(ctx.HcdFociSettingsProp, new GUIContent("HCD Foci Settings"));

            if (ctx.FociSettingsSO == null)
            {
                ctx.RefreshLinkedObjects();
                if (ctx.FociSettingsSO == null) return;
            }

            EditorGUILayout.Space(8);

            // ─── 1. 生成モード ───
            EditorGUILayout.LabelField("焦点生成モード (Generation Mode)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ctx.FociGenModeProp != null) EditorGUILayout.PropertyField(ctx.FociGenModeProp, new GUIContent("生成モード"));

                if (ctx.FociGenModeProp != null)
                {
                    var mode = (HapticsGenerationMode)ctx.FociGenModeProp.enumValueIndex;
                    if (mode == HapticsGenerationMode.Simplified)
                    {
                        EditorGUILayout.HelpBox("Simplified モード: 1クラスタあたり1焦点の単純・高速出力です（CPU負荷最小）。", MessageType.None);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("Precision モード: 楕円軌道やランダムサンプリングなどのリッチな触覚刺激（なぞる感触・テクスチャ感）を生成します。", MessageType.None);
                    }
                }
            }

            EditorGUILayout.Space(8);

            // ─── 2. 各種ソース設定 ───
            EditorGUILayout.LabelField("接触ソース設定 (Precision Sources)", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ctx.FociCentroidSourceProp != null)
                {
                    EditorGUILayout.PropertyField(ctx.FociCentroidSourceProp, new GUIContent("1. 重心ソース (Centroid)", "接触クラスタ重心の焦点"), true);
                }
            }

            EditorGUILayout.Space(4);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ctx.FociEllipseSourceProp != null)
                {
                    EditorGUILayout.PropertyField(ctx.FociEllipseSourceProp, new GUIContent("2. 楕円ソース (Ellipse)", "接触面の広がりと主軸に沿ったSTM焦点"), true);
                }
            }

            EditorGUILayout.Space(4);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ctx.FociRandomSourceProp != null)
                {
                    EditorGUILayout.PropertyField(ctx.FociRandomSourceProp, new GUIContent("3. ランダムソース (Random)", "接触面内部を高速移動するザラザラ感STM焦点"), true);
                }
            }
        }

        private static void AutoAssignFociSettings(HAP_EditorContext ctx)
        {
            var foci = ctx.Controller.GetComponent<HAP_HCDFociSettings>();
            if (foci == null)
            {
                foci = Object.FindAnyObjectByType<HAP_HCDFociSettings>();
                if (foci == null)
                {
                    foci = ctx.Controller.gameObject.AddComponent<HAP_HCDFociSettings>();
                }
            }

            ctx.HcdFociSettingsProp.objectReferenceValue = foci;
            ctx.SerializedObject.ApplyModifiedProperties();
            ctx.RefreshLinkedObjects();
        }
    }
}
#endif
