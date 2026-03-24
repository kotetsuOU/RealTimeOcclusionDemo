// --------------------------------------------------------------------------
// PCD_Occlusion_Kernels_Preprocess.hlsl
// Preprocessing pass kernels executed at the start of each frame.
// Execution order (matches #pragma kernel declarations in .compute):
//   0. MergeBuffer            – copy point data between structured buffers
//   1. ClearMaps              – reset all render targets to default values
//   2. ProjectPoints          – rasterize the point cloud onto the screen
//   3. CalculateGridZMin      – find the nearest depth per grid cell
//   4. CalculateDensity       – measure point cloud fill rate per grid cell
//   5. CalculateGridLevel     – map density to a neighborhood search level
//   6. GridMedianFilter       – spatially smooth the grid levels
//   7. CalculateNeighborhoodSize – propagate grid levels to per-pixel map
// --------------------------------------------------------------------------
#ifndef PCD_OCCLUSION_KERNELS_PREPROCESS_INCLUDED
#define PCD_OCCLUSION_KERNELS_PREPROCESS_INCLUDED

// --------------------------------------------------------------------------
// 0. MergeBuffer
// Copies _MergeCopyCount points from _MergeSrcBuffer (starting at
// _MergeSrcOffset) into _MergeDstBuffer (starting at _MergeDstOffset).
// Enables combining multiple source point clouds into a single destination
// buffer without a CPU round-trip.
// --------------------------------------------------------------------------
[numthreads(256, 1, 1)]
void MergeBuffer(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _MergeCopyCount)
        return;
    _MergeDstBuffer[_MergeDstOffset + id.x] = _MergeSrcBuffer[_MergeSrcOffset + id.x];
}

// --------------------------------------------------------------------------
// 1. ClearMaps
// Resets every render target pixel to its "empty" sentinel value so that
// subsequent kernels can safely use atomic min / accumulation operations
// without leftover data from previous frames.
//   - ColorMap        -> transparent black
//   - DepthMap        -> DEPTH_MAX_UINT (no geometry)
//   - ViewPositionMap -> w = 1e9 (far sentinel for view-space depth)
//   - OcclusionResult / OcclusionValue / FinalImage -> zero
//   - OriginTypeMap   -> 2 (Background)
// --------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void ClearMaps(uint3 id : SV_DispatchThreadID)
{
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

// --------------------------------------------------------------------------
// 2. ProjectPoints
// Transforms each point from world space to screen space and writes its
// color and view-space position to the per-pixel maps.
//
// Algorithm:
//   1. Multiply by the view matrix to get view-space position.
//   2. Multiply by the projection matrix to get clip space.
//   3. Perspective divide to get normalized device coordinates (NDC).
//   4. Discard points outside the NDC frustum ([-1,1] x [-1,1] x [0,1]).
//   5. Map NDC to pixel coordinates.
//   6. Encode depth as uint and use InterlockedMin to keep the nearest
//      point when multiple points project to the same pixel.
//   7. Only update color / view-position maps when this thread wins the
//      depth race (depth_uint < previous minimum).
// --------------------------------------------------------------------------
[numthreads(256, 1, 1)]
void ProjectPoints(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= _PointCount)
        return;

    Point p = _PointBuffer[id.x];
    float4 worldPos = float4(p.position, 1.0);
    float4 viewPos  = mul(_ViewMatrix,      worldPos);
    float4 clipPos  = mul(_ProjectionMatrix, viewPos);

    // Perspective divide -> NDC in [-1,1] x [-1,1] x [0,1]
    float3 ndc = clipPos.xyz / clipPos.w;
    if (ndc.x < -1 || ndc.x > 1 || ndc.y < -1 || ndc.y > 1 || ndc.z < 0 || ndc.z > 1)
        return; // point is outside the view frustum

    // Convert NDC to integer pixel coordinates
    uint2 screenUV = uint2((ndc.x * 0.5 + 0.5) * _ScreenParams.x,
                           (ndc.y * 0.5 + 0.5) * _ScreenParams.y);

    // Encode linear NDC depth [0,1] as uint for atomic comparison
    float depth      = ndc.z;
    uint  depth_uint = (uint) (depth * (float) DEPTH_MAX_UINT);

    // Race to claim the pixel: only the closest point writes its attributes
    uint oldDepth;
    InterlockedMin(_DepthMap_RW[screenUV], depth_uint, oldDepth);

    if (depth_uint < oldDepth)
    {
        _ColorMap_RW[screenUV]        = float4(p.color, 1.0);
        _ViewPositionMap_RW[screenUV] = float4(viewPos.xyz, depth);
        _OriginTypeMap_RW[screenUV]   = p.originType;
    }
}

// --------------------------------------------------------------------------
// 3. CalculateGridZMin
// Finds the minimum (nearest) depth value across all pixels in a 16x16
// grid cell and writes it to _GridZMinMap_RW at grid-space coordinates.
//
// Uses group-shared memory + InterlockedMin so every thread in the group
// participates in a single reduction without global memory overhead.
// The result is used in CalculateDensity to define the depth range that
// counts as being "inside" the cell.
// --------------------------------------------------------------------------
[numthreads(GRID_SIZE, GRID_SIZE, 1)]
void CalculateGridZMin(uint3 id : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint groupIndex : SV_GroupIndex)
{
    // Thread 0 initializes the shared accumulator before other threads use it
    if (groupIndex == 0u)
    {
        shared_z_min = DEPTH_MAX_UINT;
    }
    GroupMemoryBarrierWithGroupSync();

    uint depth_uint = _DepthMap[id.xy];

    // Only valid (non-background) pixels participate in the reduction
    if (depth_uint < DEPTH_MAX_UINT)
    {
        InterlockedMin(shared_z_min, depth_uint);
    }
    GroupMemoryBarrierWithGroupSync();

    // Thread 0 writes the group result to the grid-resolution map
    if (groupIndex == 0u)
    {
        _GridZMinMap_RW[groupID.xy] = shared_z_min;
    }
}

// --------------------------------------------------------------------------
// 4. CalculateDensity
// Counts the number of PointCloud pixels (originType == 0) within
// _DensityThreshold_e of the grid's minimum depth, then divides by the
// total cell area (GRID_SIZE²) to produce a fill density in [0, 1].
//
// Why only PointCloud pixels?  StaticMesh and Background pixels are always
// dense by definition; using only point cloud pixels measures the actual
// spatial sampling density of the captured sensor data.
// --------------------------------------------------------------------------
[numthreads(GRID_SIZE, GRID_SIZE, 1)]
void CalculateDensity(uint3 id : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint groupIndex : SV_GroupIndex)
{
    // Initialise the per-group counter
    if (groupIndex == 0u)
    {
        shared_point_count = 0u;
    }
    GroupMemoryBarrierWithGroupSync();

    float z_min  = (float) _GridZMinMap[groupID.xy] / (float) DEPTH_MAX_UINT;
    uint  depth_uint = _DepthMap[id.xy];

    // OriginType fetch. 0 = PointCloud (Dynamic), 1 = StaticMesh, 2 = Background
    uint originType = _OriginTypeMap[id.xy];

    // A pixel is "dense" if it has geometry AND is a point cloud pixel
    // within the depth epsilon of the grid's nearest surface
    if (depth_uint < DEPTH_MAX_UINT && originType == 0u)
    {
        float depth = (float) depth_uint / (float) DEPTH_MAX_UINT;
        if ((depth - z_min) < _DensityThreshold_e)
        {
            InterlockedAdd(shared_point_count, 1u);
        }
    }

    GroupMemoryBarrierWithGroupSync();

    // Thread 0 normalizes and stores the result
    if (groupIndex == 0u)
    {
        float density = float(shared_point_count) / float(GRID_SIZE * GRID_SIZE);
        _DensityMap_RW[groupID.xy] = density;
    }
}

// --------------------------------------------------------------------------
// 5. CalculateGridLevel
// Maps the density of a grid cell to an integer neighborhood level using
// the formula:  L = floor(log2(_NeighborhoodParam_p_prime / sqrt(density)))
//
// Rationale: sparser regions (lower density) need a larger search radius to
// find neighbors, so the level increases as density decreases.  The base-2
// logarithm maps this to a power-of-two radius used later when sampling
// neighboring pixels.  Cells with negligible density get level 0.
// --------------------------------------------------------------------------
[numthreads(16, 16, 1)]
void CalculateGridLevel(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DensityMap.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;

    float density = _DensityMap[id.xy];
    int   level   = 0;
    if (density > 0.001)
    {
        // p' / sqrt(density) gives the effective inter-point spacing;
        // taking log2 converts it to a mip-like level index
        float L = _NeighborhoodParam_p_prime / sqrt(density);
        level   = (int) floor(log2(L));
    }
    _GridLevelMap_RW[id.xy] = max(0, level); // clamp to non-negative
}

// --------------------------------------------------------------------------
// 6. GridMedianFilter
// Applies a 3x3 median filter to _GridLevelMap to remove isolated outlier
// cells caused by noise or sparse projections.  The median is found by
// sorting all 9 samples with a simple bubble-sort and selecting index [4].
//
// Using median (rather than mean) preserves sharp boundaries between
// high-density and low-density regions while removing salt-and-pepper noise.
// --------------------------------------------------------------------------
[numthreads(16, 16, 1)]
void GridMedianFilter(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _GridLevelMap.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;

    // Collect 3x3 neighborhood values, clamping at the texture border
    int values[9];
    values[0] = _GridLevelMap[clamp(id.xy + int2(-1, -1), 0, dim - 1)];
    values[1] = _GridLevelMap[clamp(id.xy + int2( 0, -1), 0, dim - 1)];
    values[2] = _GridLevelMap[clamp(id.xy + int2( 1, -1), 0, dim - 1)];
    values[3] = _GridLevelMap[clamp(id.xy + int2(-1,  0), 0, dim - 1)];
    values[4] = _GridLevelMap[clamp(id.xy + int2( 0,  0), 0, dim - 1)];
    values[5] = _GridLevelMap[clamp(id.xy + int2( 1,  0), 0, dim - 1)];
    values[6] = _GridLevelMap[clamp(id.xy + int2(-1,  1), 0, dim - 1)];
    values[7] = _GridLevelMap[clamp(id.xy + int2( 0,  1), 0, dim - 1)];
    values[8] = _GridLevelMap[clamp(id.xy + int2( 1,  1), 0, dim - 1)];

    // Bubble-sort the 9 values ascending; [unroll] hints the compiler to
    // unroll both loops since the iteration count is compile-time constant
    [unroll]
    for (int i = 0; i < 9; ++i)
    {
        [unroll]
        for (int j = i + 1; j < 9; ++j)
        {
            if (values[i] > values[j])
            {
                int temp   = values[i];
                values[i]  = values[j];
                values[j]  = temp;
            }
        }
    }

    // Index 4 is the median of 9 elements
    _FilteredGridLevelMap_RW[id.xy] = values[4];
}

// --------------------------------------------------------------------------
// 7. CalculateNeighborhoodSize
// Upsamples the grid-resolution filtered level map to full screen resolution
// by assigning each pixel the level of its parent grid cell.
// The result is used by ApplyAdaptiveGradientCorrection and OcclusionAndFilter.
// --------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void CalculateNeighborhoodSize(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    // Integer division maps each pixel to its containing grid cell
    uint2 gridID = id.xy / GRID_SIZE;
    int   level  = _FilteredGridLevelMap[gridID];
    _NeighborhoodSizeMap_RW[id.xy] = level;
}

#endif // PCD_OCCLUSION_KERNELS_PREPROCESS_INCLUDED