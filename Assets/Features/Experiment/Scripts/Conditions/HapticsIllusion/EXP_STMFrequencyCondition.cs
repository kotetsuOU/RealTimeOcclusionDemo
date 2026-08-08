using UnityEngine;

#nullable enable

/// <summary>
/// 【実験条件】STM 周波数の知覚重さ比較（2AFC）。
/// <see cref="EXP_Base2AFCCondition"/> を継承し、周波数 (Hz) に特化した適用ロジックのみを定義します。
/// </summary>
[CreateAssetMenu(fileName = "STMFrequencyCondition", menuName = "EXP/Conditions/STMFrequencyCondition")]
public class EXP_STMFrequencyCondition : EXP_Base2AFCCondition
{
    [Header("Frequency Settings")]
    [Tooltip("基準刺激の STM 周波数 [Hz]（ReferenceVsComparison モードで使用）")]
    [Min(1f)]
    public float referenceFrequency = 80f;

    [Tooltip("固定の比較刺激 STM 周波数 [Hz]（FixedPair モードで使用）")]
    [Min(1f)]
    public float comparisonFrequency = 120f;

    [Tooltip("周波数候補のリスト [Hz]（ReferenceVsComparison / RandomPair モードで使用）")]
    public float[] candidateFrequencies = new float[] { 20f, 40f, 60f, 80f, 100f, 120f, 140f, 160f };

    // =====================================================
    // EXP_Base2AFCCondition Overrides
    // =====================================================

    protected override float GetReferenceValue() => referenceFrequency;

    protected override float GetFixedComparisonValue() => comparisonFrequency;

    protected override float[] GetCandidateValues() => candidateFrequencies;

    protected override string FormatValueForDebug(float value) => $"{value:F0} Hz";

    protected override void ApplyValueToController(HAP_HapticsIllusionFoxFootController ctrl, float frequency)
    {
        ctrl.stmFrequency = frequency;
        ctrl.sequentialSTMFrequency = frequency;

        if (ctrl.autdController != null)
        {
            ctrl.autdController.stmFrequency = frequency;
        }

        var mainController = Object.FindAnyObjectByType<HAP_AUTDHapticsController>();
        if (mainController != null)
        {
            mainController.stmFrequency = frequency;
        }
    }

    protected override void ResetValueOnTrialEnd(HAP_HapticsIllusionFoxFootController ctrl)
    {
        ApplyValueToController(ctrl, referenceFrequency);
    }
}
