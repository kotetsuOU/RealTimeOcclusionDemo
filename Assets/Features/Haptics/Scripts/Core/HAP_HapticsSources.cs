using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Haptics の各種 Source 共通の振幅算出モード
/// </summary>
public enum HapticsForceAmplitudeMode
{
    Constant,
    VectorSum,
    VectorMean,
    MagnitudeSum,
    MagnitudeMean
}

public enum HapticsOutputMode
{
    FociStm,
    Sequential
}

[System.Serializable]
public class HapticsModulationOverride
{
    public bool enabled = false;
    public int priority = 0;
    public ModulationMode mode = ModulationMode.Sine;
    public float frequency = 150f;
}

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
    /// 共分散行列からPCA（主成分分析）を行い、接触面の楕円形状を推定してSTMフレームを生成します。
    /// out float amplitudeScale は速度による強度補正値を返します。
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
            
            // 周波数スケールが高い = 1サイクルを早く終わらせる = サンプル数を減らす
            float freqScale = Mathf.Max(0.1f, frequencyScaleBySpeed.Evaluate(speedNormalized));
            samples = Mathf.Clamp(Mathf.RoundToInt(samples / freqScale), 10, 1000);
        }

        // GPUから読み戻した共分散行列の要素
        float cXX = c.Precision.covXX, cYY = c.Precision.covYY, cZZ = c.Precision.covZZ;
        float cXY = c.Precision.covXY, cXZ = c.Precision.covXZ, cYZ = c.Precision.covYZ;

        // 接触面の法線に直交する平面上でPCAを行うため、初期ベクトルを法線と直交させる
        Vector3 v1 = new Vector3(1, 0, 0);
        if (Mathf.Abs(Vector3.Dot(v1, c.Normal)) > 0.9f) v1 = new Vector3(0, 1, 0);
        v1 = Vector3.ProjectOnPlane(v1, c.Normal).normalized;
        
        // Power Iteration (冪乗法) により最大固有値に対応する固有ベクトル（主軸）を求める
        for (int i = 0; i < 3; i++)
        {
            float nx = cXX * v1.x + cXY * v1.y + cXZ * v1.z;
            float ny = cXY * v1.x + cYY * v1.y + cYZ * v1.z;
            float nz = cXZ * v1.x + cYZ * v1.y + cZZ * v1.z;
            v1 = Vector3.ProjectOnPlane(new Vector3(nx, ny, nz), c.Normal).normalized;
            if (v1.sqrMagnitude < 0.0001f) break;
        }
        
        // 副軸は法線と主軸の両方に直交するベクトル
        Vector3 v2 = Vector3.Cross(c.Normal, v1).normalized;

        // レイリー商から分散（固有値）を求める
        float lambda1 = Vector3.Dot(v1, new Vector3(
            cXX * v1.x + cXY * v1.y + cXZ * v1.z,
            cXY * v1.x + cYY * v1.y + cYZ * v1.z,
            cXZ * v1.x + cYZ * v1.y + cZZ * v1.z
        ));
        float lambda2 = Vector3.Dot(v2, new Vector3(
            cXX * v2.x + cXY * v2.y + cXZ * v2.z,
            cXY * v2.x + cYY * v2.y + cYZ * v2.z,
            cXZ * v2.x + cYZ * v2.y + cZZ * v2.z
        ));

        // 半径は標準偏差（分散の平方根）に比例
        float r1 = Mathf.Sqrt(Mathf.Max(0, lambda1 / c.ContactCount)) * ellipseScale;
        float r2 = Mathf.Sqrt(Mathf.Max(0, lambda2 / c.ContactCount)) * ellipseScale;

        // 楕円をなぞるSTMフレームを生成
        for (int i = 0; i < samples; i++)
        {
            float theta = (float)i / samples * Mathf.PI * 2.0f;
            Vector3[] foci = new Vector3[focusCount];
            for (int f = 0; f < focusCount; f++)
            {
                float angleOffset = (float)f / focusCount * Mathf.PI * 2.0f;
                float currentTheta = theta + angleOffset;
                Vector3 localPos = v1 * (r1 * Mathf.Cos(currentTheta)) + v2 * (r2 * Mathf.Sin(currentTheta));
                foci[f] = c.Centroid + localPos + offset;
            }
            frames.Add(foci);
        }
        
        return frames;
    }
}

[System.Serializable]
public class HAP_HapticsRandomSource
{
    [Tooltip("有効にすると、GPUでサンプリングされた接触面内のランダムな座標を用いて、不規則に飛び回るSTMフレーム（ザラザラとしたノイズ感）を生成します。")]
    public bool enabled = false;
    
    [Tooltip("STMの1サイクルあたりのサンプル（フレーム）数。この数だけランダム座標間を高速移動します。")]
    public int stmSamplesPerCycle = 100;

    [Header("Output Mode")]
    [Tooltip("出力方式。FociStm(ハードウェア再生) か Sequential(毎フレーム1点描画)")]
    public HapticsOutputMode outputMode = HapticsOutputMode.FociStm;
    
    [Header("Modulation Override")]
    public HapticsModulationOverride modulationOverride = new HapticsModulationOverride();
    
    /// <summary>
    /// GPUでサンプリングされたランダムポイントを利用してノイズ状のSTMフレームを生成します。
    /// </summary>
    public List<Vector3[]> GenerateSTMFrames(TrackedCluster c, Vector3 offset)
    {
        var frames = new List<Vector3[]>();
        if (!enabled) return frames;
        
        Vector3[] rps = new Vector3[17];
        rps[0] = c.Centroid;
        rps[1] = c.Precision.rp00; rps[2] = c.Precision.rp01; rps[3] = c.Precision.rp02; rps[4] = c.Precision.rp03;
        rps[5] = c.Precision.rp04; rps[6] = c.Precision.rp05; rps[7] = c.Precision.rp06; rps[8] = c.Precision.rp07;
        rps[9] = c.Precision.rp08; rps[10] = c.Precision.rp09; rps[11] = c.Precision.rp10; rps[12] = c.Precision.rp11;
        rps[13] = c.Precision.rp12; rps[14] = c.Precision.rp13; rps[15] = c.Precision.rp14; rps[16] = c.Precision.rp15;

        for (int i = 0; i < stmSamplesPerCycle; i++)
        {
            // 17点（重心+16ランダム点）からランダムに選んでSTMの1フレームとする
            int r = Random.Range(0, 17);
            Vector3 point = rps[r];
            
            // 安全のためゼロベクトル（初期状態）なら重心にフォールバック
            if (point.sqrMagnitude < 0.0001f) point = c.Centroid;
            
            frames.Add(new Vector3[] { point + offset });
        }
        
        return frames;
    }
}
