using UnityEngine;
using System;
using Core.Logging;
using Features.Experiment.Debug;

#nullable enable

/// <summary>
/// 全実験パラダイム（2AFC, SingleStimulus, ABX, Adjustment）に対応するキーバインディング構造体。
/// Inspector 上で自由に設定・カスタマイズできます。
/// </summary>
[Serializable]
public class EXP_KeyBindings
{
    [Header("Common System Keys")]
    public KeyCode startKey = KeyCode.F1;
    public KeyCode nextKey = KeyCode.F1;
    public KeyCode abortKey = KeyCode.Escape;

    [Header("2AFC & ABX Keys")]
    public KeyCode choice1Key = KeyCode.F2;
    public KeyCode choice2Key = KeyCode.F3;

    [Header("Single Stimulus (Yes/No) Keys")]
    public KeyCode yesKey = KeyCode.F2;
    public KeyCode noKey = KeyCode.F3;

    [Header("Adjustment (Method of Adjustment) Keys")]
    public KeyCode upKey = KeyCode.UpArrow;
    public KeyCode downKey = KeyCode.DownArrow;
    public KeyCode confirmKey = KeyCode.Space;
}

/// <summary>
/// 全実験パラダイム対応の参加者入力受付コンポーネント。
/// </summary>
public class EXP_InputHandler : MonoBehaviour
{
    // =====================================================
    // Inspector Settings
    // =====================================================

    [Header("Input Settings")]
    public EXP_InputDevice inputDevice = EXP_InputDevice.Any;

    [Header("Key Bindings")]
    public EXP_KeyBindings keyBindings = new EXP_KeyBindings();

    [Header("Gamepad Settings")]
    public string[] gamepadButtons = new string[] { "buttonSouth", "buttonNorth" };

    [Header("Behavior")]
    public bool blockAfterFirstResponse = true;
    public bool runInBackground = true;

    // =====================================================
    // State (Read-Only)
    // =====================================================

    public bool IsListening { get; private set; } = false;
    public bool HasResponded { get; private set; } = false;

    // =====================================================
    // Events
    // =====================================================

    public event Action<string>? OnResponse;

    // =====================================================
    // Unity Lifecycle
    // =====================================================

    void Awake()
    {
        if (runInBackground)
            Application.runInBackground = true;
    }

    void Update()
    {
        // キーボード入力 (Update) の監視は EXP_KeyController (AppKeyboard 連動) に委譲されています。
        // （IMGUI / EditorWindow フォーカス時の補足は引き続き OnGUI() で処理されます）
    }

    /// <summary>
    /// Unity Editor の EditorWindow (IMGUI) にフォーカスが当たっている場合、
    /// Input.GetKeyDown() はキーイベントを受け取れません。
    /// OnGUI() 内で Event.current を使うことで、EditorWindow フォーカス中でも
    /// キーボード入力を確実にキャッチします。
    /// </summary>
    void OnGUI()
    {
        if (!IsListening || (blockAfterFirstResponse && HasResponded)) return;
        if (inputDevice != EXP_InputDevice.Keyboard && inputDevice != EXP_InputDevice.Any) return;

        var e = Event.current;
        if (e == null || e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return;

        string? response = GetResponseForKeyCode(e.keyCode);
        if (response != null)
        {
            e.Use();
            AppLogger.Log(this, EXP_LogTriggers.TagInputHandler, $"OnGUI キー検知: {e.keyCode} → '{response}'");
            Respond(response);
        }
    }

    // =====================================================
    // Public API
    // =====================================================

    public void StartListening() 
    { 
        HasResponded = false; 
        IsListening = true; 
        AppLogger.Log(this, EXP_LogTriggers.TagInputHandler, $"StartListening 開始。inputDevice={inputDevice}, blockAfterFirstResponse={blockAfterFirstResponse}");
    }
    public void StopListening()  
    { 
        IsListening = false; 
        AppLogger.Log(this, EXP_LogTriggers.TagInputHandler, "StopListening");
    }
    public void ResetResponse()  { HasResponded = false; }

    public void TriggerResponse(string responseValue)
    {
        if (!IsListening || (blockAfterFirstResponse && HasResponded)) return;
        Respond(responseValue);
    }

    // =====================================================
    // Private Helpers
    // =====================================================

    private void CheckKeyboard()
    {
        if (inputDevice != EXP_InputDevice.Keyboard && inputDevice != EXP_InputDevice.Any)
            return;

        // 2AFC / ABX / SingleStimulus (Choice 1 vs 2)
        if (Input.GetKeyDown(keyBindings.choice1Key) || Input.GetKeyDown(keyBindings.yesKey) || Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.Alpha1)) 
            { AppLogger.Log(this, EXP_LogTriggers.TagInputHandler, "キー検知: Choice1"); Respond("Choice1"); }
        else if (Input.GetKeyDown(keyBindings.choice2Key) || Input.GetKeyDown(keyBindings.noKey) || Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.Alpha2)) 
            { AppLogger.Log(this, EXP_LogTriggers.TagInputHandler, "キー検知: Choice2"); Respond("Choice2"); }
        // Adjustment
        else if (Input.GetKeyDown(keyBindings.upKey) || Input.GetKeyDown(KeyCode.W)) { AppLogger.Log(this, EXP_LogTriggers.TagInputHandler, "キー検知: upKey"); Respond("Up"); }
        else if (Input.GetKeyDown(keyBindings.downKey) || Input.GetKeyDown(KeyCode.S)) { AppLogger.Log(this, EXP_LogTriggers.TagInputHandler, "キー検知: downKey"); Respond("Down"); }
        else if (Input.GetKeyDown(keyBindings.confirmKey) || Input.GetKeyDown(KeyCode.Return)) { AppLogger.Log(this, EXP_LogTriggers.TagInputHandler, "キー検知: confirmKey"); Respond("Confirm"); }
        // System
        else if (Input.GetKeyDown(keyBindings.nextKey)) { AppLogger.Log(this, EXP_LogTriggers.TagInputHandler, "キー検知: nextKey"); Respond("Next"); }
        else if (Input.GetKeyDown(keyBindings.startKey)) { AppLogger.Log(this, EXP_LogTriggers.TagInputHandler, "キー検知: startKey"); Respond("Start"); }
    }

    /// <summary>
    /// 指定キーコードに対応するレスポンス文字列を返します。
    /// Update (Input.GetKeyDown) と OnGUI (Event.current) の両方から共用されます。
    /// </summary>
    private string? GetResponseForKeyCode(KeyCode key)
    {
        // 2AFC / ABX / SingleStimulus
        if (key == keyBindings.choice1Key || key == keyBindings.yesKey || key == KeyCode.Z || key == KeyCode.Alpha1) return "Choice1";
        if (key == keyBindings.choice2Key || key == keyBindings.noKey || key == KeyCode.X || key == KeyCode.Alpha2) return "Choice2";
        // Adjustment
        if (key == keyBindings.upKey || key == KeyCode.W) return "Up";
        if (key == keyBindings.downKey || key == KeyCode.S) return "Down";
        if (key == keyBindings.confirmKey || key == KeyCode.Return) return "Confirm";
        // System
        if (key == keyBindings.nextKey) return "Next";
        if (key == keyBindings.startKey) return "Start";
        return null;
    }

    private void Respond(string responseValue)
    {
        AppLogger.Log(this, EXP_LogTriggers.TagInputHandler, $"Respond('{responseValue}') 発火。IsListening={IsListening}, HasResponded={HasResponded}, OnResponseリスナー数={OnResponse?.GetInvocationList()?.Length ?? 0}");
        HasResponded = true;
        OnResponse?.Invoke(responseValue);
    }
}
