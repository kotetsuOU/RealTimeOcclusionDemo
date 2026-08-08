using UnityEngine;
using System.Collections;
using System;
using Core.Logging;
using Features.Experiment.Debug;

#nullable enable

/// <summary>
/// Pointing実験のタスクを制御し、物理的なTool（RealSense等）の位置と仮想ターゲットの距離を判定するコントローラ
/// </summary>
[AppLoggable("PointingTask")]
public class EXP_PointingController : MonoBehaviour
{
    [Header("Tracking References")]
    [Tooltip("RealSense等から取得した物理Toolの先端位置（対象の Transform をアサインしてください）")]
    public Transform? toolTip;

    private GameObject? currentTargetInstance;
    private EXP_PointingCondition? currentCondition;
    private EXP_TrialData? currentTrial;

    private EXP_ExperimentManager? manager;

    void Awake()
    {
        manager = UnityEngine.Object.FindFirstObjectByType<EXP_ExperimentManager>();
    }

    void Start()
    {
        if (manager != null)
        {
            manager.OnTrialCompleted += HandleTrialCompleted;
            if (manager.inputHandler != null)
            {
                manager.inputHandler.OnResponse += HandleResponse;
            }
        }
        else
        {
            AppLogger.LogError(this, "EXP_ExperimentManager.Instance が見つかりません。");
        }
    }

    void OnDestroy()
    {
        if (manager != null)
        {
            manager.OnTrialCompleted -= HandleTrialCompleted;
            if (manager.inputHandler != null)
            {
                manager.inputHandler.OnResponse -= HandleResponse;
            }
        }
    }

    /// <summary>
    /// EXP_PointingCondition から呼び出され、ターゲットの配置等を行います。
    /// </summary>
    public IEnumerator ExecutePointingTask(EXP_PointingCondition condition, EXP_TrialData trial)
    {
        currentCondition = condition;
        currentTrial = trial;

        AppLogger.Log(this, $"ExecutePointingTask開始: useOcclusion={condition.useOcclusion}, Placement={condition.placementType}");

        // 仮想ターゲットを生成・配置
        if (condition.virtualTargetPrefab != null)
        {
            currentTargetInstance = Instantiate(condition.virtualTargetPrefab, transform);
            currentTargetInstance.transform.localPosition = condition.targetLocalPosition;
        }
        else
        {
            AppLogger.LogWarning(this, "virtualTargetPrefab が未設定です。");
        }

        // 条件情報をメタデータに記録
        trial.metadata["UseOcclusion"] = condition.useOcclusion.ToString();
        trial.metadata["PlacementType"] = condition.placementType.ToString();
        trial.metadata["TargetLocalX"] = condition.targetLocalPosition.x.ToString("F4");
        trial.metadata["TargetLocalY"] = condition.targetLocalPosition.y.ToString("F4");
        trial.metadata["TargetLocalZ"] = condition.targetLocalPosition.z.ToString("F4");

        // コルーチンとしては待機せず終了し、EXP_TrialRunner側の Response フェーズに移行させます
        yield break; 
    }

    /// <summary>
    /// 参加者が決定ボタンを押した瞬間の位置をキャプチャし、誤差を計算します。
    /// </summary>
    private void HandleResponse(string response)
    {
        if (currentCondition == null || currentTrial == null) return;
        if (currentTargetInstance == null || toolTip == null)
        {
            AppLogger.LogWarning(this, "ターゲットインスタンスまたは ToolTip が存在しないため誤差計算をスキップします。");
            return;
        }

        // ワールド座標での誤差計算
        Vector3 pTool = toolTip.position;
        Vector3 pTarget = currentTargetInstance.transform.position;

        float eX = Mathf.Abs(pTool.x - pTarget.x);
        float eY = Mathf.Abs(pTool.y - pTarget.y);
        float eZ = Mathf.Abs(pTool.z - pTarget.z);
        float e3D = Vector3.Distance(pTool, pTarget);
        bool isSuccess = e3D < currentCondition.successRadius;

        // 計測結果をメタデータに記録
        currentTrial.metadata["ToolPosX"] = pTool.x.ToString("F4");
        currentTrial.metadata["ToolPosY"] = pTool.y.ToString("F4");
        currentTrial.metadata["ToolPosZ"] = pTool.z.ToString("F4");
        
        currentTrial.metadata["E_3D"] = e3D.ToString("F4");
        currentTrial.metadata["E_X"] = eX.ToString("F4");
        currentTrial.metadata["E_Y"] = eY.ToString("F4");
        currentTrial.metadata["E_Z"] = eZ.ToString("F4");
        currentTrial.metadata["IsSuccess"] = isSuccess.ToString();
        
        AppLogger.Log(this, $"Pointing決定: E_3D={e3D:F4}m, E_Z={eZ:F4}m, Success={isSuccess}");
    }

    /// <summary>
    /// 試行終了時の後片付け
    /// </summary>
    private void HandleTrialCompleted(EXP_TrialData trial)
    {
        if (currentTargetInstance != null)
        {
            Destroy(currentTargetInstance);
            currentTargetInstance = null;
        }
        currentCondition = null;
        currentTrial = null;
    }
}
