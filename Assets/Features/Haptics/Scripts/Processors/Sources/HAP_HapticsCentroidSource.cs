using UnityEngine;

/// <summary>
/// 接触クラスタの重心に対して基本となる単焦点振幅を算出するハプティクスソース。
/// </summary>
[System.Serializable]
public class HAP_HapticsCentroidSource
{
    [Tooltip("有効にすると、接触クラスタの重心に対して基本となる焦点を生成します。")]
    public bool enabled = true;

    [Tooltip("振幅（出力の強さ）を計算するアルゴリズムを指定します。Force(接触面積)とNormal(法線)を加味して決定できます。")]
    public HapticsForceAmplitudeMode amplitudeMode = HapticsForceAmplitudeMode.MagnitudeSum;

    [Tooltip("Constant モード時の固定振幅値、または他のモードのベースとなる振幅値です。")]
    public float amplitudeConstant = 1.0f;

    [Tooltip("計算された振幅値に対する最終的なスケーリング係数（掛け率）です。")]
    public float amplitudeScale = 1.0f;

    [Header("Modulation Override")]
    public HapticsModulationOverride modulationOverride = new HapticsModulationOverride();

    /// <summary>
    /// 接触クラスタ情報から重心焦点の振幅値を算出します。
    /// </summary>
    public float CalculateAmplitude(TrackedCluster c)
    {
        if (!enabled) return 0f;

        switch (amplitudeMode)
        {
            case HapticsForceAmplitudeMode.Constant:
                return amplitudeConstant;
            case HapticsForceAmplitudeMode.VectorSum:
                return (c.Normal * c.Force).magnitude * amplitudeScale;
            case HapticsForceAmplitudeMode.VectorMean:
                return (c.Normal * c.Force).magnitude / Mathf.Max(1, c.ContactCount) * amplitudeScale;
            case HapticsForceAmplitudeMode.MagnitudeSum:
                return c.Force * amplitudeScale;
            case HapticsForceAmplitudeMode.MagnitudeMean:
                return c.Force / Mathf.Max(1, c.ContactCount) * amplitudeScale;
            default:
                return amplitudeConstant;
        }
    }
}

