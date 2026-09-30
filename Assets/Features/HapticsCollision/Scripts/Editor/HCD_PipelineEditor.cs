#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HCD_Pipeline))]
public class HCD_PipelineEditor : Editor
{
    private static bool _showDistanceProcessor = true;
    private static bool _showClusteringProcessor = true;
    private static bool _showClusterTracker = true;
    private static bool _showInternalShaders = false;
    private static bool _showLockedTargets = false;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Assembly Definitionの制約によりReflectionでPR_VirtualObjectManagerまたはPR_AnimationControllerを検索
        bool isAutoLinked = false;
        string managerName = "PR_VirtualObjectManager";

        System.Type vomType = System.Type.GetType("Features.Animation.PR_VirtualObjectManager, Assembly-CSharp") 
                           ?? System.Type.GetType("PR_VirtualObjectManager, Assembly-CSharp");
        if (vomType != null)
        {
            Object vom = Object.FindFirstObjectByType(vomType);
            if (vom != null)
            {
                SerializedObject vomSO = new SerializedObject(vom);
                SerializedProperty syncProp = vomSO.FindProperty("syncWithHcd");
                if (syncProp != null && syncProp.boolValue)
                {
                    isAutoLinked = true;
                    managerName = "PR_VirtualObjectManager";
                }
            }
        }

        if (!isAutoLinked)
        {
            System.Type animCtrlType = System.Type.GetType("Features.Animation.PR_AnimationController, Assembly-CSharp") 
                                    ?? System.Type.GetType("PR_AnimationController, Assembly-CSharp");
            if (animCtrlType != null)
            {
                Object animCtrl = Object.FindFirstObjectByType(animCtrlType);
                if (animCtrl != null)
                {
                    SerializedObject animCtrlSO = new SerializedObject(animCtrl);
                    SerializedProperty autoUpdateProp = animCtrlSO.FindProperty("autoUpdateCollisionTarget");
                    if (autoUpdateProp != null && autoUpdateProp.boolValue)
                    {
                        isAutoLinked = true;
                        managerName = "PR_AnimationController";
                    }
                }
            }
        }

        // Script Field
        SerializedProperty scriptProp = serializedObject.FindProperty("m_Script");
        if (scriptProp != null)
        {
            GUI.enabled = false;
            EditorGUILayout.PropertyField(scriptProp);
            GUI.enabled = true;
        }

        EditorGUILayout.Space();

        // --- 1. Distance Processor Settings ---
        SerializedProperty dpProp = serializedObject.FindProperty("distanceProcessor");
        if (dpProp != null)
        {
            _showDistanceProcessor = EditorGUILayout.BeginFoldoutHeaderGroup(_showDistanceProcessor, "Distance Processor Settings");
            if (_showDistanceProcessor)
            {
                EditorGUI.indentLevel++;

                SerializedProperty detModeProp = dpProp.FindPropertyRelative("detectionMode");
                SerializedProperty distModeProp = dpProp.FindPropertyRelative("distanceMode");

                int detMode = detModeProp != null ? detModeProp.enumValueIndex : 0;
                int distMode = distModeProp != null ? distModeProp.enumValueIndex : 0;

                // Detection Mode & Target Settings
                if (isAutoLinked)
                {
                    EditorGUILayout.HelpBox($"🔒 {managerName} の自動同期が有効なため、対象設定は自動管理（ロック）されています。", MessageType.Info);
                    GUI.enabled = false;
                    if (detModeProp != null) EditorGUILayout.PropertyField(detModeProp);
                    GUI.enabled = true;

                    _showLockedTargets = EditorGUILayout.Foldout(_showLockedTargets, "自動同期中の対象オブジェクト一覧を表示", true);
                    if (_showLockedTargets)
                    {
                        EditorGUI.indentLevel++;
                        GUI.enabled = false;
                        DrawTargetProperty(dpProp, detMode);
                        GUI.enabled = true;
                        EditorGUI.indentLevel--;
                    }
                }
                else
                {
                    if (detModeProp != null) EditorGUILayout.PropertyField(detModeProp);
                    DrawTargetProperty(dpProp, detMode);
                }

                EditorGUILayout.Space();

                // Distance Mode Settings
                if (distModeProp != null) EditorGUILayout.PropertyField(distModeProp);

                if (distMode == (int)HCD_DistanceProcessor.DistanceMode.ViewDirection)
                {
                    SerializedProperty viewCamProp = dpProp.FindPropertyRelative("viewCamera");
                    if (viewCamProp != null) EditorGUILayout.PropertyField(viewCamProp);

                    SerializedProperty visSurfProp = dpProp.FindPropertyRelative("visibleSurfaceDistanceThreshold");
                    SerializedProperty visBackProp = dpProp.FindPropertyRelative("visibleBackfaceDistanceThreshold");
                    SerializedProperty occSurfProp = dpProp.FindPropertyRelative("occludedSurfaceDistanceThreshold");
                    SerializedProperty occBackProp = dpProp.FindPropertyRelative("occludedBackfaceDistanceThreshold");

                    if (visSurfProp != null) EditorGUILayout.PropertyField(visSurfProp);
                    if (visBackProp != null) EditorGUILayout.PropertyField(visBackProp);
                    if (occSurfProp != null) EditorGUILayout.PropertyField(occSurfProp);
                    if (occBackProp != null) EditorGUILayout.PropertyField(occBackProp);
                }
                else
                {
                    SerializedProperty meshSurfProp = dpProp.FindPropertyRelative("meshSurfaceDistanceThreshold");
                    SerializedProperty meshBackProp = dpProp.FindPropertyRelative("meshBackfaceDistanceThreshold");
                    if (meshSurfProp != null) EditorGUILayout.PropertyField(meshSurfProp);
                    if (meshBackProp != null) EditorGUILayout.PropertyField(meshBackProp);
                }

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        EditorGUILayout.Space();

        // --- 2. Spatial Clustering Processor Settings ---
        SerializedProperty scpProp = serializedObject.FindProperty("clusteringProcessor");
        if (scpProp != null)
        {
            _showClusteringProcessor = EditorGUILayout.BeginFoldoutHeaderGroup(_showClusteringProcessor, "Spatial Clustering Settings");
            if (_showClusteringProcessor)
            {
                EditorGUI.indentLevel++;
                SerializedProperty maxClustersProp = scpProp.FindPropertyRelative("maxClusters");
                SerializedProperty cellSizeProp = scpProp.FindPropertyRelative("cellSize");
                SerializedProperty aggModeProp = scpProp.FindPropertyRelative("aggregationMode");
                SerializedProperty posSourceProp = scpProp.FindPropertyRelative("positionSource");
                SerializedProperty distPowerProp = scpProp.FindPropertyRelative("distanceWeightPower");
                SerializedProperty precisionModeProp = scpProp.FindPropertyRelative("precisionMode");

                if (maxClustersProp != null) EditorGUILayout.PropertyField(maxClustersProp);
                if (cellSizeProp != null) EditorGUILayout.PropertyField(cellSizeProp);

                EditorGUILayout.Space(4);
                if (aggModeProp != null) EditorGUILayout.PropertyField(aggModeProp);
                if (posSourceProp != null) EditorGUILayout.PropertyField(posSourceProp);

                if (aggModeProp != null && aggModeProp.enumValueIndex == (int)ClusterAggregationMode.DistanceWeightedCentroid)
                {
                    if (distPowerProp != null) EditorGUILayout.PropertyField(distPowerProp);
                }

                EditorGUILayout.Space(4);
                if (precisionModeProp != null) EditorGUILayout.PropertyField(precisionModeProp);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        EditorGUILayout.Space();

        // --- 3. Cluster Tracker Settings ---
        SerializedProperty ctProp = serializedObject.FindProperty("clusterTracker");
        if (ctProp != null)
        {
            _showClusterTracker = EditorGUILayout.BeginFoldoutHeaderGroup(_showClusterTracker, "Cluster Tracker Settings");
            if (_showClusterTracker)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(ctProp, true);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        EditorGUILayout.Space();

        // --- 4. Debug Settings ---
        SerializedProperty gizmoProp = serializedObject.FindProperty("showDebugGizmos");
        if (gizmoProp != null)
        {
            EditorGUILayout.PropertyField(gizmoProp);
        }

        EditorGUILayout.Space();

        // --- 5. Internal Compute Shaders ---
        SerializedProperty distComputeShaderProp = dpProp != null ? dpProp.FindPropertyRelative("collisionComputeShader") : null;
        SerializedProperty clusterComputeShaderProp = scpProp != null ? scpProp.FindPropertyRelative("clusteringComputeShader") : null;

        bool missingShader = (distComputeShaderProp != null && distComputeShaderProp.objectReferenceValue == null) ||
                             (clusterComputeShaderProp != null && clusterComputeShaderProp.objectReferenceValue == null);

        if (missingShader)
        {
            _showInternalShaders = true; // シェーダー未割り当て時は自動展開
        }

        _showInternalShaders = EditorGUILayout.BeginFoldoutHeaderGroup(_showInternalShaders, "Internal Compute Shaders");
        if (_showInternalShaders)
        {
            EditorGUI.indentLevel++;
            if (missingShader)
            {
                EditorGUILayout.HelpBox("⚠️ コンピュートシェーダーが設定されていません。以下のプロパティに適切なシェーダーを割り当ててください。", MessageType.Warning);
            }

            if (distComputeShaderProp != null) EditorGUILayout.PropertyField(distComputeShaderProp, new GUIContent("Distance Compute Shader"));
            if (clusterComputeShaderProp != null) EditorGUILayout.PropertyField(clusterComputeShaderProp, new GUIContent("Clustering Compute Shader"));
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawTargetProperty(SerializedProperty dpProp, int detMode)
    {
        if (detMode == (int)HCD_DistanceProcessor.DetectionMode.TransformOnly)
        {
            SerializedProperty targetObjProp = dpProp.FindPropertyRelative("targetObject");
            if (targetObjProp != null) EditorGUILayout.PropertyField(targetObjProp);
            SerializedProperty targetTransformsProp = dpProp.FindPropertyRelative("targetTransforms");
            if (targetTransformsProp != null) EditorGUILayout.PropertyField(targetTransformsProp, true);
        }
        else if (detMode == (int)HCD_DistanceProcessor.DetectionMode.SkinnedMeshRenderer)
        {
            SerializedProperty targetSkinnedProp = dpProp.FindPropertyRelative("targetSkinnedMeshes");
            if (targetSkinnedProp != null) EditorGUILayout.PropertyField(targetSkinnedProp, true);
        }
        else if (detMode == (int)HCD_DistanceProcessor.DetectionMode.MeshFilter)
        {
            SerializedProperty targetMeshFilterProp = dpProp.FindPropertyRelative("targetMeshFilters");
            if (targetMeshFilterProp != null) EditorGUILayout.PropertyField(targetMeshFilterProp, true);
        }
    }
}
#endif