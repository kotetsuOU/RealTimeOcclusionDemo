// =============================================================================
// PCDResourceRequirements.cs
// -----------------------------------------------------------------------------
// 現在の設定から「このフレームで必要となる任意テクスチャ群」を判定する値型。
// PCDResourcePool はこの判定結果に従って、デバッグ用マップやホールフィリング手法別の
// 作業バッファを確保・バインドする。
// =============================================================================

/// <summary>
/// フレームごとに必要となる任意リソースの判定結果。
/// </summary>
internal readonly struct PCDResourceRequirements
{
    /// <summary> デバッグ表示マップ（DebugDisplayMap）が必要か </summary>
    public readonly bool DebugDisplay;

    /// <summary> オクルージョンデバッグ記録（OcclusionValueMap / NeighborCountMap）が必要か </summary>
    public readonly bool OcclusionDebug;

    /// <summary> 統合深度マップの記録が必要か </summary>
    public readonly bool IntegratedDepth;

    /// <summary> 近傍サイズマップの記録が必要か </summary>
    public readonly bool NeighborhoodRecord;

    /// <summary> Pull-Push ホールフィリング用ピラミッドが必要か </summary>
    public readonly bool PullPush;

    /// <summary> モルフォロジーホールフィリング用バッファが必要か </summary>
    public readonly bool Morphology;

    private PCDResourceRequirements(bool debugDisplay, bool occlusionDebug, bool integratedDepth,
        bool neighborhoodRecord, bool pullPush, bool morphology)
    {
        DebugDisplay = debugDisplay;
        OcclusionDebug = occlusionDebug;
        IntegratedDepth = integratedDepth;
        NeighborhoodRecord = neighborhoodRecord;
        PullPush = pullPush;
        Morphology = morphology;
    }

    /// <summary>
    /// レンダー設定から必要リソースを判定する。
    /// 判定条件は各ステージ（PCDOcclusionStage / PCDPostProcessStage / PCDBlitPassBuilder）の分岐と一致させること。
    /// </summary>
    public static PCDResourceRequirements FromSettings(in PCDRendererFeature.PCDRenderSettings s)
    {
        bool debugDisplay = s.enablePixelTagMap || s.enableOcclusionMap
            || (s.debugPatternId >= 0 && s.debugPatternId < 256)
            || (s.debugSectorId >= 0 && s.debugSectorId <= 10);

        // SICESI セクターマスク収集中は NeighborCountMap を外部から読み戻すため常に有効化する
        bool isSectorMaskSweep = SICESI.SICESI_SectorMaskCollector.IsCollectingActive;

        bool occlusionDebug = debugDisplay
            || s.recordOcclusionDebugMap || s.recordPixelTagMap || s.recordNeighborCountMap
            || isSectorMaskSweep;

        var method = s.holeFillingMethod;
        bool pullPush = method == PCDRendererFeature.PCD_HoleFillingMethod.PullPush;
        bool morphology = method == PCDRendererFeature.PCD_HoleFillingMethod.Morphology_OC
                       || method == PCDRendererFeature.PCD_HoleFillingMethod.Morphology_CO;

        return new PCDResourceRequirements(
            debugDisplay,
            occlusionDebug,
            s.recordIntegratedDepthMap,
            s.recordNeighborhoodMap,
            pullPush,
            morphology);
    }

    /// <summary> すべてのリソースを必要とする安全フォールバック </summary>
    public static PCDResourceRequirements All => new PCDResourceRequirements(true, true, true, true, true, true);
}
