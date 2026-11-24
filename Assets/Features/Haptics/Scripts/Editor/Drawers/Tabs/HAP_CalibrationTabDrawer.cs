#if UNITY_EDITOR
#nullable enable
using UnityEditor;
using UnityEngine;

namespace Features.Haptics.Editor
{
    /// <summary>
    /// HAP_AUTDHapticsController の「Calibration」タブ描画クラス。
    /// 単焦点テスト照射の ON/OFF、ターゲット位置・微調整・出力/変調オーバーライド、および空間キャリブレーション (Offset 補正) を提供します。
    /// </summary>
    public static class HAP_CalibrationTabDrawer
    {
        private const string PrefFoldNudge = "HAP_CalibrationTab_FoldNudge";
        private const string PrefFoldCalibration = "HAP_CalibrationTab_FoldCalibration";

        /// <summary>
        /// Calibration タブの Inspector UI を描画します。
        /// </summary>
        public static void Draw(HAP_EditorContext ctx)
        {
            if (ctx.DbgEnableProp == null) return;

            var ctrl = ctx.Controller;
            var cfg = ctrl.debugFocusConfig;
            bool isEnabled = ctx.DbgEnableProp.boolValue;

            EditorGUILayout.LabelField("🎯 単焦点テスト照射 & 空間キャリブレーション", EditorStyles.boldLabel);
            DrawToggleHeader(ctx, isEnabled);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPositionSection(ctx, cfg);
                EditorGUILayout.Space(6);
                DrawOutputAndModulationSection(ctx, ctrl, cfg);
                EditorGUILayout.Space(6);
                DrawCalibrationSection(ctx, ctrl, cfg);
            }

            if (Application.isPlaying && HAP_DebugFocusPlayStatePersistence.PreservePlayChanges)
            {
                HAP_DebugFocusPlayStatePersistence.SavePlayState(ctrl);
            }
        }

        /// <summary>
        /// Scene ビュー上に単焦点の位置操作ハンドルを描画します。
        /// </summary>
        public static void DrawSceneGUI(HAP_EditorContext ctx)
        {
            if (ctx == null || ctx.Controller == null) return;
            HAP_DebugFocusSceneActions.DrawSceneMoveHandle(ctx);
        }

        private static void DrawToggleHeader(HAP_EditorContext ctx, bool isEnabled)
        {
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = isEnabled
                ? new Color(1f, 0.55f, 0.35f)
                : new Color(0.75f, 0.92f, 1f);

            string toggleLabel = isEnabled
                ? "🎯 単焦点テスト照射中（クリックで停止）"
                : "🎯 単焦点テスト照射を開始（通常出力を上書き）";

            if (GUILayout.Button(toggleLabel, GUILayout.Height(28)))
            {
                ctx.DbgEnableProp!.boolValue = !isEnabled;
            }
            GUI.backgroundColor = prevBg;

            if (isEnabled)
            {
                EditorGUILayout.HelpBox(
                    "単焦点 (Naive) オーバーライド中: 通常の AutoHCD / ObjectTarget 出力を一時停止しています。",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(4);
        }

        private static void DrawPositionSection(HAP_EditorContext ctx, Config.HAP_DebugFocusConfig cfg)
        {
            EditorGUILayout.LabelField("焦点位置", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (ctx.DbgTargetTransformProp != null)
                {
                    EditorGUILayout.PropertyField(ctx.DbgTargetTransformProp, new GUIContent("Target Transform (任意)"));
                }

                if (GUILayout.Button(cfg.targetTransform != null ? "選択" : "生成", GUILayout.Width(52)))
                {
                    HAP_DebugFocusSceneActions.CreateOrSelectTargetObject(ctx);
                }
            }

            if (cfg.targetTransform == null)
            {
                if (ctx.DbgFocusPositionProp != null)
                {
                    EditorGUILayout.PropertyField(ctx.DbgFocusPositionProp, new GUIContent("Focus Position (m)"));
                }
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Vector3Field("Target ワールド座標 (m)", cfg.targetTransform.position);
                }
            }

            bool foldNudge = SessionState.GetBool(PrefFoldNudge, false);
            foldNudge = EditorGUILayout.Foldout(foldNudge, "微調整 (±1mm / ±10mm)・クイック配置", true);
            SessionState.SetBool(PrefFoldNudge, foldNudge);

            if (foldNudge)
            {
                EditorGUI.indentLevel++;
                if (cfg.targetTransform == null)
                {
                    DrawPositionNudgeButtons(ctx);
                }
                else
                {
                    DrawTransformNudgeButtons(cfg.targetTransform);
                }

                EditorGUILayout.Space(2);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("📍 アレイ前方 20cm", GUILayout.Height(22)))
                    {
                        HAP_DebugFocusSceneActions.PlaceAtArrayFront(ctx);
                    }
                    if (GUILayout.Button("🎯 アクティブ対象位置", GUILayout.Height(22)))
                    {
                        HAP_DebugFocusSceneActions.PlaceAtActiveTarget(ctx);
                    }
                }
                EditorGUI.indentLevel--;
            }
        }

        private static void DrawOutputAndModulationSection(
            HAP_EditorContext ctx,
            HAP_AUTDHapticsController ctrl,
            Config.HAP_DebugFocusConfig cfg)
        {
            EditorGUILayout.LabelField("出力・変調オーバーライド", EditorStyles.boldLabel);

            if (ctx.DbgMaximizeDutyProp != null)
            {
                EditorGUILayout.PropertyField(ctx.DbgMaximizeDutyProp, new GUIContent("最大出力 (Duty 100%) で駆動"));
            }
            if (!cfg.maximizeDuty && ctx.DbgIntensityProp != null)
            {
                EditorGUILayout.PropertyField(ctx.DbgIntensityProp, new GUIContent("目標音圧 (Pa)"));
            }

            if (ctx.DbgForceSineProp != null)
            {
                EditorGUILayout.PropertyField(ctx.DbgForceSineProp, new GUIContent("照射中は Sine 変調 (AM) を強制"));
            }
            if (cfg.forceSineModulation && ctx.DbgSineFreqProp != null)
            {
                EditorGUILayout.PropertyField(ctx.DbgSineFreqProp, new GUIContent("テスト変調周波数 (Hz)"));
            }
            else if (!cfg.forceSineModulation && ctrl.hardwareController != null && ctrl.hardwareController.modulationMode == ModulationMode.Static)
            {
                EditorGUILayout.HelpBox(
                    "Hardware の変調が Static (無変調 DC) のため、静止単焦点は極めて弱く感じます。Sine 変調の併用を推奨します。",
                    MessageType.Warning);
            }
        }

        private static void DrawCalibrationSection(
            HAP_EditorContext ctx,
            HAP_AUTDHapticsController ctrl,
            Config.HAP_DebugFocusConfig cfg)
        {
            bool foldCal = SessionState.GetBool(PrefFoldCalibration, true);
            foldCal = EditorGUILayout.Foldout(foldCal, "空間キャリブレーション (Offset 補正)", true);
            SessionState.SetBool(PrefFoldCalibration, foldCal);

            if (!foldCal) return;

            EditorGUI.indentLevel++;

            if (ctx.DbgApplyOffsetProp != null)
            {
                EditorGUILayout.PropertyField(ctx.DbgApplyOffsetProp, new GUIContent("全体オフセットを適用"));
            }

            if (ctx.TransformLoaderSO != null && ctx.TlOffsetProp != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(ctx.TlOffsetProp, new GUIContent("全体オフセット (m)"));
                    if (GUILayout.Button("0", GUILayout.Width(26)))
                    {
                        ctx.TlOffsetProp.vector3Value = Vector3.zero;
                    }
                }
            }

            Vector3 effectivePos = cfg.GetEffectiveFocusPosition(ctrl.offset);
            EditorGUILayout.LabelField(
                $"実送信座標: ({effectivePos.x:F4}, {effectivePos.y:F4}, {effectivePos.z:F4}) m",
                EditorStyles.miniLabel);

            if (ctx.DbgReferenceTargetProp != null)
            {
                EditorGUILayout.PropertyField(ctx.DbgReferenceTargetProp, new GUIContent("正解位置 Transform"));
            }

            using (new EditorGUI.DisabledScope(cfg.referenceTrueTarget == null || ctrl.transformLoader == null))
            {
                if (GUILayout.Button("📐 知覚位置と正解位置の差分から Offset を自動補正", GUILayout.Height(24)))
                {
                    HAP_DebugFocusSceneActions.ApplyDifferenceCalibration(ctx);
                }
            }

            bool preserve = HAP_DebugFocusPlayStatePersistence.PreservePlayChanges;
            bool newPreserve = EditorGUILayout.ToggleLeft("Playモード中の座標・Offset変更を停止後も保持", preserve);
            if (newPreserve != preserve)
            {
                HAP_DebugFocusPlayStatePersistence.PreservePlayChanges = newPreserve;
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawPositionNudgeButtons(HAP_EditorContext ctx)
        {
            if (ctx.DbgFocusPositionProp == null) return;
            Vector3 p = ctx.DbgFocusPositionProp.vector3Value;
            if (DrawAxisNudgeGrid(ref p))
            {
                ctx.DbgFocusPositionProp.vector3Value = p;
            }
        }

        private static void DrawTransformNudgeButtons(Transform target)
        {
            Vector3 p = target.position;
            if (DrawAxisNudgeGrid(ref p))
            {
                Undo.RecordObject(target, "Nudge AUTD Debug Focus Target");
                target.position = p;
                EditorUtility.SetDirty(target);
            }
        }

        private static bool DrawAxisNudgeGrid(ref Vector3 pos)
        {
            bool changed = false;
            changed |= DrawSingleAxisNudge("X", ref pos.x);
            changed |= DrawSingleAxisNudge("Y", ref pos.y);
            changed |= DrawSingleAxisNudge("Z", ref pos.z);
            return changed;
        }

        private static bool DrawSingleAxisNudge(string axisLabel, ref float value)
        {
            bool changed = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{axisLabel}:", GUILayout.Width(32));
                if (GUILayout.Button("-10mm", EditorStyles.miniButtonLeft)) { value -= 0.010f; changed = true; }
                if (GUILayout.Button("-1mm", EditorStyles.miniButtonMid)) { value -= 0.001f; changed = true; }
                if (GUILayout.Button("+1mm", EditorStyles.miniButtonMid)) { value += 0.001f; changed = true; }
                if (GUILayout.Button("+10mm", EditorStyles.miniButtonRight)) { value += 0.010f; changed = true; }
            }
            return changed;
        }
    }
}
#endif

