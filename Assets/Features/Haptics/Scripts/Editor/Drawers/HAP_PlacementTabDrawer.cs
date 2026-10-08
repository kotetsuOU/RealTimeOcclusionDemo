#if UNITY_EDITOR
#nullable enable
using UnityEditor;
using UnityEngine;

namespace Features.Haptics.Editor
{
    /// <summary>
    /// HAP_AUTDHapticsController の「Placement / Calibration」タブ描画クラス。
    /// HAP_AUTDTransformLoader と連動し、AUTD3デバイス配置データ（JSON保存/復元）、プレハブ生成、焦点オフセットを一括管理します。
    /// </summary>
    public static class HAP_PlacementTabDrawer
    {
        public static void Draw(HAP_EditorContext ctx)
        {
            EditorGUILayout.LabelField("📐 AUTD3 デバイス配置 & 座標キャリブレーション", EditorStyles.boldLabel);

            var tl = ctx.Controller.transformLoader;
            if (tl == null)
            {
                EditorGUILayout.HelpBox("TransformLoader が設定されていません。以下のボタンからシーン内のローダーを取得または自動アタッチしてください。", MessageType.Warning);
                if (GUILayout.Button("TransformLoader を自動取得 / アタッチ", GUILayout.Height(28)))
                {
                    AutoAssignTransformLoader(ctx);
                }
                return;
            }

            EditorGUILayout.PropertyField(ctx.TransformLoaderProp, new GUIContent("Transform Loader"));

            if (ctx.TransformLoaderSO == null)
            {
                ctx.RefreshLinkedObjects();
                if (ctx.TransformLoaderSO == null) return;
            }

            EditorGUILayout.Space(8);

            // ─── 1. 座標オフセット（キャリブレーション） ───
            EditorGUILayout.LabelField("空間座標微調整 (Calibration Offset)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ctx.TlOffsetProp != null)
                {
                    EditorGUILayout.PropertyField(ctx.TlOffsetProp, new GUIContent("全体焦点オフセット (Offset)"));
                    EditorGUILayout.HelpBox("すべての焦点座標に加算されるオフセット (m) です。デバイス座標系とUnity空間の微調整に使用します。", MessageType.None);
                }
            }

            EditorGUILayout.Space(8);

            // ─── 2. JSON 配置保存 / 復元 ───
            EditorGUILayout.LabelField("デバイス配置データ管理 (JSON)", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ctx.TlConfigFileProp != null)
                {
                    EditorGUILayout.PropertyField(ctx.TlConfigFileProp, new GUIContent("設定ファイル名"));
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("シーン配置を JSON に保存", GUILayout.Height(26)))
                {
                    tl.Save();
                }
                if (GUILayout.Button("JSON からシーンに読み込み", GUILayout.Height(26)))
                {
                    tl.Load();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(8);

            // ─── 3. デバイス自動生成 ───
            EditorGUILayout.LabelField("デバイスオブジェクト自動生成", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (ctx.TlDevicePrefabProp != null) EditorGUILayout.PropertyField(ctx.TlDevicePrefabProp, new GUIContent("デバイスプレハブ"));
                if (ctx.TlDeviceRootProp != null) EditorGUILayout.PropertyField(ctx.TlDeviceRootProp, new GUIContent("配置先親ルート (Root)"));
                if (ctx.TlPrefabCountProp != null) EditorGUILayout.PropertyField(ctx.TlPrefabCountProp, new GUIContent("生成最大数"));

                EditorGUILayout.Space(4);
                if (GUILayout.Button("デバイス GameObject を生成", GUILayout.Height(24)))
                {
                    tl.GeneratePrefabs();
                }
            }
        }

        private static void AutoAssignTransformLoader(HAP_EditorContext ctx)
        {
            var tl = Object.FindAnyObjectByType<HAP_AUTDTransformLoader>();
            if (tl == null)
            {
                tl = ctx.Controller.gameObject.AddComponent<HAP_AUTDTransformLoader>();
            }

            ctx.TransformLoaderProp.objectReferenceValue = tl;
            ctx.SerializedObject.ApplyModifiedProperties();
            ctx.RefreshLinkedObjects();
        }
    }
}
#endif
