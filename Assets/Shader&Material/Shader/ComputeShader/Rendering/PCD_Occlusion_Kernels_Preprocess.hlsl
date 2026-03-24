// =============================================================================
// PCD_Occlusion_Kernels_Preprocess.hlsl
// Preprocessing kernels: buffer merging, map clearing, point projection, and
// per-tile density / LOD computation.
// =============================================================================
#ifndef PCD_OCCLUSION_KERNELS_PREPROCESS_INCLUDED
#define PCD_OCCLUSION_KERNELS_PREPROCESS_INCLUDED

// -----------------------------------------------------------------------------
// Kernel 0 -- MergeBuffer
// Copies _MergeCopyCount points from _MergeSrcBuffer (starting at _MergeSrcOffset)
// into _MergeDstBuffer (starting at _MergeDstOffset).
// Dispatched as a 1-D grid of 256-thread groups over the copy count.
// Allows multiple point cloud streams to be merged into a single working buffer
// before the projection pass.
// -----------------------------------------------------------------------------
[numthreads(256, 1, 1)]
void MergeBuffer(uint3 id : SV_DispatchThreadID)
{
    // Discard threads beyond the copy range.
    if (id.x >= (uint) _MergeCopyCount)
        return;
    _MergeDstBuffer[_MergeDstOffset + id.x] = _MergeSrcBuffer[_MergeSrcOffset + id.x];
}

// -----------------------------------------------------------------------------
// Kernel 1 -- ClearMaps
// Resets all per-pixel render maps to their initial / empty values before a new
// frame is rendered.  Must be dispatched before ProjectPoints so that every
// pixel starts in a known state.
//   - _ColorMap_RW         -> transparent black (no color)
//   - _DepthMap_RW         -> DEPTH_MAX_UINT (farthest possible depth)
//   - _ViewPositionMap_RW  -> zero position / far sentinel (w = 1e9)
//   - _OcclusionResultMap_RW / _OcclusionValueMap_RW / _FinalImage_RW -> cleared
//   - _OriginTypeMap_RW    -> 2 (Background)
// Dispatched as an 8x8 2-D grid covering the full screen.
// -----------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void ClearMaps(uint3 id : SV_DispatchThreadID)
{
    // Discard threads outside the screen bounds.
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    _ColorMap_RW[id.xy]          = float4(0, 0, 0, 0);
    _DepthMap_RW[id.xy]          = DEPTH_MAX_UINT;
    _ViewPositionMap_RW[id.xy]   = float4(0, 0, 0, 1e9);
    _OcclusionResultMap_RW[id.xy]= float4(0, 0, 0, 0);
    _OcclusionValueMap_RW[id.xy] = 0.0;
    _FinalImage_RW[id.xy]        = float4(0, 0, 0, 0);

    _OriginMap_RW[id.xy]         = float4(0, 0, 0, 1);
    _OriginTypeMap_RW[id.xy]     = 2u; // 2 = Background
}

// -----------------------------------------------------------------------------
// Kernel 2 -- ProjectPoints
// Transforms each point from world space into screen space and writes the
// nearest point's depth, color, and view-space position to the maps.
// An atomicMin on _DepthMap_RW ensures only the closest point at each pixel is
// kept (painter's algorithm resolved by depth).
//
// Steps per point:
//   1. Multiply world position by ViewMatrix  -> view space
//   2. Multiply by ProjectionMatrix           -> clip space
//   3. Perspective divide                     -> NDC [-1,1] x [-1,1] x [0,1]
//   4. Reject points outside the view frustum
//   5. Convert NDC to integer screen UV
//   6. Encode depth as uint and attempt InterlockedMin into _DepthMap_RW
//   7. If this point won the depth race, write its color and view position
// Dispatched as a 1-D grid of 256-thread groups over _PointCount.
// -----------------------------------------------------------------------------
[numthreads(256, 1, 1)]
void ProjectPoints(uint3 id : SV_DispatchThreadID)
{
    // Discard threads beyond the point buffer size.
    if (id.x >= _PointCount)
        return;

    Point p = _PointBuffer[id.x];

    // --- Step 1-3: World -> View -> Clip -> NDC ---
    float4 worldPos = float4(p.position, 1.0);
    float4 viewPos  = mul(_ViewMatrix, worldPos);
    float4 clipPos  = mul(_ProjectionMatrix, viewPos);
    float3 ndc      = clipPos.xyz / clipPos.w;

    // --- Step 4: Frustum cull (outside [-1,1] x [-1,1] or depth [0,1]) ---
    if (ndc.x < -1 || ndc.x > 1 || ndc.y < -1 || ndc.y > 1 || ndc.z < 0 || ndc.z > 1)
        return;

    // --- Step 5: Convert NDC to integer pixel coordinates ---
    uint2 screenUV = uint2(
        (ndc.x * 0.5 + 0.5) * _ScreenParams.x,
        (ndc.y * 0.5 + 0.5) * _ScreenParams.y);

    // --- Step 6: Encode NDC depth as uint and race for the nearest pixel ---
    float depth      = ndc.z;
    uint  depth_uint = (uint)(depth * (float)DEPTH_MAX_UINT);

    uint oldDepth;
    InterlockedMin(_DepthMap_RW[screenUV], depth_uint, oldDepth);

    // --- Step 7: Write color / view position only when this point won ---
    if (depth_uint < oldDepth)
    {
        _ColorMap_RW[screenUV]        = float4(p.color, 1.0);
        _ViewPositionMap_RW[screenUV] = float4(viewPos.xyz, depth);
        _OriginTypeMap_RW[screenUV]   = p.originType;
    }
}

// -----------------------------------------------------------------------------
// Kernel 3 -- CalculateGridZMin
// Finds the minimum (nearest) depth value within each GRID_SIZE x GRID_SIZE
// screen tile and writes it to _GridZMinMap_RW.
// Uses groupshared memory (shared_z_min) so the minimum reduction is done
// entirely within the warp/wave before a single write to the output map.
//
// Dispatched with (GRID_SIZE x GRID_SIZE) threads per group, one group per tile.
// -----------------------------------------------------------------------------
[numthreads(GRID_SIZE, GRID_SIZE, 1)]
void CalculateGridZMin(uint3 id : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint groupIndex : SV_GroupIndex)
{
    // Thread 0 of each tile initializes the shared accumulator.
    if (groupIndex == 0u)
    {
        shared_z_min = DEPTH_MAX_UINT;
    }
    GroupMemoryBarrierWithGroupSync();

    // Each thread contributes its pixel's depth to the tile minimum.
    uint depth_uint = _DepthMap[id.xy];
    if (depth_uint < DEPTH_MAX_UINT)
    {
        InterlockedMin(shared_z_min, depth_uint);
    }
    GroupMemoryBarrierWithGroupSync();

    // Thread 0 writes the final minimum for this tile.
    if (groupIndex == 0u)
    {
        _GridZMinMap_RW[groupID.xy] = shared_z_min;
    }
}

// -----------------------------------------------------------------------------
// Kernel 4 -- CalculateDensity
// Computes the local point cloud density for each GRID_SIZE x GRID_SIZE tile.
// Density = (number of dynamic point-cloud pixels within _DensityThreshold_e
//            of the tile's minimum depth) / (GRID_SIZE * GRID_SIZE).
//
// Only PointCloud pixels (originType == 0) are counted; StaticMesh and
// Background pixels are excluded so the LOD level reflects point density only.
// Uses groupshared memory (shared_point_count) for the intra-tile reduction.
//
// Dispatched with (GRID_SIZE x GRID_SIZE) threads per group, one group per tile.
// -----------------------------------------------------------------------------
[numthreads(GRID_SIZE, GRID_SIZE, 1)]
void CalculateDensity(uint3 id : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint groupIndex : SV_GroupIndex)
{
    // Thread 0 resets the shared counter for this tile.
    if (groupIndex == 0u)
    {
        shared_point_count = 0u;
    }
    GroupMemoryBarrierWithGroupSync();

    // Normalize the tile's minimum depth from uint to float [0, 1].
    uint  z_min_uint = _GridZMinMap[groupID.xy];
    float z_min      = (float)z_min_uint / (float)DEPTH_MAX_UINT;

    uint depth_uint = _DepthMap[id.xy];

    // OriginType: 0 = PointCloud (Dynamic), 1 = StaticMesh, 2 = Background
    uint originType = _OriginTypeMap[id.xy];

    // Count only valid, dynamic point-cloud pixels close to the tile's z-min.
    if (depth_uint < DEPTH_MAX_UINT && originType == 0u)
    {
        float depth = (float)depth_uint / (float)DEPTH_MAX_UINT;
        if ((depth - z_min) < _DensityThreshold_e)
        {
            InterlockedAdd(shared_point_count, 1u);
        }
    }

    GroupMemoryBarrierWithGroupSync();

    // Thread 0 writes the normalized density for the tile.
    if (groupIndex == 0u)
    {
        float density = float(shared_point_count) / float(GRID_SIZE * GRID_SIZE);
        _DensityMap_RW[groupID.xy] = density;
    }
}

// -----------------------------------------------------------------------------
// Kernel 5 -- CalculateGridLevel
// Converts the per-tile density into a discrete LOD level (neighbourhood size
// expressed as a power-of-two exponent).
//
// Formula: L = p' / sqrt(density),  level = floor(log2(L))
// where p' = _NeighborhoodParam_p_prime is a tunable scaling constant.
// Tiles with near-zero density are assigned level 0 (no expansion).
//
// Dispatched as a 16x16 2-D grid over the density map dimensions
// (screen / GRID_SIZE).
// -----------------------------------------------------------------------------
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
        // Compute the ideal neighbourhood radius for this density,
        // then take its log2 to get the pyramid level.
        float L = _NeighborhoodParam_p_prime / sqrt(density);
        level = (int) floor(log2(L));
    }
    _GridLevelMap_RW[id.xy] = max(0, level);
}

// -----------------------------------------------------------------------------
// Kernel 6 -- GridMedianFilter
// Applies a 3x3 median filter to the raw LOD level map to reduce isolated
// outlier tiles (tiles whose level differs greatly from their neighbours).
// Uses an in-register bubble sort of 9 values; the median is the middle element.
//
// Dispatched as a 16x16 2-D grid over the density map dimensions.
// -----------------------------------------------------------------------------
[numthreads(16, 16, 1)]
void GridMedianFilter(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _GridLevelMap.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;

    // Collect the 3x3 neighbourhood (clamped at borders).
    int values[9];
    values[0] = _GridLevelMap[clamp(id.xy + int2(-1, -1), 0, dim - 1)];
    values[1] = _GridLevelMap[clamp(id.xy + int2( 0, -1), 0, dim - 1)];
    values[2] = _GridLevelMap[clamp(id.xy + int2( 1, -1), 0, dim - 1)];
    values[3] = _GridLevelMap[clamp(id.xy + int2(-1,  0), 0, dim - 1)];
    values[4] = _GridLevelMap[clamp(id.xy + int2( 0,  0), 0, dim - 1)]; // Centre pixel
    values[5] = _GridLevelMap[clamp(id.xy + int2( 1,  0), 0, dim - 1)];
    values[6] = _GridLevelMap[clamp(id.xy + int2(-1,  1), 0, dim - 1)];
    values[7] = _GridLevelMap[clamp(id.xy + int2( 0,  1), 0, dim - 1)];
    values[8] = _GridLevelMap[clamp(id.xy + int2( 1,  1), 0, dim - 1)];

    // Bubble sort all 9 values in ascending order.
    [unroll]
    for (int i = 0; i < 9; ++i)
    {
        [unroll]
        for (int j = i + 1; j < 9; ++j)
        {
            if (values[i] > values[j])
            {
                int temp    = values[i];
                values[i]   = values[j];
                values[j]   = temp;
            }
        }
    }

    // The median is element [4] after sorting.
    _FilteredGridLevelMap_RW[id.xy] = values[4];
}

// -----------------------------------------------------------------------------
// Kernel 7 -- CalculateNeighborhoodSize
// Propagates the (smoothed) tile-level LOD value to every screen pixel that
// belongs to that tile.  Each pixel simply looks up the tile it falls in and
// copies the filtered LOD level.  This produces a full-resolution map that the
// gradient correction and occlusion kernels can sample directly.
//
// Dispatched as an 8x8 2-D grid covering the full screen.
// -----------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void CalculateNeighborhoodSize(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)_ScreenParams.x || id.y >= (uint)_ScreenParams.y)
        return;

    // Determine which tile this pixel belongs to.
    uint2 gridID = id.xy / GRID_SIZE;
    int level = _FilteredGridLevelMap[gridID];

    // Store the LOD level at pixel resolution for later per-pixel use.
    _NeighborhoodSizeMap_RW[id.xy] = level;
}

#endif // PCD_OCCLUSION_KERNELS_PREPROCESS_INCLUDED