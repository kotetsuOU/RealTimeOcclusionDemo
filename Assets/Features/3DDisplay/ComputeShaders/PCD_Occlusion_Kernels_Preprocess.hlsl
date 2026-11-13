#ifndef PCD_OCCLUSION_KERNELS_PREPROCESS_INCLUDED
#define PCD_OCCLUSION_KERNELS_PREPROCESS_INCLUDED

// 0. Merge Buffer
// 複数の点群のバッファを結合して1つのリストを作成するためのユーティリティカーネル
[numthreads(256, 1, 1)]
void MergeBuffer(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _MergeCopyCount)
        return;
    _MergeDstBuffer[_MergeDstOffset + id.x] = _MergeSrcBuffer[_MergeSrcOffset + id.x];
}

// 1. Clear Maps
// 描画ごとにすべての出力・計算用バッファをクリアして初期状態（背景や最大深度）に設定
[numthreads(8, 8, 1)]
void ClearMaps(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    _ColorMap_RW[id.xy] = float4(0, 0, 0, 0);
    _DepthMap_RW[id.xy] = DEPTH_MAX_UINT;
    _ViewPositionMap_RW[id.xy] = float4(0, 0, 0, SENTINEL_DEPTH);
    
    // 以下のテクスチャは後続のカーネルで全ピクセルが上書きされるため、クリアを省略して高速化
    // _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0);
    // _OcclusionValueMap_RW[id.xy] = float2(0.0, 0.0);
    // _FinalImage_RW[id.xy] = float4(0, 0, 0, 0);

    if (_EnableDebugDisplay > 0)
    {
        _OriginMap_RW[id.xy] = float4(0, 0, 0, 1);
    }
    _OriginTypeMap_RW[id.xy] = 2u; // 2 = Background
}

// 1.5 Clear Counter
// オーバーフローを防ぐためやUAV制限のために別カーネルでカウンターをリセット
[numthreads(1, 1, 1)]
void ClearCounter(uint3 id : SV_DispatchThreadID)
{
    if (id.x == 0)
    {
        _StaticMeshCounter_RW[0] = 0;
    }
}

// 2. Project Points
// 3D空間上の点群を行列変換し、スクリーン上のピクセル(2D)へ投影。
// InterlockedMin を用いてアトミック(排他)に最もカメラに近接した点の情報(色・深度)を記録する
[numthreads(256, 1, 1)]
void ProjectPoints(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= _PointCount)
        return;

    Point p = _PointBuffer[id.x];
    float4 worldPos = float4(p.position, 1.0);
    float4 clipPos = mul(_ViewProjMatrix, worldPos);

    if (clipPos.w <= 0.0)
        return; // 【重要】背後の点群が画面手前に反転ワープしてデプスバッファを埋め尽くすのを防ぐ

    float3 ndc = clipPos.xyz / clipPos.w;
    if (ndc.x < -1 || ndc.x > 1 || ndc.y < -1 || ndc.y > 1 || ndc.z < 0 || ndc.z > 1)
        return;

    // 視錐台内の点のみビュー空間座標を計算する
    float4 viewPos = mul(_ViewMatrix, worldPos);

    uint2 screenUV = uint2((ndc.xy * 0.5 + 0.5) * _ScreenParams.xy);
    float depth = ndc.z;
    
    if (_IsReversedZ > 0)
    {
        depth = 1.0 - depth;
    }
    
    uint depth_uint = (uint) (depth * (float) DEPTH_MAX_UINT);

    uint oldDepth;
    InterlockedMin(_DepthMap_RW[screenUV], depth_uint, oldDepth);

    if (depth_uint < oldDepth)
    {
        _ColorMap_RW[screenUV] = float4(p.color, 1.0);
        _ViewPositionMap_RW[screenUV] = float4(viewPos.xyz, depth);
        _OriginTypeMap_RW[screenUV] = p.originType;
    }
}

// 3+4. Calculate Z-Min, Density & Grid Level per Grid (統合カーネル)
// GRID_SIZE (デフォルト16x16) の領域ごとに最も手前の深度(Z-Min)を共有メモリで算出し、
// 続けて同じスレッドグループ内で有効な点群密度と近傍探索レベル(Level)を求めて _GridLevelMap_RW に格納する。
[numthreads(GRID_SIZE, GRID_SIZE, 1)]
void CalculateDensity(uint3 id : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint groupIndex : SV_GroupIndex)
{
    if (groupIndex == 0u)
    {
        shared_z_min = DEPTH_MAX_UINT;
        shared_point_count = 0u;
    }
    GroupMemoryBarrierWithGroupSync();

    bool inBounds = (id.x < (uint)_ScreenParams.x && id.y < (uint)_ScreenParams.y);
    uint depth_uint = inBounds ? _DepthMap[id.xy] : DEPTH_MAX_UINT;

    if (depth_uint < DEPTH_MAX_UINT)
    {
        InterlockedMin(shared_z_min, depth_uint);
    }
    GroupMemoryBarrierWithGroupSync();

    uint z_min_uint = shared_z_min;

    if (depth_uint < DEPTH_MAX_UINT)
    {
        uint originType = _OriginTypeMap[id.xy];
        uint diff = depth_uint - z_min_uint;
        uint threshold_uint = (uint)(_DensityThreshold_e * (float)DEPTH_MAX_UINT);
        if (diff < threshold_uint)
        {
            if (_EnableTypeAwareDensity == 0 || originType == 0u)
            {
                InterlockedAdd(shared_point_count, 1u);
            }
            if (_EnableTypeAwareDensity == 0 && originType == 1u)
            {
                uint safeMultiplier = min((uint)_StaticMeshDensityMultiplier, 1000u);
                InterlockedAdd(shared_point_count, safeMultiplier);
            }
        }
    }

    GroupMemoryBarrierWithGroupSync();

    if (groupIndex == 0u)
    {
        float density = float(shared_point_count) / float(GRID_SIZE * GRID_SIZE);
        int level = _MinSearchLevel;

        // 1点が存在するときの密度の半分を閾値とする（グリッドサイズ依存の動的閾値）
        float min_density_threshold = 0.5 / float(GRID_SIZE * GRID_SIZE);
        if (density > min_density_threshold)
        {
            float L = _NeighborhoodParam_p_prime / sqrt(density);
            level = min(6, (int) floor(log2(L))); // 最大レベル 6 でクランプしてサンプリング境界を保証
        }
        _GridLevelMap_RW[groupID.xy] = max(_MinSearchLevel, level);

        if (_UseDensityAdaptiveLUT > 0)
        {
            int count = clamp(_DensityLutCount, 1, 8);
            uint bin = (uint)(count - 1);
            [unroll]
            for (int i = 6; i >= 0; --i)
            {
                if (i < count - 1)
                {
                    float thresh = (i < 4) ? _DensityLutThresholds[0][i] : _DensityLutThresholds[1][i - 4];
                    if (density <= thresh)
                    {
                        bin = (uint)i;
                    }
                }
            }
            _GridDensityBinMap_RW[groupID.xy] = bin;
        }
    }
}

#define PCD_MEDIAN_SWAP(a, b) { int _tMin = min(values[a], values[b]); int _tMax = max(values[a], values[b]); values[a] = _tMin; values[b] = _tMax; }

// 6. Grid Median Filter
// グリッド単位で設定されたLevelマップに対し3x3のメディアンフィルタを適用し、
// 一部の異常値や点群のムラによる極端な近傍サイズの変化を平滑化する。
[numthreads(16, 16, 1)]
void GridMedianFilter(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _GridLevelMap.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;

    int values[9];
    values[0] = _GridLevelMap[clamp(id.xy + int2(-1, -1), 0, dim - 1)];
    values[1] = _GridLevelMap[clamp(id.xy + int2(0, -1), 0, dim - 1)];
    values[2] = _GridLevelMap[clamp(id.xy + int2(1, -1), 0, dim - 1)];
    values[3] = _GridLevelMap[clamp(id.xy + int2(-1, 0), 0, dim - 1)];
    values[4] = _GridLevelMap[clamp(id.xy + int2(0, 0), 0, dim - 1)];
    values[5] = _GridLevelMap[clamp(id.xy + int2(1, 0), 0, dim - 1)];
    values[6] = _GridLevelMap[clamp(id.xy + int2(-1, 1), 0, dim - 1)];
    values[7] = _GridLevelMap[clamp(id.xy + int2(0, 1), 0, dim - 1)];
    values[8] = _GridLevelMap[clamp(id.xy + int2(1, 1), 0, dim - 1)];

    // 9要素メディアン用の19比較分岐なしソーティングネットワーク (Paeth 1990)
    PCD_MEDIAN_SWAP(1, 2); PCD_MEDIAN_SWAP(4, 5); PCD_MEDIAN_SWAP(7, 8);
    PCD_MEDIAN_SWAP(0, 1); PCD_MEDIAN_SWAP(3, 4); PCD_MEDIAN_SWAP(6, 7);
    PCD_MEDIAN_SWAP(1, 2); PCD_MEDIAN_SWAP(4, 5); PCD_MEDIAN_SWAP(7, 8);
    PCD_MEDIAN_SWAP(0, 3); PCD_MEDIAN_SWAP(5, 8); PCD_MEDIAN_SWAP(4, 7);
    PCD_MEDIAN_SWAP(3, 6); PCD_MEDIAN_SWAP(1, 4); PCD_MEDIAN_SWAP(2, 5);
    PCD_MEDIAN_SWAP(4, 7); PCD_MEDIAN_SWAP(4, 2); PCD_MEDIAN_SWAP(6, 4);
    PCD_MEDIAN_SWAP(4, 2);

    _FilteredGridLevelMap_RW[id.xy] = values[4];
}

#undef PCD_MEDIAN_SWAP

// 7. Calculate Neighborhood Size
// ピクセル単位のスレッドが起動し、自身が属するグリッドのLevel(Neighborhood Size)を取得して
// ピクセル解像度用マップ(ピクセルごとにどのサイズのブロック探索を行うか)に書き込む。
[numthreads(8, 8, 1)]
void CalculateNeighborhoodSize(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    uint2 gridID = id.xy / GRID_SIZE;
    int level = _FilteredGridLevelMap[gridID];
    _NeighborhoodSizeMap_RW[id.xy] = max(_MinSearchLevel, level);
}

// 7.5 CopyColorToOcclusion (Skip Occlusion Kernel)
// オクルージョン判定を行わず、直接 FillHoles などに送るために _ColorMap を _OcclusionResultMap にコピーする
[numthreads(8, 8, 1)]
void CopyColorToOcclusion(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    // 背景(2u)も含めて全ピクセルをコピー(上書き)し、事前のClear処理を不要にする
    _OcclusionResultMap_RW[id.xy] = _ColorMap[id.xy];
}

// 7.6 Fill Neighborhood Size with Min Level (Density LOD disabled)
[numthreads(8, 8, 1)]
void FillNeighborhoodSizeWithMinLevel(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    _NeighborhoodSizeMap_RW[id.xy] = _MinSearchLevel;
}

#endif // PCD_OCCLUSION_KERNELS_PREPROCESS_INCLUDED