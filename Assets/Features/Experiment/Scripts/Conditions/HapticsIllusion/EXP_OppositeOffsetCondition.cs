using UnityEngine;

#nullable enable

/// <summary>
/// 【実験条件】OppositeOffset Y 値の知覚重さ比較（2AFC）。
/// <see cref="EXP_Base2AFCCondition"/> を継承し、Y オフセット (m/cm) に特化した適用ロジックのみを定義します。
/// </summary>
[CreateAssetMenu(fileName = "OppositeOffsetCondition", menuName = "EXP/Conditions/OppositeOffsetCondition")]
public class EXP_OppositeOffsetCondition : EXP_Base2AFCCondition
{
    [Header("Offset Settings")]
    [Tooltip("基準刺激の OppositeOffset Y 値 [m]（ReferenceVsComparison モードで使用、例: 0.03 = 3cm）")]
    public float referenceOffsetY = 0.03f;

    [Tooltip("固定の比較刺激 OppositeOffset Y 値 [m]（FixedPair モードで使用）")]
    public float comparisonOffsetY = 0.0f;

    [Tooltip("Y オフセット候補のリスト [m]（ReferenceVsComparison / RandomPair モードで使用）\n（例: -0.04 〜 0.02 m）")]
    public float[] candidateOffsetsY = new float[] { -0.04f, -0.03f, -0.02f, -0.01f, 0.0f, 0.01f, 0.02f };

    // =====================================================
    // EXP_Base2AFCCondition Overrides
    // =====================================================

    protected override float GetReferenceValue() => referenceOffsetY;

    protected override float GetFixedComparisonValue() => comparisonOffsetY;

    protected override float[] GetCandidateValues() => candidateOffsetsY;

    protected override string FormatValueForDebug(float value) => $"Y: {value * 100:F1} cm";

    protected override void ApplyValueToController(HAP_HapticsIllusionFoxFootController ctrl, float offsetY)
    {
        var off = ctrl.oppositeOffset;
        off.y = offsetY;
        ctrl.oppositeOffset = off;
    }

    protected override void ResetValueOnTrialEnd(HAP_HapticsIllusionFoxFootController ctrl)
    {
        ApplyValueToController(ctrl, referenceOffsetY);
    }
}
