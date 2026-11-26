using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GPU でサンプリングされた接触面内ランダム点群を用いてテクスチャ感（ノイズ状 STM）を生成するハプティクスソース。
/// </summary>
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
    /// GPU でサンプリングされたランダムポイントを利用してノイズ状の STM フレームを生成します。
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
            int r = Random.Range(0, 17);
            Vector3 point = rps[r];

            // ゼロベクトル（未初期化点）は重心にフォールバックする
            if (point.sqrMagnitude < 0.0001f) point = c.Centroid;

            frames.Add(new Vector3[] { point + offset });
        }

        return frames;
    }
}

