using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;
using System.Linq;
using Core.Logging;
using Features.Haptics.Config;
using Features.Haptics.Debug;
using Features.Haptics.Processors;

#nullable enable

/// <summary>
/// HCD_Pipeline やオブジェクト接触判定から焦点位置（Foci / STM）をリアルタイム計算し、
/// HAP_AUTDHardwareController 経由でマルチフォーカス超音波を出力する触覚生成パイプラインコントローラー。
/// 設定データ（Config）・ターゲット収集・焦点生成・ハードウェア送信を専任クラスへ委譲し、
/// ライフサイクルとオーケストレーションに特化したスリムな司令塔クラスです。
/// </summary>
[AppLoggable("Haptics")]
public class HAP_AUTDHapticsController : MonoBehaviour, IAppLoggable, ISerializationCallbackReceiver
{
    public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
    {
        var triggers = GetComponent<HAP_LogTriggers>() ?? gameObject.AddComponent<HAP_LogTriggers>();
        triggers.RegisterLogTriggers(group, existingLabels);
    }

    [Header("Hardware Reference")]
    [Tooltip("物理通信接続および送信を担当する HAP_AUTDHardwareController の参照。未指定時は自動取得します。")]
    public HAP_AUTDHardwareController hardwareController = null!;

    [Tooltip("配置・座標オフセットを管理する HAP_AUTDTransformLoader の参照。未指定時は自動取得します。")]
    public HAP_AUTDTransformLoader transformLoader = null!;

    [Header("Operation Settings")]
    [Tooltip("触覚出力のターゲットデータソース（AutoHCD: 手の接触クラスタ、ObjectTarget: オブジェクト部位ターゲット、Manual: 手動API）")]
    public HapticsSourceMode sourceMode = HapticsSourceMode.AutoHCD;

    [Header("Dependencies")]
    [Tooltip("接触判定を行う HCD_Pipeline の参照。自動モード時に毎フレームここからクラスタ情報を取得します。")]
    public HCD_Pipeline hcdPipeline = null!;

    [Tooltip("HCDの焦点生成設定を行う HAP_HCDFociSettings の参照。未指定時は自動取得します。")]
    public HAP_HCDFociSettings hcdFociSettings = null!;

    [Tooltip("オブジェクトのハプティクス制御コンポーネントのリスト。アタッチされている場合、特定オブジェクト位置へ照射します。")]
    public List<HAP_BaseObjectHapticsController> objectHapticsControllers = new List<HAP_BaseObjectHapticsController>();

    [Tooltip("現在アクティブなコントローラーのインデックス（0以上）。選択されたオブジェクトのみ enabled=true に同期されます。")]
    public int activeObjectControllerIndex = 0;

    /// <summary>
    /// 単一オブジェクトコントローラーとの互換用アクセサ。最初の要素を返します/セットします。
    /// </summary>
    public HAP_BaseObjectHapticsController? objectHapticsController
    {
        get => objectHapticsControllers.FirstOrDefault(c => c != null && c.enabled);
        set
        {
            if (value != null && !objectHapticsControllers.Contains(value))
            {
                objectHapticsControllers.Add(value);
            }
        }
    }

    /// <summary>
    /// 指定されたインデックスのコントローラーのみを enabled / GameObject.SetActive = true にし、他を非アクティブへ連動切り替えします。
    /// </summary>
    public void SetActiveControllerIndex(int index)
    {
        activeObjectControllerIndex = index;
        _targetDispatcher.SynchronizeActiveController(objectHapticsControllers, ref activeObjectControllerIndex, gameObject);
    }

    [HideInInspector]
    public bool bypassHaptics = false;

    // ─── 設定コンフィグ群（責務分離: Config） ───────────────────────────
    [Header("Acoustic Configuration")]
    [Tooltip("ホログラフィアルゴリズム、超音波出力強度、指向性グルーピング設定")]
    public HAP_AcousticConfig acousticConfig = new HAP_AcousticConfig();

    [Header("STM Configuration")]
    [Tooltip("時空間変調 (STM) モードおよび周波数設定")]
    public HAP_STMConfig stmConfig = new HAP_STMConfig();

    [Header("Profiling Configuration")]
    [Tooltip("パイプライン処理時間計測および同期送信設定")]
    public HAP_ProfilingConfig profilingConfig = new HAP_ProfilingConfig();

    // ─── 外部互換プロパティ（既存スクリプト・外部参照との互換性を完全維持） ─────
    public HoloAlgorithm holoAlgorithm { get => acousticConfig.holoAlgorithm; set => acousticConfig.holoAlgorithm = value; }
    public float focusIntensityPascal { get => acousticConfig.focusIntensityPascal; set => acousticConfig.focusIntensityPascal = value; }
    public bool enableDirectionalGrouping { get => acousticConfig.enableDirectionalGrouping; set => acousticConfig.enableDirectionalGrouping = value; }
    public float directionalAngleThreshold { get => acousticConfig.directionalAngleThreshold; set => acousticConfig.directionalAngleThreshold = value; }
    public uint gspatRepeatCount { get => acousticConfig.gspatRepeatCount; set => acousticConfig.gspatRepeatCount = value; }

    public HapticsSTMMode stmMode { get => stmConfig.stmMode; set => stmConfig.stmMode = value; }
    public float stmFrequency { get => stmConfig.stmFrequency; set => stmConfig.stmFrequency = value; }
    public GainSTMMode gainStmMode { get => stmConfig.gainStmMode; set => stmConfig.gainStmMode = value; }

    /// <summary>
    /// 外部スクリプト互換用オフセットアクセサ。transformLoader の offset を参照/更新します。
    /// </summary>
    public Vector3 offset
    {
        get => transformLoader != null ? transformLoader.offset : Vector3.zero;
        set
        {
            if (transformLoader != null) transformLoader.offset = value;
        }
    }

    public bool enableProfiling { get => profilingConfig.enableProfiling; set => profilingConfig.enableProfiling = value; }
    public bool synchronousSend { get => profilingConfig.synchronousSend; set => profilingConfig.synchronousSend = value; }
    public int profilingLogInterval { get => profilingConfig.profilingLogInterval; set => profilingConfig.profilingLogInterval = value; }

    [Header("Debug")]
    [Tooltip("エディタ上でデバイスのサイズと位置を Gizmo (青色の枠) で表示します。")]
    public bool visualizeDevices = true;

    [HideInInspector]
    public HAP_AUTDPerformanceProfiler performanceProfiler = new HAP_AUTDPerformanceProfiler();

    [HideInInspector]
    public HAP_AUTDDebugDisabler? debugDisabler;

    // ─── 旧シリアライズ値マイグレーション用フィールド ─────────────────────────
    [SerializeField, HideInInspector, FormerlySerializedAs("focusIntensityPascal")]
    private float _legacyFocusIntensityPascal = -1f;

    [SerializeField, HideInInspector, FormerlySerializedAs("stmFrequency")]
    private float _legacyStmFrequency = -1f;

    void ISerializationCallbackReceiver.OnBeforeSerialize() { }

    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        if (_legacyFocusIntensityPascal > 0f)
        {
            acousticConfig.focusIntensityPascal = _legacyFocusIntensityPascal;
            _legacyFocusIntensityPascal = -1f;
        }
        if (_legacyStmFrequency > 0f)
        {
            stmConfig.stmFrequency = _legacyStmFrequency;
            _legacyStmFrequency = -1f;
        }
    }

    // ─── 専任プロセッサ（責務分離: Processors） ─────────────────────────
    private readonly HAP_TargetSourceDispatcher _targetDispatcher = new HAP_TargetSourceDispatcher();
    private readonly HAP_AcousticPipelineExecutor _pipelineExecutor = new HAP_AcousticPipelineExecutor();

    void Awake()
    {
        if (GetComponent<HAP_LogTriggers>() == null) gameObject.AddComponent<HAP_LogTriggers>();
        debugDisabler = GetComponent<HAP_AUTDDebugDisabler>() ?? GetComponentInChildren<HAP_AUTDDebugDisabler>();

        if (hardwareController == null)
        {
            hardwareController = GetComponent<HAP_AUTDHardwareController>()
                              ?? GetComponentInChildren<HAP_AUTDHardwareController>()
                              ?? FindAnyObjectByType<HAP_AUTDHardwareController>();
            if (hardwareController == null)
            {
                hardwareController = gameObject.AddComponent<HAP_AUTDHardwareController>();
            }
        }

        if (transformLoader == null)
        {
            transformLoader = GetComponent<HAP_AUTDTransformLoader>()
                           ?? GetComponentInChildren<HAP_AUTDTransformLoader>()
                           ?? FindAnyObjectByType<HAP_AUTDTransformLoader>();
        }

        if (hcdPipeline == null)
        {
            hcdPipeline = FindAnyObjectByType<HCD_Pipeline>();
        }

        if (hcdFociSettings == null)
        {
            if (hcdPipeline != null)
            {
                hcdFociSettings = hcdPipeline.GetComponent<HAP_HCDFociSettings>()
                               ?? hcdPipeline.GetComponentInChildren<HAP_HCDFociSettings>();
            }
            if (hcdFociSettings == null)
            {
                hcdFociSettings = GetComponent<HAP_HCDFociSettings>()
                               ?? GetComponentInChildren<HAP_HCDFociSettings>()
                               ?? FindAnyObjectByType<HAP_HCDFociSettings>();
            }
        }
    }

#if UNITY_EDITOR
    private void Reset()
    {
        if (hardwareController == null) hardwareController = GetComponent<HAP_AUTDHardwareController>() ?? GetComponentInChildren<HAP_AUTDHardwareController>() ?? FindAnyObjectByType<HAP_AUTDHardwareController>();
        if (transformLoader == null) transformLoader = GetComponent<HAP_AUTDTransformLoader>() ?? GetComponentInChildren<HAP_AUTDTransformLoader>() ?? FindAnyObjectByType<HAP_AUTDTransformLoader>();
        if (hcdPipeline == null) hcdPipeline = FindAnyObjectByType<HCD_Pipeline>();
        if (hcdFociSettings == null) hcdFociSettings = GetComponent<HAP_HCDFociSettings>() ?? GetComponentInChildren<HAP_HCDFociSettings>() ?? FindAnyObjectByType<HAP_HCDFociSettings>();
        if (debugDisabler == null) debugDisabler = GetComponent<HAP_AUTDDebugDisabler>() ?? GetComponentInChildren<HAP_AUTDDebugDisabler>();
    }
#endif

    void Update()
    {
        if (hardwareController == null || !hardwareController.IsConnected) return;

        performanceProfiler.Enabled = profilingConfig.enableProfiling;
        performanceProfiler.LogEnabled = AppLogger.IsEnabled(this, HAP_LogTriggers.TagPerformanceProfiler);
        performanceProfiler.LogInterval = profilingConfig.profilingLogInterval;

        UpdateHaptics();
    }

    /// <summary>
    /// ターゲット抽出と超音波出力パイプラインを順次ディスパッチします。
    /// </summary>
    private void UpdateHaptics()
    {
        Vector3 currentOffset = offset;

        bool hasActiveTargets = _targetDispatcher.CollectTargets(
            sourceMode,
            bypassHaptics,
            hcdPipeline,
            objectHapticsControllers,
            activeObjectControllerIndex,
            acousticConfig.focusIntensityPascal,
            currentOffset,
            out var activeClusters,
            out var objectFociList);

        _pipelineExecutor.Execute(
            hasActiveTargets,
            sourceMode,
            activeClusters,
            objectFociList,
            hcdFociSettings,
            hardwareController,
            performanceProfiler,
            debugDisabler,
            acousticConfig,
            stmConfig,
            profilingConfig,
            currentOffset);
    }

    private void OnDrawGizmos()
    {
        HAP_GizmoVisualizer.DrawControllerGizmos(this);
    }
}
