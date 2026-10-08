#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Features.HapticsCollision.Editor
{
    /// <summary>
    /// HCD_Pipeline のカスタム Inspector エディタ。
    /// タブ切り替え（Toolbar）により、設定カテゴリごとに直感的なパラメータ調整 UI を提供します。
    /// 各カテゴリの具体的な描画は以下の専任 Drawer に完全に分離されています:
    /// - HCD_DistanceTabDrawer: 接触判定対象オブジェクト設定、自動同期検知、判定距離しきい値
    /// - HCD_ClusteringTabDrawer: 空間ハッシュクラスタリング、重心集約、第2パス精度オプション
    /// - HCD_TrackingTabDrawer: フレーム間クラスタ追跡、接触力 (Force) 計算、実行時リアルタイムモニタ
    /// - HCD_ShadersTabDrawer: 内部コンピュートシェーダー割り当て、自動セットアップユーティリティ
    /// - HCD_DebugTabDrawer: Scene Gizmos 可視化、AppLogManager（ログ一元管理）連携
    /// </summary>
    [CustomEditor(typeof(HCD_Pipeline))]
    public class HCD_PipelineEditor : UnityEditor.Editor
    {
        public enum Tab
        {
            Distance = 0,
            Clustering = 1,
            Tracking = 2,
            Shaders = 3,
            Debug = 4
        }

        private const string PrefKeyTab = "HCD_PipelineEditor_CurrentTab";
        private static Tab _currentTab = Tab.Distance;

        private HCD_EditorContext _ctx;

        private void OnEnable()
        {
            _currentTab = (Tab)EditorPrefs.GetInt(PrefKeyTab, (int)Tab.Distance);
            _ctx = new HCD_EditorContext((HCD_Pipeline)target, serializedObject);
        }

        private void OnDisable()
        {
            EditorPrefs.SetInt(PrefKeyTab, (int)_currentTab);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Script Reference Field (Read-only)
            if (_ctx.ScriptProp != null)
            {
                GUI.enabled = false;
                EditorGUILayout.PropertyField(_ctx.ScriptProp);
                GUI.enabled = true;
            }

            // ─── PlayMode ステータスバナー ───
            if (Application.isPlaying)
            {
                DrawRuntimeStatusBanner();
                EditorGUILayout.Space(4);
            }

            // ─── タブ切り替えツールバー ───
            DrawTabToolbar();

            EditorGUILayout.Space(8);

            // ─── タブコンテンツ描画 ───
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.Space(4);
                switch (_currentTab)
                {
                    case Tab.Distance:
                        HCD_DistanceTabDrawer.Draw(_ctx);
                        break;
                    case Tab.Clustering:
                        HCD_ClusteringTabDrawer.Draw(_ctx);
                        break;
                    case Tab.Tracking:
                        HCD_TrackingTabDrawer.Draw(_ctx);
                        break;
                    case Tab.Shaders:
                        HCD_ShadersTabDrawer.Draw(_ctx);
                        break;
                    case Tab.Debug:
                        HCD_DebugTabDrawer.Draw(_ctx);
                        break;
                }
                EditorGUILayout.Space(4);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTabToolbar()
        {
            bool missingShaders = HCD_ShadersTabDrawer.HasMissingShaders(_ctx);

            var tabLabels = new[]
            {
                new GUIContent("🎯 Distance", "距離・接触判定および対象オブジェクト設定"),
                new GUIContent("🧩 Clustering", "空間ハッシュクラスタリング・重心推定設定"),
                new GUIContent("⏱️ Tracking", "フレーム間クラスタ追跡・接触力設定"),
                new GUIContent(missingShaders ? "⚡ Shaders ⚠️" : "⚡ Shaders", "内部コンピュートシェーダー設定"),
                new GUIContent("🔍 Debug", "Gizmos 描画・ログ統合管理")
            };

            using (new EditorGUILayout.HorizontalScope())
            {
                _currentTab = (Tab)GUILayout.Toolbar((int)_currentTab, tabLabels, GUILayout.Height(28));
            }
        }

        private void DrawRuntimeStatusBanner()
        {
            int trackedCount = _ctx.Pipeline.GetTrackedClusters()?.Count ?? 0;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("● HCD Pipeline Running", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Active Tracked Clusters: {trackedCount}");
            EditorGUILayout.EndVertical();
        }
    }
}
#endif