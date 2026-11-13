using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="PCDOcclusionPipelineController"/> Inspector 最下部の
/// デバッグ設定（<see cref="PCDOcclusionDebugSettings"/>）および PlayMode キャプチャボタン群を描画する Editor ヘルパー。
/// </summary>
internal sealed class PCDOcclusionDebugSectionDrawer
{
    private SerializedProperty _debugSettings;
    private SerializedProperty _enablePixelTagMap;
    private SerializedProperty _enableOcclusionMap;
    private SerializedProperty _debugPatternId;
    private SerializedProperty _debugSectorId;
    private SerializedProperty _recordOcclusionDebugMap;
    private SerializedProperty _recordPixelTagMap;
    private SerializedProperty _recordIntegratedDepthMap;
    private SerializedProperty _recordNeighborhoodMap;
    private SerializedProperty _recordNeighborCountMap;
    private SerializedProperty _enableBufferManagerLog;

    private static bool _showDebugSettings = false;

    public void Bind(SerializedObject serializedObject)
    {
        _debugSettings = serializedObject.FindProperty("debugSettings");
        if (_debugSettings == null) return;

        _enablePixelTagMap = _debugSettings.FindPropertyRelative("enablePixelTagMap");
        _enableOcclusionMap = _debugSettings.FindPropertyRelative("enableOcclusionMap");
        _debugPatternId = _debugSettings.FindPropertyRelative("debugPatternId");
        _debugSectorId = _debugSettings.FindPropertyRelative("debugSectorId");
        _recordOcclusionDebugMap = _debugSettings.FindPropertyRelative("recordOcclusionDebugMap");
        _recordPixelTagMap = _debugSettings.FindPropertyRelative("recordPixelTagMap");
        _recordIntegratedDepthMap = _debugSettings.FindPropertyRelative("recordIntegratedDepthMap");
        _recordNeighborhoodMap = _debugSettings.FindPropertyRelative("recordNeighborhoodMap");
        _recordNeighborCountMap = _debugSettings.FindPropertyRelative("recordNeighborCountMap");
        _enableBufferManagerLog = _debugSettings.FindPropertyRelative("enableBufferManagerLog");
    }

    public void Draw(SerializedObject serializedObject, PCDOcclusionPipelineController controller)
    {
        GUI.backgroundColor = new Color(0.95f, 0.95f, 0.95f);
        _showDebugSettings = EditorGUILayout.BeginFoldoutHeaderGroup(
            _showDebugSettings, "🛠️ Debug & Record Settings (変数調整・キャプチャ)");
        GUI.backgroundColor = Color.white;

        if (_showDebugSettings && _debugSettings != null)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("Display Debug (常時表示)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_enablePixelTagMap, new GUIContent("PixelTagMap 表示 (白黒由来マップ)"));
            EditorGUILayout.PropertyField(_enableOcclusionMap, new GUIContent("OcclusionMap 表示 (ヒートマップ)"));
            EditorGUILayout.PropertyField(_debugSectorId, new GUIContent("Sector ID (-1:OFF, 0..7:個別, 8:8bit統合, 9:真値)"));
            EditorGUILayout.PropertyField(_debugPatternId, new GUIContent("Pattern ID (-1:OFF, 0..255:特定パターン抽出)"));

            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField("Record Debug (1フレーム記録)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_recordOcclusionDebugMap, new GUIContent("Record Occlusion Map"));
            EditorGUILayout.PropertyField(_recordPixelTagMap, new GUIContent("Record PixelTag Map"));
            EditorGUILayout.PropertyField(_recordIntegratedDepthMap, new GUIContent("Record Integrated Depth"));
            EditorGUILayout.PropertyField(_recordNeighborhoodMap, new GUIContent("Record Neighborhood Map"));
            EditorGUILayout.PropertyField(_recordNeighborCountMap, new GUIContent("Record SectorMask (RAW/CSV/PNG)"));

            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField("Logger Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_enableBufferManagerLog, new GUIContent("BufferManager ログ出力"));

            EditorGUI.indentLevel--;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("📸 Quick Capture Buttons (PlayMode 専用)", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.4f, 0.85f, 0.95f);
            if (GUILayout.Button("📸 占有セクターマスク (SectorMask RAW/CSV/PNG) を出力", GUILayout.Height(28)))
            {
                controller.debugSettings.TriggerRecordSectorMask();
                EditorUtility.SetDirty(controller);
                serializedObject.Update();
            }

            GUI.backgroundColor = new Color(0.95f, 0.85f, 0.4f);
            if (GUILayout.Button("📸 オクルージョンマップ (OcclusionMap PNG/CSV) を出力", GUILayout.Height(26)))
            {
                controller.debugSettings.TriggerRecordOcclusionMap();
                EditorUtility.SetDirty(controller);
                serializedObject.Update();
            }

            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("📸 全デバッグマップ 一括出力", GUILayout.Height(26)))
            {
                controller.debugSettings.TriggerRecordAllDebugMaps();
                EditorUtility.SetDirty(controller);
                serializedObject.Update();
            }

            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }
}

