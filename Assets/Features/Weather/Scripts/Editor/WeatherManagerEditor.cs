#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Features.Weather.Editor
{
    /// <summary>
    /// WeatherManager のカスタム Inspector エディタ。
    /// タブ切り替え（Toolbar）による直感的なパラメータ調整 UI を提供します。
    /// 各タブおよびコントロールの具体的な描画は以下の専任 Drawer に委譲されています:
    /// - WeatherPlayModeControlDrawer: 実行時の雨・落雷・雲クイックコントロール
    /// - WeatherGeneralTabDrawer: 空間・カメラ設定および自動セットアップ
    /// - WeatherEffectTabDrawer: 雨・雲・落雷エフェクトパラメータ
    /// - WeatherEnvironmentTabDrawer: 照明・音響・ジェスチャー入力パラメータ
    /// </summary>
    [CustomEditor(typeof(WeatherManager))]
    public class WeatherManagerEditor : UnityEditor.Editor
    {
        private enum Tab
        {
            General,
            Rain,
            Cloud,
            Lightning,
            Lighting,
            Audio,
            Input
        }

        private static Tab _currentTab = Tab.General;
        private static bool _showAdvanced = false;

        private static readonly string[] TabLabels = new[]
        {
            "General",
            "Rain",
            "Cloud",
            "Lightning",
            "Lighting",
            "Audio",
            "Input"
        };

        // Properties
        private SerializedProperty _viewerProp;
        private SerializedProperty _cloudYProp;
        private SerializedProperty _groundYProp;
        private SerializedProperty _rainFadeDurationProp;
        private SerializedProperty _useSharedBoundsProp;
        private SerializedProperty _boundsCenterProp;
        private SerializedProperty _boundsSizeProp;
        private SerializedProperty _followViewerProp;

        private SerializedProperty _rainProp;
        private SerializedProperty _cloudsProp;
        private SerializedProperty _lightningProp;
        private SerializedProperty _lightingProp;
        private SerializedProperty _audioSettingsProp;
        private SerializedProperty _inputProp;

        private SerializedProperty _rainProcessorProp;
        private SerializedProperty _cloudProcessorProp;
        private SerializedProperty _lightningProcessorProp;
        private SerializedProperty _lightingProcessorProp;
        private SerializedProperty _audioSynthesizerProp;
        private SerializedProperty _inputBridgeProp;
        private SerializedProperty _keyControllerProp;

        private void OnEnable()
        {
            _viewerProp = serializedObject.FindProperty("viewer");
            _cloudYProp = serializedObject.FindProperty("cloudY");
            _groundYProp = serializedObject.FindProperty("groundY");
            _rainFadeDurationProp = serializedObject.FindProperty("rainFadeDuration");
            _useSharedBoundsProp = serializedObject.FindProperty("useSharedBounds");
            _boundsCenterProp = serializedObject.FindProperty("boundsCenter");
            _boundsSizeProp = serializedObject.FindProperty("boundsSize");
            _followViewerProp = serializedObject.FindProperty("followViewer");

            _rainProp = serializedObject.FindProperty("rain");
            _cloudsProp = serializedObject.FindProperty("clouds");
            _lightningProp = serializedObject.FindProperty("lightning");
            _lightingProp = serializedObject.FindProperty("lighting");
            _audioSettingsProp = serializedObject.FindProperty("audioSettings");
            _inputProp = serializedObject.FindProperty("input");

            _rainProcessorProp = serializedObject.FindProperty("rainProcessor");
            _cloudProcessorProp = serializedObject.FindProperty("cloudProcessor");
            _lightningProcessorProp = serializedObject.FindProperty("lightningProcessor");
            _lightingProcessorProp = serializedObject.FindProperty("lightingProcessor");
            _audioSynthesizerProp = serializedObject.FindProperty("audioSynthesizer");
            _inputBridgeProp = serializedObject.FindProperty("inputBridge");
            _keyControllerProp = serializedObject.FindProperty("keyController");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var manager = (WeatherManager)target;

            EditorGUILayout.Space(4);

            // ─── PlayMode クイックコントロール（再生時は最上部に表示） ───
            if (Application.isPlaying)
            {
                WeatherPlayModeControlDrawer.Draw(manager);
                EditorGUILayout.Space(8);
            }

            // ─── タブ切り替えツールバー ───
            using (new EditorGUILayout.HorizontalScope())
            {
                _currentTab = (Tab)GUILayout.Toolbar((int)_currentTab, TabLabels, GUILayout.Height(28));
            }

            EditorGUILayout.Space(10);

            // ─── 各タブコンテンツ ───
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                switch (_currentTab)
                {
                    case Tab.General:
                        WeatherGeneralTabDrawer.Draw(
                            manager,
                            _viewerProp,
                            _cloudYProp,
                            _groundYProp,
                            _rainFadeDurationProp,
                            _useSharedBoundsProp,
                            _boundsCenterProp,
                            _boundsSizeProp,
                            _followViewerProp);
                        break;
                    case Tab.Rain:
                        WeatherEffectTabDrawer.DrawRain(_rainProp);
                        break;
                    case Tab.Cloud:
                        WeatherEffectTabDrawer.DrawCloud(_cloudsProp);
                        break;
                    case Tab.Lightning:
                        WeatherEffectTabDrawer.DrawLightning(_lightningProp, _useSharedBoundsProp);
                        break;
                    case Tab.Lighting:
                        WeatherEnvironmentTabDrawer.DrawLighting(_lightingProp);
                        break;
                    case Tab.Audio:
                        WeatherEnvironmentTabDrawer.DrawAudio(_audioSettingsProp);
                        break;
                    case Tab.Input:
                        WeatherEnvironmentTabDrawer.DrawInput(_inputProp);
                        break;
                }
            }

            EditorGUILayout.Space(8);

            // ─── Advanced: 内部プロセッサ参照 ───
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Internal Processors & Components (Advanced)", true);
            if (_showAdvanced)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(_rainProcessorProp);
                    EditorGUILayout.PropertyField(_cloudProcessorProp);
                    EditorGUILayout.PropertyField(_lightningProcessorProp);
                    EditorGUILayout.PropertyField(_lightingProcessorProp);
                    EditorGUILayout.PropertyField(_audioSynthesizerProp);
                    EditorGUILayout.PropertyField(_inputBridgeProp);
                    EditorGUILayout.PropertyField(_keyControllerProp);
                }
            }

            if (serializedObject.ApplyModifiedProperties())
            {
                manager.ApplyAllSettings();
            }
        }
    }
}
#endif
