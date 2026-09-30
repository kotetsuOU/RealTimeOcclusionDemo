#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Features.PhysicalResponse
{
    [CustomEditor(typeof(PR_LiftController))]
    public class PR_LiftControllerEditor : Editor
    {
        private SerializedProperty boneDetectorProp;
        private SerializedProperty targetTransformProp;
        private SerializedProperty frontLeftFootProp;
        private SerializedProperty frontRightFootProp;
        private SerializedProperty backLeftFootProp;
        private SerializedProperty backRightFootProp;

        private SerializedProperty enableFrontLeftProp;
        private SerializedProperty enableFrontRightProp;
        private SerializedProperty enableBackLeftProp;
        private SerializedProperty enableBackRightProp;

        private SerializedProperty planeMarginProp;
        private SerializedProperty underPlaneDepthThresholdProp;
        private SerializedProperty upperPlaneMarginProp;
        private SerializedProperty syncWithHcdProp;

        private SerializedProperty liftModeProp;
        private SerializedProperty liftSensitivityProp;
        private SerializedProperty maxLiftHeightProp;
        private SerializedProperty minLiftHeightProp;
        private SerializedProperty followHorizontalHandProp;
        private SerializedProperty maxLiftDeltaProp;
        private SerializedProperty maxCentroidJumpProp;

        private SerializedProperty fallbackPointProp;
        private SerializedProperty fallSpeedProp;

        private void OnEnable()
        {
            boneDetectorProp = serializedObject.FindProperty("boneDetector");
            targetTransformProp = serializedObject.FindProperty("targetTransform");
            frontLeftFootProp = serializedObject.FindProperty("frontLeftFoot");
            frontRightFootProp = serializedObject.FindProperty("frontRightFoot");
            backLeftFootProp = serializedObject.FindProperty("backLeftFoot");
            backRightFootProp = serializedObject.FindProperty("backRightFoot");

            enableFrontLeftProp = serializedObject.FindProperty("enableFrontLeft");
            enableFrontRightProp = serializedObject.FindProperty("enableFrontRight");
            enableBackLeftProp = serializedObject.FindProperty("enableBackLeft");
            enableBackRightProp = serializedObject.FindProperty("enableBackRight");

            planeMarginProp = serializedObject.FindProperty("planeMargin");
            underPlaneDepthThresholdProp = serializedObject.FindProperty("underPlaneDepthThreshold");
            upperPlaneMarginProp = serializedObject.FindProperty("upperPlaneMargin");
            syncWithHcdProp = serializedObject.FindProperty("syncWithHcd");

            liftModeProp = serializedObject.FindProperty("liftMode");
            liftSensitivityProp = serializedObject.FindProperty("liftSensitivity");
            maxLiftHeightProp = serializedObject.FindProperty("maxLiftHeight");
            minLiftHeightProp = serializedObject.FindProperty("minLiftHeight");
            followHorizontalHandProp = serializedObject.FindProperty("followHorizontalHand");
            maxLiftDeltaProp = serializedObject.FindProperty("maxLiftDelta");
            maxCentroidJumpProp = serializedObject.FindProperty("maxCentroidJump");

            fallbackPointProp = serializedObject.FindProperty("fallbackPoint");
            fallSpeedProp = serializedObject.FindProperty("fallSpeed");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var controller = (PR_LiftController)target;

            EditorGUILayout.LabelField("Bone Detector & Target Integration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(boneDetectorProp);

            bool isSynced = controller.boneDetector != null || FindFirstObjectByType<PR_BoneDetector>() != null;

            if (isSynced)
            {
                string detectorName = controller.boneDetector != null ? controller.boneDetector.name : "PR_BoneDetector";
                EditorGUILayout.HelpBox($"🔒 各足ボーン・ターゲットTransform・基準静止ポーズは {detectorName} により一元同期されています。\nボーンの検出やポーズ記録は {detectorName} のInspectorから行ってください。", MessageType.Info);
            }
            else
            {
                EditorGUILayout.PropertyField(targetTransformProp);
                EditorGUILayout.Space();

                EditorGUILayout.LabelField("Foot Bones (Manual Fallback)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(frontLeftFootProp);
                EditorGUILayout.PropertyField(frontRightFootProp);
                EditorGUILayout.PropertyField(backLeftFootProp);
                EditorGUILayout.PropertyField(backRightFootProp);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Auto Detect Target & Bones"))
                {
                    Undo.RecordObject(controller, "Auto Detect Bones");
                    controller.targetTransform = null;
                    controller.frontLeftFoot = null;
                    controller.frontRightFoot = null;
                    controller.backLeftFoot = null;
                    controller.backRightFoot = null;
                    controller.AutoDetectBones();
                    controller.CaptureRestPose();
                    EditorUtility.SetDirty(controller);
                }
                if (GUILayout.Button("Capture Rest Pose"))
                {
                    Undo.RecordObject(controller, "Capture Rest Pose");
                    controller.CaptureRestPose();
                    EditorUtility.SetDirty(controller);
                }
                EditorGUILayout.EndHorizontal();

                if (controller.HasRestPose)
                {
                    EditorGUILayout.HelpBox("基準足ポーズ（アニメーション非依存）が記録されています。", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("基準足ポーズが未記録です。静止姿勢の状態で [Capture Rest Pose] を押してください。", MessageType.Warning);
                }
            }
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Foot Toggles", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(enableFrontLeftProp);
            EditorGUILayout.PropertyField(enableFrontRightProp);
            EditorGUILayout.PropertyField(enableBackLeftProp);
            EditorGUILayout.PropertyField(enableBackRightProp);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Foot Plane & Area Filtering", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(planeMarginProp);
            EditorGUILayout.PropertyField(underPlaneDepthThresholdProp);
            EditorGUILayout.PropertyField(upperPlaneMarginProp);
            EditorGUILayout.PropertyField(syncWithHcdProp);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Lift Calculation & Movement Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(liftModeProp);
            EditorGUILayout.PropertyField(liftSensitivityProp);

            if (liftModeProp.enumValueIndex == (int)LiftCalculationMode.InitialPositionPlusLift)
            {
                EditorGUILayout.PropertyField(minLiftHeightProp);
                EditorGUILayout.PropertyField(maxLiftHeightProp);
                EditorGUILayout.PropertyField(followHorizontalHandProp);
                EditorGUILayout.HelpBox("💡 初期位置 + 手の持ち上げ変位モード:\n手が静止している場合、Jump等のアニメーションやフレーム間のノイズでキツネが下に沈まず、安定して高さが維持されます。", MessageType.None);
            }
            else
            {
                EditorGUILayout.PropertyField(maxLiftDeltaProp);
            }

            EditorGUILayout.PropertyField(maxCentroidJumpProp);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Fall Behavior Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(fallbackPointProp);
            EditorGUILayout.PropertyField(fallSpeedProp);

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// 足平面・バウンディングボリューム・法線・接触重心を Scene ビュー上に描画します。
        /// </summary>
        private void OnSceneGUI()
        {
            var controller = (PR_LiftController)target;
            if (controller == null || controller.targetTransform == null) return;

            // BoneDetector 同期
            if (!Application.isPlaying)
            {
                var bd = controller.boneDetector != null
                    ? controller.boneDetector
                    : FindFirstObjectByType<PR_BoneDetector>();

                if (bd != null && bd.targetTransform != null)
                {
                    controller.targetTransform  = bd.targetTransform;
                    controller.frontLeftFoot    = bd.frontLeftFoot;
                    controller.frontRightFoot   = bd.frontRightFoot;
                    controller.backLeftFoot     = bd.backLeftFoot;
                    controller.backRightFoot    = bd.backRightFoot;
                }
                else if (controller.targetTransform == null || !controller.targetTransform.gameObject.activeInHierarchy
                    || controller.frontLeftFoot == null || !controller.frontLeftFoot.IsChildOf(controller.targetTransform))
                {
                    controller.AutoDetectBones();
                }
            }

            controller.GetRestFeetWorldPositions(
                out Vector3 fl, out Vector3 fr, out Vector3 bl, out Vector3 br);

            // SerializedProperty から値を取得
            serializedObject.Update();
            var planeProp      = serializedObject.FindProperty("planeMargin");
            var depthProp      = serializedObject.FindProperty("underPlaneDepthThreshold");
            var upperProp      = serializedObject.FindProperty("upperPlaneMargin");
            bool hasRestPose   = serializedObject.FindProperty("hasRestPose").boolValue;
            var locFL = ToVec(serializedObject.FindProperty("localFrontLeft"));
            var locFR = ToVec(serializedObject.FindProperty("localFrontRight"));
            var locBL = ToVec(serializedObject.FindProperty("localBackLeft"));
            var locBR = ToVec(serializedObject.FindProperty("localBackRight"));

            float planeMargin = planeProp.floatValue;
            float underDepth  = depthProp.floatValue;
            float upperMargin = upperProp.floatValue;

            var plane = PR_LiftPlaneCalculator.CalculatePlane(
                controller.targetTransform, fl, fr, bl, br,
                controller.enableFrontLeft, controller.enableFrontRight,
                controller.enableBackLeft, controller.enableBackRight,
                planeMargin, hasRestPose, locFL, locFR, locBL, locBR
            );
            if (!plane.IsValid) return;

            // 足4点
            Handles.color = hasRestPose ? Color.yellow : Color.gray;
            Handles.SphereHandleCap(0, fl, Quaternion.identity, 0.016f, EventType.Repaint);
            Handles.SphereHandleCap(0, fr, Quaternion.identity, 0.016f, EventType.Repaint);
            Handles.SphereHandleCap(0, bl, Quaternion.identity, 0.016f, EventType.Repaint);
            Handles.SphereHandleCap(0, br, Quaternion.identity, 0.016f, EventType.Repaint);

            // 足4点の輪郭
            Handles.color = Color.yellow;
            Handles.DrawLine(fl, fr);
            Handles.DrawLine(fr, br);
            Handles.DrawLine(br, bl);
            Handles.DrawLine(bl, fl);

            // 法線
            Handles.color = Color.cyan;
            Handles.DrawLine(plane.Origin, plane.Origin + plane.Normal * 0.1f);

            // バウンディングボリューム（ローカル空間）
            Vector3 boxCenterLocal = new Vector3(
                (plane.MinX + plane.MaxX) * 0.5f,
                plane.RefY + (-underDepth + upperMargin) * 0.5f,
                (plane.MinZ + plane.MaxZ) * 0.5f
            );
            Vector3 boxSizeLocal = new Vector3(
                plane.MaxX - plane.MinX,
                underDepth + upperMargin,
                plane.MaxZ - plane.MinZ
            );

            bool isContacting = controller.IsContactingGizmo;
            Handles.color = isContacting
                ? new Color(0f, 1f, 0f, 0.8f)
                : new Color(0f, 0.8f, 1f, 0.4f);

            Matrix4x4 prev = Handles.matrix;
            Handles.matrix = controller.targetTransform.localToWorldMatrix;
            DrawWireCubeHandles(boxCenterLocal, boxSizeLocal);
            Handles.matrix = prev;

            // 接触重心
            if (isContacting)
            {
                Handles.color = Color.magenta;
                Handles.SphereHandleCap(0, controller.PreviousCentroidGizmo, Quaternion.identity, 0.03f, EventType.Repaint);
            }
        }

        private static Vector3 ToVec(SerializedProperty p) =>
            new Vector3(p.FindPropertyRelative("x").floatValue,
                        p.FindPropertyRelative("y").floatValue,
                        p.FindPropertyRelative("z").floatValue);

        private static void DrawWireCubeHandles(Vector3 center, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3[] verts = new Vector3[]
            {
                center + new Vector3(-h.x, -h.y, -h.z),
                center + new Vector3( h.x, -h.y, -h.z),
                center + new Vector3( h.x, -h.y,  h.z),
                center + new Vector3(-h.x, -h.y,  h.z),
                center + new Vector3(-h.x,  h.y, -h.z),
                center + new Vector3( h.x,  h.y, -h.z),
                center + new Vector3( h.x,  h.y,  h.z),
                center + new Vector3(-h.x,  h.y,  h.z),
            };
            for (int i = 0; i < 4; i++)
            {
                Handles.DrawLine(verts[i], verts[(i + 1) % 4]);
                Handles.DrawLine(verts[i + 4], verts[(i + 1) % 4 + 4]);
                Handles.DrawLine(verts[i], verts[i + 4]);
            }
        }
    }
}
#endif

