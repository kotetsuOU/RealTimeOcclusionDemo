using UnityEngine;
using Core.Logging;

#nullable enable

/// <summary>
/// ターゲット位置のカテゴリ
/// </summary>
public enum TargetPlacementType
{
    Front,
    Behind,
    Side
}

[CreateAssetMenu(menuName = "EXP/Conditions/PointingCondition")]
public class EXP_PointingCondition : EXP_BaseInteractiveTaskCondition
{
    [Header("Pointing Task Settings")]
    [Tooltip("提案手法（オクルージョンあり）か従来手法（なし）か")]
    public bool useOcclusion = true;

    [Tooltip("基準点からの仮想ターゲットの相対座標")]
    public Vector3 targetLocalPosition = Vector3.zero;

    [Tooltip("ターゲットの位置カテゴリ。データ分析時に利用します。")]
    public TargetPlacementType placementType = TargetPlacementType.Behind;

    [Tooltip("成功とみなす判定半径（メートル）")]
    public float successRadius = 0.01f;

    [Tooltip("表示する仮想ターゲットのプレハブ")]
    public GameObject? virtualTargetPrefab;

    public override string ParadigmType => "3DPointing";

    /// <summary>
    /// この条件専用の実行処理（コントローラに処理を委譲します）
    /// </summary>
    protected override System.Collections.IEnumerator ExecuteTaskCoroutine(EXP_TrialData trial, MonoBehaviour runner)
    {
        // 実際の処理はシーン上の EXP_PointingController に委譲します
        var controller = Object.FindFirstObjectByType<EXP_PointingController>();
        if (controller == null)
        {
            AppLogger.LogError(this, "SceneにEXP_PointingControllerが見つかりません。");
            yield break;
        }

        // コントローラ側でタスクを実行し、完了を待つ
        yield return runner.StartCoroutine(controller.ExecutePointingTask(this, trial));
    }
}
