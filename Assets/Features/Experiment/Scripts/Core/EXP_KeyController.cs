using System.Collections.Generic;
using UnityEngine;
using Core.Logging;
using Core.Keyboard;

/// <summary>
/// 被験者実験（Psychophysics・評価実験）専用のキーボードコントローラー。
/// 実験の開始/中断、回答入力（Choice 1/2, Up/Down, Confirm, Next）、操作パネル表示トグルを一括処理します。
/// AppKeyboard / AppKeyboardManager (Core.Keyboard) を通じて一元管理されたキーバインドに従い動作します (パターン A)。
/// </summary>
[AppLoggable("Experiment")]
[DisallowMultipleComponent]
public class EXP_KeyController : MonoBehaviour, IAppLoggable
{
    public const string TagEXPKey = "EXP_KeyController";

    public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
    {
        const string label = "[EXP_KeyController] Key Operations";
        if (!existingLabels.Contains(label))
        {
            group.entries.Add(new LogInstanceEntry
            {
                label = label,
                tag = TagEXPKey,
                target = this,
                enabled = true
            });
            existingLabels.Add(label);
        }
    }

    [Header("Target Modules")]
    [Tooltip("実験進行マネージャー (未指定時は自動検出)")]
    [SerializeField] private EXP_ExperimentManager experimentManager;

    [Tooltip("回答入力ハンドラー (未指定時は自動検出)")]
    [SerializeField] private EXP_InputHandler inputHandler;

    [Tooltip("インゲーム操作パネル (未指定時は自動検出)")]
    [SerializeField] private EXP_InGameControlPanel controlPanel;

    private void Awake()
    {
        if (experimentManager == null)
        {
            experimentManager = GetComponent<EXP_ExperimentManager>() ?? FindFirstObjectByType<EXP_ExperimentManager>();
        }
        if (inputHandler == null)
        {
            inputHandler = GetComponent<EXP_InputHandler>() ?? FindFirstObjectByType<EXP_InputHandler>();
        }
        if (controlPanel == null)
        {
            controlPanel = GetComponent<EXP_InGameControlPanel>() ?? FindFirstObjectByType<EXP_InGameControlPanel>();
        }
    }

    private void Update()
    {
        if (experimentManager != null && !experimentManager.debugKeyEnabled) return;

        // 1. 実験開始 (Spaceキー / Idle時のみ)
        if (AppKeyboard.GetKeyDown(AppKeyAction.EXP_Start))
        {
            if (experimentManager != null && experimentManager.CurrentState == EXP_ExperimentState.Idle)
            {
                AppLogger.Log(this, TagEXPKey, "[EXP_KeyController] 実験開始キー検知");
                experimentManager.StartExperiment();
            }
        }

        // 2. 実験中断 (Escapeキー / 実行中のみ)
        if (AppKeyboard.GetKeyDown(AppKeyAction.EXP_Abort))
        {
            if (experimentManager != null && 
                experimentManager.CurrentState != EXP_ExperimentState.Idle && 
                experimentManager.CurrentState != EXP_ExperimentState.Finished)
            {
                AppLogger.Log(this, TagEXPKey, "[EXP_KeyController] 実験中断キー検知");
                experimentManager.AbortExperiment();
            }
        }

        // 3. インゲーム操作パネル表示切替 (F1キー等)
        if (AppKeyboard.GetKeyDown(AppKeyAction.EXP_ToggleControlPanel))
        {
            if (controlPanel != null)
            {
                controlPanel.ToggleVisibility();
                AppLogger.Log(this, TagEXPKey, $"[EXP_KeyController] 操作パネル表示切替: {controlPanel.IsVisible}");
            }
        }

        // 4. 実験回答キー入力 (InputHandler が受付中の場合)
        if (inputHandler != null && inputHandler.IsListening)
        {
            CheckResponseKeys();
        }
    }

    private void CheckResponseKeys()
    {
        if (inputHandler == null) return;

        // 選択肢1 (Choice 1 / Yes / Z / 1)
        if (AppKeyboard.GetKeyDown(AppKeyAction.EXP_Choice1))
        {
            AppLogger.Log(this, TagEXPKey, "[EXP_KeyController] 回答検知: Choice1");
            inputHandler.TriggerResponse("Choice1");
        }
        // 選択肢2 (Choice 2 / No / X / 2)
        else if (AppKeyboard.GetKeyDown(AppKeyAction.EXP_Choice2))
        {
            AppLogger.Log(this, TagEXPKey, "[EXP_KeyController] 回答検知: Choice2");
            inputHandler.TriggerResponse("Choice2");
        }
        // 調整 Up (W / UpArrow)
        else if (AppKeyboard.GetKeyDown(AppKeyAction.EXP_AdjustUp))
        {
            AppLogger.Log(this, TagEXPKey, "[EXP_KeyController] 調整検知: Up");
            inputHandler.TriggerResponse("Up");
        }
        // 調整 Down (S / DownArrow)
        else if (AppKeyboard.GetKeyDown(AppKeyAction.EXP_AdjustDown))
        {
            AppLogger.Log(this, TagEXPKey, "[EXP_KeyController] 調整検知: Down");
            inputHandler.TriggerResponse("Down");
        }
        // 確定 (Confirm / Return)
        else if (AppKeyboard.GetKeyDown(AppKeyAction.EXP_Confirm))
        {
            AppLogger.Log(this, TagEXPKey, "[EXP_KeyController] 決定検知: Confirm");
            inputHandler.TriggerResponse("Confirm");
        }
        // 次へ (Next / N)
        else if (AppKeyboard.GetKeyDown(AppKeyAction.EXP_Next))
        {
            AppLogger.Log(this, TagEXPKey, "[EXP_KeyController] 進行検知: Next");
            inputHandler.TriggerResponse("Next");
        }
    }
}
