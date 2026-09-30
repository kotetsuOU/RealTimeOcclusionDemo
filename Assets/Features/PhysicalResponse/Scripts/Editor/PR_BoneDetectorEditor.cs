#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Features.PhysicalResponse
{
    [CustomEditor(typeof(PR_BoneDetector))]
    public class PR_BoneDetectorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var detector = (PR_BoneDetector)target;

            EditorGUILayout.LabelField("Target Setup", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("virtualObjectManager"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetTransform"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoDetectActiveModel"));
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Detect Active Target & Bones", GUILayout.Height(30)))
            {
                Undo.RecordObject(detector, "Detect Active Bones");
                detector.DetectBones();
                EditorUtility.SetDirty(detector);
            }
            if (GUILayout.Button("Sync To All Components", GUILayout.Height(30)))
            {
                Undo.RecordObject(detector, "Sync To All Components");
                detector.SyncToAllComponents();
                EditorUtility.SetDirty(detector);
            }
            EditorGUILayout.EndHorizontal();

            if (detector.IsBonesValid())
            {
                EditorGUILayout.HelpBox($"ターゲット '{detector.targetTransform.name}' の全足ボーンが正常にバインドされています。", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("有効なボーンがバインドされていません。[Detect Active Target & Bones] を押してください。", MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Detected Foot Bones", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("frontLeftFoot"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("frontRightFoot"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("backLeftFoot"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("backRightFoot"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Detected Body Bones", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("headBone"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("leftEarBone"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rightEarBone"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("tailBone"));

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            var bd = (PR_BoneDetector)target;
            if (bd == null || bd.targetTransform == null) return;

            bd.GetRestFeetWorldPositions(out Vector3 fl, out Vector3 fr, out Vector3 bl, out Vector3 br);

            // 足4点
            Handles.color = Color.yellow;
            DrawHandle(fl, 0.01f, "FL Foot");
            DrawHandle(fr, 0.01f, "FR Foot");
            DrawHandle(bl, 0.01f, "BL Foot");
            DrawHandle(br, 0.01f, "BR Foot");

            // 頭・耳・尻尾
            if (bd.headBone     != null) { Handles.color = Color.magenta; DrawHandle(bd.headBone.position,     0.015f, "Head"); }
            if (bd.leftEarBone  != null) { Handles.color = Color.cyan;    DrawHandle(bd.leftEarBone.position,  0.01f,  "L Ear"); }
            if (bd.rightEarBone != null) { Handles.color = Color.cyan;    DrawHandle(bd.rightEarBone.position, 0.01f,  "R Ear"); }
            if (bd.tailBone     != null) { Handles.color = Color.green;   DrawHandle(bd.tailBone.position,     0.01f,  "Tail"); }
        }

        private static void DrawHandle(Vector3 pos, float radius, string label)
        {
            Handles.SphereHandleCap(0, pos, Quaternion.identity, radius * 2f, EventType.Repaint);
            Handles.Label(pos + Vector3.up * (radius * 1.5f), label);
        }
    }
}
#endif
