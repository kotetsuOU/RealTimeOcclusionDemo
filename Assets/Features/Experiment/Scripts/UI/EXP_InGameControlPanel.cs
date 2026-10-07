using UnityEngine;

#nullable enable

/// <summary>
/// 【インゲーム / Build後用 実験コントロールパネル】
/// Unity Editor 外での Standalone Build（exe実行時）や、
/// ゲーム画面上でコントロールパネルを直接表示・操作するための Runtime コンポーネント。
/// <para>
/// <b>[F1] キー</b>（またはトグルキー）を押すことで、ゲーム画面上にコントロールダッシュボードをオーバーレイ表示します。
/// </para>
/// </summary>
public class EXP_InGameControlPanel : MonoBehaviour
{
    [Header("Panel Settings")]
    [Tooltip("パネルを開閉トグルするキー (デフォルト: F1)")]
    public KeyCode toggleKey = KeyCode.F1;

    [Tooltip("起動時にデフォルトでパネルを表示しておくか")]
    public bool showOnStart = false;

    [Tooltip("Build 後の実行時のみ有効にするか（true の場合、Editor Play モード時は非表示）")]
    public bool buildOnlyMode = false;

    [Header("GUI Layout")]
    [Tooltip("インゲーム GUI パネルの画面サイズ・位置")]
    public Rect windowRect = new Rect(20, 20, 460, 680);

    private bool _isVisible;
    private EXP_ExperimentManager? _manager;

    void Start()
    {
#if UNITY_EDITOR
        if (buildOnlyMode)
        {
            _isVisible = false;
            return;
        }
#endif
        _isVisible = showOnStart;
        _manager = GetComponent<EXP_ExperimentManager>() ?? Object.FindAnyObjectByType<EXP_ExperimentManager>();
    }

    public bool IsVisible => _isVisible;

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleVisibility();
        }
    }

    void OnGUI()
    {
        if (!_isVisible) return;

        if (_manager == null)
            _manager = GetComponent<EXP_ExperimentManager>() ?? Object.FindAnyObjectByType<EXP_ExperimentManager>();

        if (_manager == null) return;

        // ウィンドウのドラッグ描画
        windowRect = GUILayout.Window(
            999123,
            windowRect,
            DrawInGameWindow,
            "🔬 [Build対応] 被験者実験 コントロールパネル (F1で開閉)",
            GUILayout.Width(460),
            GUILayout.Height(680)
        );
    }

    private void DrawInGameWindow(int windowID)
    {
        if (_manager != null)
        {
            EXP_ControlPanelDrawer.DrawDashboard(_manager, isEditorWindow: false);
        }

        GUI.DragWindow(new Rect(0, 0, 10000, 24));
    }

    public void ToggleVisibility()
    {
        _isVisible = !_isVisible;
    }

    public void SetVisibility(bool visible)
    {
        _isVisible = visible;
    }
}
