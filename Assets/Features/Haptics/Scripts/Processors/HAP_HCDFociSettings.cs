using System.Collections.Generic;
using UnityEngine;

#nullable enable

/// <summary>
/// HCD (Hand Contact Detection) から得られる接触クラスタをどのような焦点（単一重心 / 楕円 / ランダムノイズ）で生成するかを管理するコンポーネント。
/// </summary>
[DisallowMultipleComponent]
public class HAP_HCDFociSettings : MonoBehaviour
{
    [Header("Operation Settings")]
    [Tooltip("Simplified: 1クラスタ1点の単純出力(軽量)。\nPrecision: 楕円やランダムノイズなどリッチな表現を使用します。")]
    public HapticsGenerationMode generationMode = HapticsGenerationMode.Simplified;

    [Header("Precision Sources")]
    [Tooltip("接触領域の「重心」に対して基本的な超音波の焦点を生成するソース設定")]
    public HAP_HapticsCentroidSource centroidSource = new HAP_HapticsCentroidSource();

    [Tooltip("接触領域の「形状」を主成分分析(PCA)し、楕円状となぞるSTMを生成するソース設定")]
    public HAP_HapticsEllipseSource ellipseSource = new HAP_HapticsEllipseSource();

    [Tooltip("接触領域内でランダムに16点をサンプリングし、不規則に飛び回るSTM（ザラザラ感）を生成するソース設定")]
    public HAP_HapticsRandomSource randomSource = new HAP_HapticsRandomSource();

    /// <summary>
    /// 設定されている generationMode および precision sources を使用して焦点データを生成します。
    /// </summary>
    public List<HAP_FociGenerator.ClusterFociData> GenerateFoci(
        List<TrackedCluster> activeClusters,
        float focusIntensityPascal,
        Vector3 offset,
        HapticsSTMMode stmMode,
        float stmFrequency)
    {
        return HAP_FociGenerator.Generate(
            activeClusters,
            generationMode,
            centroidSource,
            ellipseSource,
            randomSource,
            focusIntensityPascal,
            offset,
            stmMode,
            stmFrequency);
    }
}
