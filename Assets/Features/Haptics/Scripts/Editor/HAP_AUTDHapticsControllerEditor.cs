#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Features.Haptics.Editor;

/// <summary>
/// HAP_AUTDHapticsController のカスタム Inspector エディタ。
/// タブ切り替え（Toolbar）により、設定カテゴリごとに直感的なパラメータ調整 UI を提供します。
/// ハードウェア通信、配置・キャリブレーション、音響、STM、HCD焦点設定、プロファイリングを一つの画面から一元管理します。
/// 具体的な描画は以下の専任 Drawer に完全に分離されています:
/// - HAP_GeneralTabDrawer: 動作モード・照射先オブジェクト選択・パイプライン連携
/// - HAP_HardwareTabDrawer: 通信リンク種別・変調（Modulation）・サイレンサー・温度/ファン
/// - HAP_PlacementTabDrawer: デバイス配置（JSON保存/復元）・プレハブ生成・焦点オフセット
/// - HAP_AcousticTabDrawer: ホログラフィアルゴリズム・音圧強度・GSPAT反復回数・STM設定
/// - HAP_HCDFociTabDrawer: 手指接触領域に対する焦点生成方式（Simplified/Precision）・ソース設定
/// - HAP_DebugTabDrawer: 処理時間プロファイラ・Gizmos・特定デバイス強制停止（ミュート）
/// </summary>
[CustomEditor(typeof(HAP_AUTDHapticsController))]
public class HAP_AUTDHapticsControllerEditor : Editor
{
    public enum Tab
    {
        General = 0,
        Hardware = 1,
        Placement = 2,
        Acoustics = 3,
        HCDFoci = 4,
        Debug = 5
    }

    private const string PrefKeyTab = "HAP_AUTDHapticsControllerEditor_CurrentTab";
    private static Tab _currentTab = Tab.General;

    private HAP_EditorContext _ctx = null!;

    private void OnEnable()
    {
        _currentTab = (Tab)EditorPrefs.GetInt(PrefKeyTab, (int)Tab.General);
        _ctx = new HAP_EditorContext((HAP_AUTDHapticsController)target, serializedObject);
    }

    private void OnDisable()
    {
        EditorPrefs.SetInt(PrefKeyTab, (int)_currentTab);
    }

    public override void OnInspectorGUI()
    {
        _ctx.UpdateAll();

        // Script Reference (Read-only)
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
                case Tab.General:
                    HAP_GeneralTabDrawer.Draw(_ctx);
                    break;
                case Tab.Hardware:
                    HAP_HardwareTabDrawer.Draw(_ctx);
                    break;
                case Tab.Placement:
                    HAP_PlacementTabDrawer.Draw(_ctx);
                    break;
                case Tab.Acoustics:
                    HAP_AcousticTabDrawer.Draw(_ctx);
                    break;
                case Tab.HCDFoci:
                    HAP_HCDFociTabDrawer.Draw(_ctx);
                    break;
                case Tab.Debug:
                    HAP_DebugTabDrawer.Draw(_ctx);
                    break;
            }
            EditorGUILayout.Space(4);
        }

        _ctx.ApplyAll();
    }

    private void DrawTabToolbar()
    {
        bool hwMissing = _ctx.Controller.hardwareController == null;

        var row1Labels = new[]
        {
            new GUIContent("⚙️ General", "動作モード・照射対象オブジェクト選択・パイプライン連携"),
            new GUIContent(hwMissing ? "📡 Hardware ⚠️" : "📡 Hardware", "通信リンク種別・変調・サイレンサー・温度/ファン"),
            new GUIContent("📐 Placement", "デバイス配置データ(JSON)・プレハブ生成・焦点オフセット")
        };

        var row2Labels = new[]
        {
            new GUIContent("🔊 Acoustics", "ホログラフィアルゴリズム・音圧強度・GSPAT反復回数・STM"),
            new GUIContent("🎯 HCD Foci", "手指接触領域に対する焦点生成方式(Simplified/Precision)・ソース設定"),
            new GUIContent("⏱️ Debug", "処理時間プロファイリング・Gizmos・特定デバイス強制ミュート")
        };

        int currentInt = (int)_currentTab;
        int row1Selected = (currentInt < 3) ? currentInt : -1;
        int row2Selected = (currentInt >= 3) ? currentInt - 3 : -1;

        int newRow1 = GUILayout.Toolbar(row1Selected, row1Labels, GUILayout.Height(26));
        if (newRow1 != -1 && newRow1 != row1Selected)
        {
            _currentTab = (Tab)newRow1;
        }

        EditorGUILayout.Space(2);

        int newRow2 = GUILayout.Toolbar(row2Selected, row2Labels, GUILayout.Height(26));
        if (newRow2 != -1 && newRow2 != row2Selected)
        {
            _currentTab = (Tab)(newRow2 + 3);
        }
    }

    private void DrawRuntimeStatusBanner()
    {
        var ctrl = _ctx.Controller;
        bool isHwConnected = ctrl.hardwareController != null && ctrl.hardwareController.IsConnected;
        string connStr = isHwConnected ? "● Hardware Connected" : "○ Hardware Disconnected";

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(connStr, EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Mode: {ctrl.sourceMode} | Output: {ctrl.focusIntensityPascal:F0} Pa", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }
}
#endif
