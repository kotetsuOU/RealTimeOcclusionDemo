using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RsGlobalPointCloudManager))]
public class RsGlobalPointCloudManagerEditor : Editor
{
    // 古い形式の書き出し時などに、保存が完了しているかの状態を保持するフラグ
    private bool _isVerticesSaved;
    private RsGlobalPointCloudManager _manager;

    // パフォーマンスや統計など、インスペクタに表示・隠蔽するためのシリアライズプロパティ群
    private SerializedProperty _statsEnabledProp;
    private SerializedProperty _asyncLoggingEnabledProp;
    private SerializedProperty _gpuProfilerEnabledProp;

    private void OnEnable()
    {
        _manager = (RsGlobalPointCloudManager)target;

        _statsEnabledProp = serializedObject.FindProperty("_statsEnabled");
        _asyncLoggingEnabledProp = serializedObject.FindProperty("_asyncLoggingEnabled");
        _gpuProfilerEnabledProp = serializedObject.FindProperty("_gpuProfilerEnabled");
    }

    public override void OnInspectorGUI()
    {
        // 変更をシリアライズされた内部オブジェクトに同期する
        serializedObject.Update();

        // 独自に描画する統計オプション等の部分を描画から除外しておく
        DrawPropertiesExcluding(
            serializedObject,
            "m_Script",
            "_statsEnabled",
            "_asyncLoggingEnabled",
            "_gpuProfilerEnabled");

        // 統計オプションの描画を実行
        DrawDebugStatisticsSection();

        // 変更を反映する
        serializedObject.ApplyModifiedProperties();
        EditorGUILayout.Space();

        // エディタ専用のバッチ処理やPLY書き出しを行うコントロールUIを描画
        DrawBatchControlSection();
        EditorGUILayout.Space(20);
        DrawPerformanceLoggerSection();
    }

    private void DrawDebugStatisticsSection()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Debug Statistics", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(_statsEnabledProp, new GUIContent("Stats Enabled"));

        using (new EditorGUI.IndentLevelScope())
        {
            if (_statsEnabledProp.boolValue)
            {
                EditorGUILayout.PropertyField(
                    _asyncLoggingEnabledProp,
                    new GUIContent("Log PCA/Cache Stats (Async)", "Write PCA/cache stats to file asynchronously"));
                EditorGUILayout.PropertyField(
                    _gpuProfilerEnabledProp,
                    new GUIContent("GPU Profiler Enabled", "Write GPU compute stats to CSV"));
            }
        }
    }

    private void OnSceneGUI()
    {
        if (Application.isPlaying) return;

        DrawScanRangeGizmo();
    }

    #region Inspector Sections

    /// <summary>
    /// 各カメラの一括設定（例えばフィルタON/OFF）や、
    /// PointCloud(PLY)を書き出すためのUIを描画・管理するメソッド。
    /// </summary>
    private void DrawBatchControlSection()
    {
        EditorGUILayout.LabelField("Batch Control for RsPointCloudRenderer Children", EditorStyles.boldLabel);

        // RsPointCloudCapturer がアタッチされていればPLY出力機能をサポートする
        var capturer = _manager.GetComponent<RsPointCloudCapturer>();
        if (capturer != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("PointCloud Capture (PLY)", EditorStyles.boldLabel);

            // プレイ中、あるいはすでにキャプチャ中の場合は設定変更などの相互作用をロックする
            EditorGUI.BeginDisabledGroup(capturer.IsCapturing || !Application.isPlaying);

            var so = new SerializedObject(capturer);
            so.Update();
            EditorGUILayout.PropertyField(so.FindProperty("captureFrames"), new GUIContent("Frames to Capture"));
            EditorGUILayout.PropertyField(so.FindProperty("outputDirectory"), new GUIContent("Output Directory"));
            so.ApplyModifiedProperties();

            if (Application.isPlaying && capturer.IsCapturing)
            {
                EditorGUILayout.HelpBox("Capturing PointCloud...", MessageType.Info);
            }

            // フレーム数が複数の場合はGround Truth用の長時間キャプチャとして扱う旨を明示
            GUI.backgroundColor = Color.cyan;
            string btnText = capturer.captureFrames > 1 ? $"Capture {capturer.captureFrames} Frames (Ground Truth)" : "Export Snapshot (1 Frame)";

            if (GUILayout.Button(btnText))
            {
                capturer.StartCapturePLY();
            }

            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();

            // 実行中（Playモード）でなければPLYの生成処理は実行できないためヘルプを表示
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Capture is available only during Play Mode.", MessageType.Info);
            }
            EditorGUILayout.Space();
        }
        else
        {
            // 旧形式の描画サポート（Capturerがアタッチされていない場合）
            GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button("Export All Current Vertices (Legacy txt)"))
            {
                ExportAllVertices();
                _isVerticesSaved = true;
            }
            GUI.backgroundColor = Color.white;

            if (_isVerticesSaved && GUILayout.Button("Reset Save Status"))
            {
                _isVerticesSaved = false;
            }
        }

        // カメラ（レンダラー）のグローバルレンジフィルタの状態を取得し表示
        bool anyFiltersEnabled = _manager.AreAnyRangeFiltersEnabled();
        bool allFiltersEnabled = _manager.AreAllRangeFiltersEnabled();
        string filterStateLabel = allFiltersEnabled ? "ON" : anyFiltersEnabled ? "MIXED" : "OFF";
        EditorGUILayout.LabelField($"Range Filter on All: {filterStateLabel}");

        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = allFiltersEnabled ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.6f, 1f, 0.6f);
        EditorGUI.BeginDisabledGroup(allFiltersEnabled);
        if (GUILayout.Button("Set Range Filter ON for All"))
        {
            _manager.SetAllRangeFilters(true);
            SceneView.RepaintAll();
            Debug.Log("[RsGlobalPointCloudManager] Set Range Filter ON for All");
        }

        EditorGUI.EndDisabledGroup();

        GUI.backgroundColor = allFiltersEnabled ? new Color(1f, 0.6f, 0.6f) : new Color(0.7f, 0.7f, 0.7f);
        EditorGUI.BeginDisabledGroup(!allFiltersEnabled);
        if (GUILayout.Button("Set Range Filter OFF for All"))
        {
            _manager.SetAllRangeFilters(false);
            SceneView.RepaintAll();
            Debug.Log("[RsGlobalPointCloudManager] Set Range Filter OFF for All");
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();

        GUI.backgroundColor = Color.white;
    }

    private void DrawPerformanceLoggerSection()
    {
        EditorGUILayout.LabelField("Performance Logger (Batch Control)", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(!Application.isPlaying);

        if (_manager.IsAnyPerformanceLogging())
        {
            GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
            if (GUILayout.Button("Stop Performance Logging on All"))
            {
                _manager.StopAllPerformanceLogs();
            }
            GUI.backgroundColor = Color.white;
        }
        else
        {
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);

            if (GUILayout.Button("Start Logging on All (New Files)"))
            {
                _manager.StartAllPerformanceLogs(append: false);
            }

            if (GUILayout.Button("Start Logging on All (Append)"))
            {
                _manager.StartAllPerformanceLogs(append: true);
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        EditorGUI.EndDisabledGroup();
    }

    #endregion

    #region Scene GUI

    private void DrawScanRangeGizmo()
    {
        var deviceController = Object.FindFirstObjectByType<RsDeviceController>();
        if (deviceController == null)
        {
            DrawWarningWindow("RsDeviceController がシーンに見つかりません。スキャン範囲を描画できません。");
            return;
        }

        Vector3 scanRange = deviceController.RealSenseScanRange;
        float frameWidth = deviceController.FrameWidth;

        Vector3 minPoint = new Vector3(frameWidth, frameWidth, frameWidth);
        Vector3 maxPoint = scanRange - minPoint;
        Vector3 size = maxPoint - minPoint;

        if (size.x < 0 || size.y < 0 || size.z < 0) return;

        Vector3 center = minPoint + size * 0.5f;

        Handles.matrix = _manager.transform.localToWorldMatrix;
        Handles.color = Color.yellow;
        Handles.DrawWireCube(center, size);
    }

    private void DrawWarningWindow(string message)
    {
        Handles.BeginGUI();
        GUILayout.Window(0, new Rect(10, 10, 320, 50), _ =>
        {
            EditorGUILayout.HelpBox(message, MessageType.Warning);
        }, "スキャン範囲 警告");
        Handles.EndGUI();
    }

    #endregion

    #region Export

    private void ExportAllVertices()
    {
        _manager.ApplyToAllRenderers(renderer =>
        {
            var vertices = renderer.GetFilteredVertices();
            var exportFileName = GetExportFileName(renderer);

            if (vertices != null && vertices.Length > 0 && !string.IsNullOrWhiteSpace(exportFileName))
            {
                RsPointCloudExportTool.SaveToFile(vertices, exportFileName);
            }
        });
    }

    private string GetExportFileName(RsPointCloudRenderer renderer)
    {
        var field = typeof(RsPointCloudRenderer).GetField("exportFileName", BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(renderer) as string;
    }

    #endregion
}