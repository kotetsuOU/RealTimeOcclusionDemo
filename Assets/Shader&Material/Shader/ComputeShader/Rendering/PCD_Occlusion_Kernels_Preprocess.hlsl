#ifndef PCD_OCCLUSION_KERNELS_PREPROCESS_INCLUDED
#define PCD_OCCLUSION_KERNELS_PREPROCESS_INCLUDED

// 0. Merge Buffer
[numthreads(256, 1, 1)]
void MergeBuffer(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _MergeCopyCount)
        return;
    _MergeDstBuffer[_MergeDstOffset + id.x] = _MergeSrcBuffer[_MergeSrcOffset + id.x];
}

// 1. Clear Maps
[numthreads(8, 8, 1)]
void ClearMaps(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    _ColorMap_RW[id.xy] = float4(0, 0, 0, 0);
    _DepthMap_RW[id.xy] = DEPTH_MAX_UINT;
    _ViewPositionMap_RW[id.xy] = float4(0, 0, 0, 1e9);
    _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0);
    _FinalImage_RW[id.xy] = float4(0, 0, 0, 0);

    _OriginMap_RW[id.xy] = float4(0, 0, 0, 1);
    _OriginTypeMap_RW[id.xy] = 2u; // 2 = Background
}

// 2. Project Points
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

    uint2 screenUV = uint2((ndc.x * 0.5 + 0.5) * _ScreenParams.x, (ndc.y * 0.5 + 0.5) * _ScreenParams.y);
    float depth = ndc.z;
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
[numthreads(GRID_SIZE, GRID_SIZE, 1)]
void CalculateDensity(uint3 id : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint groupIndex : SV_GroupIndex)
{
    if (groupIndex == 0u)
    {
        shared_point_count = 0u;
    }
    GroupMemoryBarrierWithGroupSync();

    uint z_min_uint = _GridZMinMap[groupID.xy];
    float z_min = (float) z_min_uint / (float) DEPTH_MAX_UINT;

    uint depth_uint = _DepthMap[id.xy];
    if (depth_uint < DEPTH_MAX_UINT)
    {
        float depth = (float) depth_uint / (float) DEPTH_MAX_UINT;
        if ((depth - z_min) < _DensityThreshold_e)
        {
            InterlockedAdd(shared_point_count, 1u);
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