using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GPU 共分散行列から主成分分析 (PCA) を行い、接触面形状に沿った楕円状 STM フレームを生成するハプティクスソース。
/// </summary>
[System.Serializable]
public class HAP_HapticsEllipseSource
{
    [Tooltip("有効にすると、GPUで計算された共分散行列から主成分分析(PCA)を行い、接触面の形状にフィットする楕円状のSTM（なぞる感覚）を提示します。")]
    public bool enabled = false;

    [Tooltip("STMの1サイクルあたりのサンプル（フレーム）数。多いほど滑らかですが処理負荷とデータ転送量が増加します。")]
    public int stmSamplesPerCycle = 100;

    [Tooltip("推定された楕円半径に掛けるスケール。大きすぎるとデバイスのフォーカス範囲外に飛び出す可能性があります。")]
    public float ellipseScale = 1.0f;

    [Tooltip("楕円周上に同時に配置するフォーカス数。2以上にすると複数の焦点が同時に楕円を描きます。")]
    public int focusCount = 1;

    [Header("Output Mode")]
    [Tooltip("出力方式。FociStm(ハードウェア再生) か Sequential(毎フレーム1点描画)")]
    public HapticsOutputMode outputMode = HapticsOutputMode.FociStm;

    [Header("Modulation Override")]
    public HapticsModulationOverride modulationOverride = new HapticsModulationOverride();

    [Header("Velocity Estimation")]
    [Tooltip("有効にすると、手の移動速度に応じて振幅や周波数をスケーリングします。")]
    public bool useVelocityEstimation = false;

    [Tooltip("正規化された速度（0〜1）を基準に、STMの周波数（実質的なサンプル数）を増減させるカーブ")]
    public AnimationCurve frequencyScaleBySpeed = AnimationCurve.Linear(0, 1, 1, 2);

    [Tooltip("正規化された速度（0〜1）を基準に、出力強度（Amplitude）を増減させるカーブ")]
    public AnimationCurve intensityScaleBySpeed = AnimationCurve.Linear(0, 1, 1, 2);

    [Tooltip("速度カーブのX軸（1.0）となる基準速度 (m/s)")]
    public float curveReferenceVelocity = 1.0f;

    /// <summary>
    /// 共分散行列から PCA（主成分分析）を行い、接触面の楕円形状を推定して STM フレームを生成します。
    /// </summary>
    public List<Vector3[]> GenerateSTMFrames(TrackedCluster c, Vector3 offset, out float amplitudeScale)
    {
        amplitudeScale = 1.0f;
        var frames = new List<Vector3[]>();
        if (!enabled || c.ContactCount <= 1) return frames;

        int samples = stmSamplesPerCycle;
        if (useVelocityEstimation)
        {
            float speedNormalized = Mathf.Clamp01(c.Velocity.magnitude / curveReferenceVelocity);
            amplitudeScale = intensityScaleBySpeed.Evaluate(speedNormalized);

            // 周波数スケールが高いほど 1 サイクルあたりのサンプル数を減らして回転速度を上げる
            float freqScale = Mathf.Max(0.1f, frequencyScaleBySpeed.Evaluate(speedNormalized));
            samples = Mathf.Clamp(Mathf.RoundToInt(samples / freqScale), 10, 1000);
        }

        float cXX = c.Precision.covXX, cYY = c.Precision.covYY, cZZ = c.Precision.covZZ;
        float cXY = c.Precision.covXY, cXZ = c.Precision.covXZ, cYZ = c.Precision.covYZ;

        Vector3 v1 = new Vector3(1, 0, 0);
        if (Mathf.Abs(Vector3.Dot(v1, c.Normal)) > 0.9f) v1 = new Vector3(0, 1, 0);
        v1 = Vector3.ProjectOnPlane(v1, c.Normal).normalized;

        // 冪乗法により接触法線に直交する平面上の主軸ベクトルを求める
        for (int i = 0; i < 3; i++)
        {
            float nx = cXX * v1.x + cXY * v1.y + cXZ * v1.z;
            float ny = cXY * v1.x + cYY * v1.y + cYZ * v1.z;
            float nz = cXZ * v1.x + cYZ * v1.y + cZZ * v1.z;
            v1 = Vector3.ProjectOnPlane(new Vector3(nx, ny, nz), c.Normal).normalized;
            if (v1.sqrMagnitude < 0.0001f) break;
        }

        Vector3 v2 = Vector3.Cross(c.Normal, v1).normalized;

        float lambda1 = ComputeRayleighQuotient(v1, cXX, cYY, cZZ, cXY, cXZ, cYZ);
        float lambda2 = ComputeRayleighQuotient(v2, cXX, cYY, cZZ, cXY, cXZ, cYZ);

        float r1 = Mathf.Sqrt(Mathf.Max(0, lambda1 / c.ContactCount)) * ellipseScale;
        float r2 = Mathf.Sqrt(Mathf.Max(0, lambda2 / c.ContactCount)) * ellipseScale;

        for (int i = 0; i < samples; i++)
        {
            float theta = (float)i / samples * Mathf.PI * 2.0f;
            Vector3[] foci = new Vector3[focusCount];
            for (int f = 0; f < focusCount; f++)
            {
                float currentTheta = theta + (float)f / focusCount * Mathf.PI * 2.0f;
                Vector3 localPos = v1 * (r1 * Mathf.Cos(currentTheta)) + v2 * (r2 * Mathf.Sin(currentTheta));
                foci[f] = c.Centroid + localPos + offset;
            }
            frames.Add(foci);
        }

        return frames;
    }

    private static float ComputeRayleighQuotient(Vector3 axis, float cXX, float cYY, float cZZ, float cXY, float cXZ, float cYZ)
    {
        return Vector3.Dot(axis, new Vector3(
            cXX * axis.x + cXY * axis.y + cXZ * axis.z,
            cXY * axis.x + cYY * axis.y + cYZ * axis.z,
            cXZ * axis.x + cYZ * axis.y + cZZ * axis.z));
    }
}

