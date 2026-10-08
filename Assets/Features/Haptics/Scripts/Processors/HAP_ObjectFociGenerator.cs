using UnityEngine;
using System.Collections.Generic;

#nullable enable

/// <summary>
/// HAP_BaseObjectHapticsController（足、尻尾、関節などのオブジェクト部位ターゲット）から
/// 焦点データ（ClusterFociData）やシーケンシャルSTMフレームを生成する専用の純粋計算クラスです。
/// </summary>
public static class HAP_ObjectFociGenerator
{
    /// <summary>
    /// オブジェクトコントローラーの設定とターゲット一覧から焦点データリストを生成します。
    /// </summary>
    public static List<HAP_FociGenerator.ClusterFociData> Generate(
        HAP_BaseObjectHapticsController controller,
        float defaultIntensityPascal,
        Vector3 offset)
    {
        var result = new List<HAP_FociGenerator.ClusterFociData>();
        if (controller == null) return result;

        bool useCustomCycle = controller.autdController != null 
            && (controller.stmMode == HapticsSTMMode.FociSTM || (controller.stmMode == HapticsSTMMode.GainSTM && controller.trackMode == HapticsTrackMode.Sequential));

        if (useCustomCycle)
        {
            var activeCandidates = new List<HapticsTargetInfo>();
            foreach (var info in controller.TargetInfos)
            {
                if (info.Transform != null && controller.IsTargetActive(info.Transform, info.IsEnabled, info.IsTail))
                {
                    activeCandidates.Add(info);
                }
            }

            if (activeCandidates.Count > 0)
            {
                TrackedCluster dummyCluster = new TrackedCluster
                {
                    Centroid = activeCandidates[0].Transform.position,
                    Normal = activeCandidates[0].TouchDirection.normalized,
                    Force = 1.0f,
                    IsAlive = true
                };

                var fociData = new HAP_FociGenerator.ClusterFociData(dummyCluster);
                fociData.UseSTM = true;
                fociData.IsGainSTM = (controller.stmMode == HapticsSTMMode.GainSTM);
                fociData.STMFrequency = controller.sequentialSTMFrequency;

                foreach (var info in activeCandidates)
                {
                    Vector3 pos = info.Transform.position;
                    fociData.STMFrames.Add(new List<Vector3> { 
                        new Vector3(pos.x + offset.x, pos.y + offset.y, pos.z + offset.z) 
                    });
                }
                
                result.Add(fociData);
            }
        }
        else
        {
            foreach (var info in controller.TargetInfos)
            {
                if (info.Transform == null) continue;
                if (!controller.IsTargetActive(info.Transform, info.IsEnabled, info.IsTail)) continue;

                Vector3 pos = info.Transform.position;
                TrackedCluster dummyCluster = new TrackedCluster
                {
                    Centroid = pos,
                    Normal = info.TouchDirection.normalized,
                    Force = 1.0f,
                    IsAlive = true
                };

                var fociData = new HAP_FociGenerator.ClusterFociData(dummyCluster);

                fociData.SequentialFoci.Add(new HAP_FocusPoint(pos + offset, defaultIntensityPascal));

                result.Add(fociData);
            }
        }

        return result;
    }
}
