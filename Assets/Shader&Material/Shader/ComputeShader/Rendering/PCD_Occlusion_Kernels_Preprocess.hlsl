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
    _ViewPositionMap_RW[id.xy] = float4(0, 0, 0, 1e9);
    _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0);
    _OcclusionValueMap_RW[id.xy] = 0.0;
    _FinalImage_RW[id.xy] = float4(0, 0, 0, 0);

    _OriginMap_RW[id.xy] = float4(0, 0, 0, 1);
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
    float4 viewPos = mul(_ViewMatrix, worldPos);
    float4 clipPos = mul(_ProjectionMatrix, viewPos);

    float3 ndc = clipPos.xyz / clipPos.w;
    if (ndc.x < -1 || ndc.x > 1 || ndc.y < -1 || ndc.y > 1 || ndc.z < 0 || ndc.z > 1)
        return;

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

// 3. Calculate Z-Min per Grid
// GRID_SIZE (デフォルト16x16) の領域ごとに最も手前の深度(Z-Min)を算出
// ThreadGroupごとの共有メモリを用いて排他処理を高速化
[numthreads(GRID_SIZE, GRID_SIZE, 1)]
void CalculateGridZMin(uint3 id : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint groupIndex : SV_GroupIndex)
{
    if (groupIndex == 0u)
    {
        shared_z_min = DEPTH_MAX_UINT;
    }
    GroupMemoryBarrierWithGroupSync();

    uint depth_uint = _DepthMap[id.xy];

    if (depth_uint < DEPTH_MAX_UINT)
    {
        InterlockedMin(shared_z_min, depth_uint);
    }
    GroupMemoryBarrierWithGroupSync();

    if (groupIndex == 0u)
    {
        _GridZMinMap_RW[groupID.xy] = shared_z_min;
    }
}

// 4. Calculate Density per Grid
// 各グリッド内の有効な(手前の表面に近い)点群の密度(占有率)を計算する。
// 単純な個数ではなく、Z-Min + 閾値 の範囲内の点をカウントする。
[numthreads(GRID_SIZE, GRID_SIZE, 1)]
void CalculateDensity(uint3 id : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint groupIndex : SV_GroupIndex)
{
    if (groupIndex == 0u)
    {
        shared_point_count = 0u;
    }
    GroupMemoryBarrierWithGroupSync();

    uint z_min_uint = _GridZMinMap[groupID.xy];
    uint depth_uint = _DepthMap[id.xy];

    // OriginType fetch. 0 = PointCloud (Dynamic), 1 = StaticMesh, 2 = Background
    // NOTE: Density is generally used before Occlusion. Reading SRV here is okay as it hasn't mapped RW yet,
    // or if mapped, it's safer to avoid reading from the written texture if not required, but here it's read-only in this pass.
    uint originType = _OriginTypeMap[id.xy];

    if (depth_uint < DEPTH_MAX_UINT)
    {
        uint diff = depth_uint - z_min_uint;
        uint threshold_uint = (uint)(_DensityThreshold_e * (float)DEPTH_MAX_UINT);
        if (diff < threshold_uint)
        {
            if (originType == 0u)
            {
                InterlockedAdd(shared_point_count, 1u);
            }
            else if (originType == 1u)
            {
                // mesh(仮想オブジェクトなど)はピクセル単位で密集しているため、
                // 実用上の点群密度と合わせるように係数(x)倍する
                // オーバーフロー防止のため上限を設けて加算する
                uint safeMultiplier = min((uint)_StaticMeshDensityMultiplier, 1000u);
                InterlockedAdd(shared_point_count, safeMultiplier);
            }
        }
    }

    GroupMemoryBarrierWithGroupSync();

    if (groupIndex == 0u)
    {
        float density = float(shared_point_count) / float(GRID_SIZE * GRID_SIZE);
        _DensityMap_RW[groupID.xy] = density;
    }
}

// 5. Calculate Grid Level
// 密度とパラメータ(p_prime)に応じ、穴を埋めるために必要なグリッドごとの探索範囲(Level)を決定する
// 密度が低いほど必要な探索半径(L)が大きくなるため Level(log2) も上がる。
[numthreads(16, 16, 1)]
void CalculateGridLevel(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DensityMap.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;

    float density = _DensityMap[id.xy];
    int level = 0;
    if (density > 0.001)
    {
        float L = _NeighborhoodParam_p_prime / sqrt(density);
        level = (int) floor(log2(L));
    }
    _GridLevelMap_RW[id.xy] = max(0, level);
}

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

    [unroll]
    for (int i = 0; i < 9; ++i)
    {
        [unroll]
        for (int j = i + 1; j < 9; ++j)
        {
            if (values[i] > values[j])
            {
                int temp = values[i];
                values[i] = values[j];
                values[j] = temp;
            }
        }
    }

    _FilteredGridLevelMap_RW[id.xy] = values[4];
}

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
    _NeighborhoodSizeMap_RW[id.xy] = level;
}

#endif // PCD_OCCLUSION_KERNELS_PREPROCESS_INCLUDED