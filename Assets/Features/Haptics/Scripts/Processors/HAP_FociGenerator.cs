using System.Collections.Generic;
using UnityEngine;

#nullable enable

/// <summary>
/// 接触点（TrackedCluster）と各種設定を受け取り、
/// 空間的な焦点座標（Foci）やSTMフレームのリストを生成する純粋な計算クラスです。
/// AUTD3 SDK に依存しない <see cref="HAP_FocusPoint"/> のみを出力し、SDK 固有型への変換は送信側で行います。
/// </summary>
public static class HAP_FociGenerator
{
    public class ClusterFociData
    {
        public TrackedCluster Cluster;
        public List<HAP_FocusPoint> SequentialFoci = new List<HAP_FocusPoint>();
        public List<List<Vector3>> STMFrames = new List<List<Vector3>>();
        public bool UseSTM;
        public bool IsGainSTM;
        public float STMFrequency = 150f;
        public int AssignedDeviceIndex = -1;
        public List<int> AssignedDeviceIndices = new List<int>();

        public ClusterFociData(TrackedCluster cluster)
        {
            Cluster = cluster;
        }
    }

    /// <summary>
    /// 有効なクラスタ群から、それぞれの焦点データを計算して返します。
    /// </summary>
    public static List<ClusterFociData> Generate(
        List<TrackedCluster> activeClusters,
        HapticsGenerationMode generationMode,
        HAP_HapticsCentroidSource centroidSource,
        HAP_HapticsEllipseSource ellipseSource,
        HAP_HapticsRandomSource randomSource,
        float focusIntensityPascal,
        Vector3 offset,
        HapticsSTMMode stmMode = HapticsSTMMode.FociSTM,
        float stmFrequency = 150f)
    {
        var result = new List<ClusterFociData>();

        foreach (var c in activeClusters)
        {
            var data = new ClusterFociData(c);
            data.IsGainSTM = (stmMode == HapticsSTMMode.GainSTM);
            data.STMFrequency = stmFrequency;

            // 【Simplified モード】
            if (generationMode == HapticsGenerationMode.Simplified)
            {
                data.SequentialFoci.Add(new HAP_FocusPoint(c.Centroid + offset, focusIntensityPascal * c.Force));
                result.Add(data);
                continue;
            }

            // 【Precision モード】
            bool useStm = ellipseSource.enabled || randomSource.enabled;
            data.UseSTM = useStm;

            if (!useStm)
            {
                // STMを使用せず Centroid だけが有効な場合→静的Holoとして出力
                data.SequentialFoci.Add(new HAP_FocusPoint(c.Centroid + offset, centroidSource.CalculateAmplitude(c)));
            }
            else
            {
                // STMサンプルの最大数を決定
                int maxStmSamples = 1;
                if (ellipseSource.enabled && ellipseSource.outputMode == HapticsOutputMode.FociStm)
                    maxStmSamples = Mathf.Max(maxStmSamples, ellipseSource.stmSamplesPerCycle);
                if (randomSource.enabled && randomSource.outputMode == HapticsOutputMode.FociStm)
                    maxStmSamples = Mathf.Max(maxStmSamples, randomSource.stmSamplesPerCycle);

                for (int i = 0; i < maxStmSamples; i++) data.STMFrames.Add(new List<Vector3>());

                // 1. Centroid Source の処理
                if (centroidSource.enabled)
                {
                    data.SequentialFoci.Add(new HAP_FocusPoint(c.Centroid + offset, centroidSource.CalculateAmplitude(c)));
                }

                // 2. Ellipse Source の処理
                if (ellipseSource.enabled)
                {
                    float ellipseAmpScale;
                    var eFrames = ellipseSource.GenerateSTMFrames(c, offset, out ellipseAmpScale);

                    if (ellipseSource.outputMode == HapticsOutputMode.Sequential)
                    {
                        int idx = Time.frameCount % eFrames.Count;
                        foreach (var p in eFrames[idx])
                        {
                            data.SequentialFoci.Add(new HAP_FocusPoint(p, focusIntensityPascal * c.Force * ellipseAmpScale));
                        }
                    }
                    else
                    {
                        for (int i = 0; i < eFrames.Count; i++)
                        {
                            int targetIdx = Mathf.RoundToInt((float)i / eFrames.Count * (maxStmSamples - 1));
                            data.STMFrames[targetIdx].AddRange(eFrames[i]);
                        }
                    }
                }

                // 3. Random Source の処理
                if (randomSource.enabled)
                {
                    var rFrames = randomSource.GenerateSTMFrames(c, offset);
                    if (randomSource.outputMode == HapticsOutputMode.Sequential)
                    {
                        int idx = Time.frameCount % rFrames.Count;
                        foreach (var p in rFrames[idx])
                        {
                            data.SequentialFoci.Add(new HAP_FocusPoint(p, focusIntensityPascal * c.Force));
                        }
                    }
                    else
                    {
                        for (int i = 0; i < rFrames.Count; i++)
                        {
                            int targetIdx = Mathf.RoundToInt((float)i / rFrames.Count * (maxStmSamples - 1));
                            data.STMFrames[targetIdx].AddRange(rFrames[i]);
                        }
                    }
                }

                // STMを使う場合、STMの全フレームに対して Sequential な焦点（Centroidなど）を合成する
                for (int i = 0; i < maxStmSamples; i++)
                {
                    if (data.STMFrames[i].Count == 0)
                        data.STMFrames[i].Add(c.Centroid + offset);

                    foreach (var sf in data.SequentialFoci)
                    {
                        data.STMFrames[i].Add(sf.Position);
                    }
                }
            }

            result.Add(data);
        }

        return result;
    }
}
