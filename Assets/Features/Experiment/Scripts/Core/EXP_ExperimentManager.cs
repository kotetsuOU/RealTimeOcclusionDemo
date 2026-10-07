using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Core.Logging;
using Features.Experiment.Debug;

#nullable enable

/// <summary>
/// 被験者実験のメインステートマネージャー（統括窓口）。
/// 実験設定（<see cref="EXP_SessionSettings"/>）、コンポーネント保持、ステート状態管理、および外部 API を統括します。
/// </summary>
[AppLoggable("Experiment")]
public class EXP_ExperimentManager : MonoBehaviour, IAppLoggable
{
    public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
    {
        var triggers = GetOrAdd<EXP_LogTriggers>();
        triggers.RegisterLogTriggers(group, existingLabels);
    }
    // =====================================================
    // Inspector Settings
    // =====================================================

    [Header("Experiment Settings (Foldable)")]
    public EXP_SessionSettings settings = new EXP_SessionSettings();

    [Header("Sub Components & Text Assets")]
    public EXP_TrialSequencer? sequencer;
    public EXP_DataRecorder? dataRecorder;
    public EXP_EventMarker? eventMarker;
    public EXP_InputHandler? inputHandler;

    [Tooltip("教示・同意・説明文章アセット（空の場合は標準デフォルト文章が適用されます）")]
    public EXP_InstructionTextConfig? instructionText;

    [Header("UI References")]
    public TMP_Text? messageText;
    public GameObject? fixationCross;

    [Header("Haptics Control (Custom Mode Integration)")]
    [Tooltip("実験中に背景の custom 信号を一時停止させる HAP_AUTDHapticsController の参照。\n未設定の場合は自動検索します。実験開始時に bypassHaptics=true、終了時に false に戻します。")]
    public HAP_AUTDHapticsController? hapticsController;

    [Tooltip("true にすると、実験開始時に hapticsController.bypassHaptics を true にして\ncustom 背景信号を停止させます（推奨 ON）。")]
    public bool suppressCustomHapticsOnExperiment = true;

    [Header("Debug Control")]
    public bool debugKeyEnabled = true;

    // Direct Access Properties (Convenience Accessors)
    public string participantId { get => settings.participantId; set => settings.participantId = value; }
    public string participantName { get => settings.participantName; set => settings.participantName = value; }
    public string groupLabel { get => settings.groupLabel; set => settings.groupLabel = value; }
    public bool isDebugMode { get => settings.isDebugMode; set => settings.isDebugMode = value; }
    public int trialsPerBlock { get => settings.trialsPerBlock; set => settings.trialsPerBlock = value; }
    public int blockCount { get => settings.blockCount; set => settings.blockCount = value; }
    public int practiceTrialCount { get => settings.practiceTrialCount; set => settings.practiceTrialCount = value; }
    public float itiDuration { get => settings.itiDuration; set => settings.itiDuration = value; }
    public float stimulusDuration { get => settings.stimulusDuration; set => settings.stimulusDuration = value; }
    public float responseTimeout { get => settings.responseTimeout; set => settings.responseTimeout = value; }
    public float breakDuration { get => settings.breakDuration; set => settings.breakDuration = value; }

    // =====================================================
    // State (Read-Only)
    // =====================================================

    public EXP_ExperimentState CurrentState { get; private set; } = EXP_ExperimentState.Idle;
    public EXP_TrialPhase CurrentPhase { get; private set; } = EXP_TrialPhase.ITI;
    public EXP_ExperimentSession? CurrentSession { get; private set; }
    public EXP_TrialData? CurrentTrial { get; private set; }
    public string CurrentMessage { get; private set; } = "";

    // =====================================================
    // Events
    // =====================================================

    public event Action<EXP_ExperimentState, EXP_ExperimentState>? OnStateChanged;
    public event Action<EXP_TrialData>? OnTrialStarted;
    public event Action<EXP_TrialData>? OnResponseReceived;
    public event Action<EXP_TrialData>? OnTrialCompleted;
    public event Action<EXP_ExperimentSession>? OnExperimentFinished;
    public event Action? OnExperimentAborted;

    private bool _responseReceived = false;

    // =====================================================
    // Unity Lifecycle
    // =====================================================

    void Awake()
    {
        GetOrAdd<EXP_LogTriggers>();
        GetOrAdd<EXP_KeyController>();
        sequencer    ??= GetOrAdd<EXP_TrialSequencer>();
        dataRecorder ??= GetOrAdd<EXP_DataRecorder>();
        eventMarker  ??= GetOrAdd<EXP_EventMarker>();
        inputHandler ??= GetOrAdd<EXP_InputHandler>();
    }

    void Start()
    {
        // 実験開始前の初期状態として、背景触覚のバイパスを復元し全オブジェクトコントローラーのトリガーを解放
        SuppressCustomHaptics(false);
    }

    void OnEnable()
    {
        if (inputHandler != null)
        {
            inputHandler.OnResponse += HandleResponse;
            AppLogger.Log(this, EXP_LogTriggers.TagManager, $"OnEnable: inputHandler.OnResponse に HandleResponse を登録しました。inputHandler={inputHandler.name}");
        }
        else
        {
            AppLogger.LogWarning(this, EXP_LogTriggers.TagManager, "OnEnable: inputHandler が null です！Awakeでの初期化が間に合っていない可能性があります。");
        }
    }

    void OnDisable()
    {
        if (inputHandler != null) inputHandler.OnResponse -= HandleResponse;
    }

    void Update()
    {
        // キーボード操作 (Space / Escape) は EXP_KeyController (AppKeyboard 連動) に委譲されています。
    }

    // =====================================================
    // Public API
    // =====================================================

    public void StartExperiment()
    {
        if (CurrentState != EXP_ExperimentState.Idle) return;
        SuppressCustomHaptics(true);
        StartCoroutine(EXP_ExperimentFlowController.RunMainLoop(this));
    }

    public void AbortExperiment()
    {
        if (CurrentState == EXP_ExperimentState.Idle || CurrentState == EXP_ExperimentState.Finished) return;
        StopAllCoroutines();

        if (inputHandler != null) inputHandler.StopListening();
        if (CurrentSession != null)
        {
            CurrentSession.isFinished = true;
            dataRecorder?.FinalizeSession(CurrentSession);
        }

        eventMarker?.Mark("ExperimentAborted");
        ClearAll();

        SuppressCustomHaptics(false);
        TransitionTo(EXP_ExperimentState.Idle);
        OnExperimentAborted?.Invoke();
    }

    /// <summary>
    /// 実験終了後またはリセット時に、状態・試行データ・背景触覚抑制をクリアして Idle 状態へ戻します。
    /// </summary>
    public void ResetToIdle()
    {
        StopAllCoroutines();
        if (inputHandler != null) inputHandler.StopListening();
        ResetResponseReceived();
        ClearAll();
        SetCurrentTrial(null);
        SetCurrentSession(null);
        SetPhase(EXP_TrialPhase.ITI);
        SuppressCustomHaptics(false);
        TransitionTo(EXP_ExperimentState.Idle);
    }

    /// <summary>
    /// custom モードの背景 HAP_AUTDHapticsController 信号および全オブジェクトコントローラーの抑制状態を制御します。
    /// suppress=true  → bypassHaptics=true（背景信号停止）
    /// suppress=false → bypassHaptics=false（背景信号復元）および全オブジェクトコントローラーの experimentStimulusSuppressed 解放（トリガー解放）
    /// </summary>
    public void SuppressCustomHaptics(bool suppress)
    {
        if (hapticsController == null)
            hapticsController = UnityEngine.Object.FindAnyObjectByType<HAP_AUTDHapticsController>();

        if (suppressCustomHapticsOnExperiment && hapticsController != null)
        {
            hapticsController.bypassHaptics = suppress;
            AppLogger.Log(this, EXP_LogTriggers.TagManager, $"SuppressCustomHaptics: bypassHaptics={suppress} ({hapticsController.name})");
        }

        // 実験非実行中 (suppress=false) の場合は、試行中に設定された各オブジェクトコントローラーの抑制フラグを解除（トリガー解放）
        if (!suppress)
        {
            ReleaseAllObjectHapticsSuppression();
        }
    }

    /// <summary>
    /// シーン内のすべての HAP_BaseObjectHapticsController の experimentStimulusSuppressed を false に解除（トリガー解放）します。
    /// </summary>
    public void ReleaseAllObjectHapticsSuppression()
    {
        var objectControllers = UnityEngine.Object.FindObjectsByType<HAP_BaseObjectHapticsController>(FindObjectsSortMode.None);
        foreach (var ctrl in objectControllers)
        {
            if (ctrl != null)
            {
                ctrl.experimentStimulusSuppressed = false;
            }
        }
        AppLogger.Log(this, EXP_LogTriggers.TagManager, "全 ObjectHapticsController のトリガー（experimentStimulusSuppressed）を解放しました。");
    }

    public void SetMessage(string message)
    {
        CurrentMessage = message ?? "";
        if (messageText != null)
        {
            messageText.text = CurrentMessage;
            messageText.gameObject.SetActive(!string.IsNullOrEmpty(CurrentMessage));
        }
    }

    public void SetFixation(bool visible) { if (fixationCross != null) fixationCross.SetActive(visible); }
    public void SetPhase(EXP_TrialPhase phase) => CurrentPhase = phase;
    public void ClearAll() { SetMessage(""); SetFixation(false); }

    public void TransitionTo(EXP_ExperimentState newState)
    {
        var oldState = CurrentState;
        CurrentState = newState;
        OnStateChanged?.Invoke(oldState, newState);
    }

    public void SetCurrentSession(EXP_ExperimentSession? session) => CurrentSession = session;
    public void SetCurrentTrial(EXP_TrialData? trial) => CurrentTrial = trial;
    public void ResetResponseReceived() => _responseReceived = false;
    public bool HasReceivedResponse() => _responseReceived;

    public IEnumerator WaitForResponse(float timeoutSecs)
    {
        float elapsed = 0f;
        while (!_responseReceived)
        {
            if (timeoutSecs > 0f && elapsed >= timeoutSecs) yield break;
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    public void InvokeTrialStarted(EXP_TrialData trial) => OnTrialStarted?.Invoke(trial);
    public void InvokeTrialCompleted(EXP_TrialData trial) => OnTrialCompleted?.Invoke(trial);
    public void InvokeExperimentFinished(EXP_ExperimentSession session) => OnExperimentFinished?.Invoke(session);

    private void HandleResponse(string responseValue)
    {
        AppLogger.Log(this, EXP_LogTriggers.TagManager, $"HandleResponse 受信: '{responseValue}', CurrentTrial={CurrentTrial?.conditionName ?? "null"}, Phase={CurrentPhase}");
        if (CurrentTrial != null)
        {
            var cond = sequencer?.CurrentCondition;
            string formatted = cond != null ? cond.FormatResponseValue(CurrentTrial, responseValue) : responseValue;
            CurrentTrial.responseValue = formatted;
            CurrentTrial.responseTime  = (double)Time.realtimeSinceStartup;
            OnResponseReceived?.Invoke(CurrentTrial);
        }
        _responseReceived = true;
    }

    private T GetOrAdd<T>() where T : Component => GetComponent<T>() ?? gameObject.AddComponent<T>();
}
