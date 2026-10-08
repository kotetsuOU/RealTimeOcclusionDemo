#if UNITY_EDITOR
#nullable enable
using UnityEditor;
using UnityEngine;

namespace Features.Haptics.Editor
{
    /// <summary>
    /// HAP_AUTDHapticsController の「General / Source」タブ描画クラス。
    /// 動作モード（AutoHCD / ObjectTarget / Manual）、オブジェクトターゲット選択、パイプライン連携を管理します。
    /// </summary>
    public static class HAP_GeneralTabDrawer
    {
        public static void Draw(HAP_EditorContext ctx)
        {
            EditorGUILayout.LabelField("⚙️ 動作モード & ターゲットソース", EditorStyles.boldLabel);

            if (ctx.SourceModeProp == null) return;

            EditorGUILayout.PropertyField(ctx.SourceModeProp);
            var sourceMode = (HapticsSourceMode)ctx.SourceModeProp.enumValueIndex;

            EditorGUILayout.Space(6);
            EditorGUI.indentLevel++;

            if (sourceMode == HapticsSourceMode.AutoHCD)
            {
                EditorGUILayout.LabelField("HCD 接触クラスタ連携", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(ctx.HcdPipelineProp, new GUIContent("HCD Pipeline (手・指の接触判定)"));
                if (ctx.HcdPipelineProp.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("HCD_Pipeline が未割り当てです。Awake時にシーンから自動検出されます。", MessageType.Info);
                }

                EditorGUILayout.PropertyField(ctx.HcdFociSettingsProp, new GUIContent("HCD Foci Settings (焦点生成設定)"));
                if (ctx.HcdFociSettingsProp.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("HCD_FociSettings が未割り当てです。「🎯 HCD Foci」タブで設定可能です。", MessageType.None);
                }
            }
            else if (sourceMode == HapticsSourceMode.ObjectTarget)
            {
                EditorGUILayout.LabelField("オブジェクト別コントローラー (CustomController)", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(ctx.ObjectHapticsControllersProp, new GUIContent("登録オブジェクトリスト"), true);

                var controller = ctx.Controller;
                if (controller.objectHapticsControllers != null && controller.objectHapticsControllers.Count > 0)
                {
                    var list = controller.objectHapticsControllers;
                    string[] displayOptions = new string[list.Count];

                    for (int i = 0; i < list.Count; i++)
                    {
                        var ctrl = list[i];
                        displayOptions[i] = ctrl != null ? ctrl.gameObject.name : "(未アサイン)";
                    }

                    int currentIndex = Mathf.Clamp(controller.activeObjectControllerIndex, 0, list.Count - 1);
                    int selectedIndex = EditorGUILayout.Popup("照射先オブジェクト (Active Target)", currentIndex, displayOptions);

                    if (selectedIndex != controller.activeObjectControllerIndex)
                    {
                        controller.SetActiveControllerIndex(selectedIndex);
                        EditorUtility.SetDirty(controller);
                    }
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.PropertyField(ctx.HcdPipelineProp, new GUIContent("HCD Pipeline (オクルージョン判定用・任意)"));
            }
            else if (sourceMode == HapticsSourceMode.Manual)
            {
                EditorGUILayout.HelpBox("Manual Mode: 自動更新照射は無効化されています。外部スクリプトの API 呼び出し（SetFocus, SetFocusStm等）により出力制御します。", MessageType.Info);
            }

            EditorGUI.indentLevel--;

            EditorGUILayout.Space(12);

            // ─── 依存コンポーネント接続状況サマリー ───
            EditorGUILayout.LabelField("依存コンポーネント連携サマリー", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawComponentStatus("Hardware Controller", ctx.HardwareControllerProp.objectReferenceValue != null, "「📡 Hardware」タブで設定");
                DrawComponentStatus("Transform Loader", ctx.TransformLoaderProp.objectReferenceValue != null, "「📐 Placement」タブで設定");
                DrawComponentStatus("HCD Foci Settings", ctx.HcdFociSettingsProp.objectReferenceValue != null, "「🎯 HCD Foci」タブで設定");
            }

            // ─── 子オブジェクト化（二重表示解消）アシスト ───
            var go = ctx.Controller.gameObject;
            bool hasCoexistingComponents = go.GetComponent<HAP_AUTDTransformLoader>() != null ||
                                           go.GetComponent<HAP_AUTDDebugDisabler>() != null ||
                                           go.GetComponent<HAP_AUTDHardwareController>() != null;

            if (hasCoexistingComponents)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox("現在、同一 GameObject に依存コンポーネントがアタッチされているため、Inspector 下部に設定が重複して表示されています。子オブジェクト（AUTD3_Services）に分離することで、親の Inspector を HAP_AUTDHapticsController だけにスッキリ整理できます。", MessageType.Info);
                if (GUILayout.Button("子オブジェクトに分離して Inspector の重複を解消", GUILayout.Height(28)))
                {
                    MigrateToChildObject(ctx);
                }
            }
        }

        private static void MigrateToChildObject(HAP_EditorContext ctx)
        {
            var parentGo = ctx.Controller.gameObject;
            Undo.RegisterFullObjectHierarchyUndo(parentGo, "Migrate HAP Components to Child");

            Transform? childTrans = parentGo.transform.Find("AUTD3_Services");
            GameObject childGo;
            if (childTrans == null)
            {
                childGo = new GameObject("AUTD3_Services");
                Undo.RegisterCreatedObjectUndo(childGo, "Create AUTD3_Services");
                childGo.transform.SetParent(parentGo.transform, false);
            }
            else
            {
                childGo = childTrans.gameObject;
            }

            // Move TransformLoader
            var tl = parentGo.GetComponent<HAP_AUTDTransformLoader>();
            if (tl != null)
            {
                UnityEditorInternal.ComponentUtility.CopyComponent(tl);
                var newTl = childGo.GetComponent<HAP_AUTDTransformLoader>() ?? childGo.AddComponent<HAP_AUTDTransformLoader>();
                UnityEditorInternal.ComponentUtility.PasteComponentValues(newTl);
                ctx.TransformLoaderProp.objectReferenceValue = newTl;
                Undo.DestroyObjectImmediate(tl);
            }

            // Move DebugDisabler
            var disabler = parentGo.GetComponent<HAP_AUTDDebugDisabler>();
            if (disabler != null)
            {
                UnityEditorInternal.ComponentUtility.CopyComponent(disabler);
                var newDisabler = childGo.GetComponent<HAP_AUTDDebugDisabler>() ?? childGo.AddComponent<HAP_AUTDDebugDisabler>();
                UnityEditorInternal.ComponentUtility.PasteComponentValues(newDisabler);
                ctx.Controller.debugDisabler = newDisabler;
                Undo.DestroyObjectImmediate(disabler);
            }

            // Move HardwareController if on same GO
            var hw = parentGo.GetComponent<HAP_AUTDHardwareController>();
            if (hw != null)
            {
                UnityEditorInternal.ComponentUtility.CopyComponent(hw);
                var newHw = childGo.GetComponent<HAP_AUTDHardwareController>() ?? childGo.AddComponent<HAP_AUTDHardwareController>();
                UnityEditorInternal.ComponentUtility.PasteComponentValues(newHw);
                ctx.HardwareControllerProp.objectReferenceValue = newHw;
                Undo.DestroyObjectImmediate(hw);
            }

            ctx.SerializedObject.ApplyModifiedProperties();
            ctx.RefreshLinkedObjects();
            EditorUtility.SetDirty(parentGo);
            EditorUtility.SetDirty(childGo);

            UnityEngine.Debug.Log("[HAP_AUTDHapticsController] 依存コンポーネントを子オブジェクト (AUTD3_Services) に分離しました。");
        }

        private static void DrawComponentStatus(string label, bool isAssigned, string tabHint)
        {
            EditorGUILayout.BeginHorizontal();
            string icon = isAssigned ? "✓" : "⚠️";
            GUIStyle labelStyle = isAssigned ? EditorStyles.label : EditorStyles.boldLabel;
            EditorGUILayout.LabelField($"{icon} {label}", labelStyle, GUILayout.Width(180));
            EditorGUILayout.LabelField(isAssigned ? "連携済み" : $"未アサイン ({tabHint})", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
