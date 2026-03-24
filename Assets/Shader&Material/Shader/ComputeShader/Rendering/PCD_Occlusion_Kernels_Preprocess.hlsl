// PCD_Occlusion_Kernels_Preprocess.hlsl
// Preprocess stage kernels for the PCD Occlusion pipeline.
//
// Kernels defined here (in dispatch order):
//   0. MergeBuffer               - Copy point data between structured buffers
//   1. ClearMaps                 - Reset all render maps to initial / empty values
//   2. ProjectPoints             - Project 3D points to screen space; depth-test via atomics
//   3. CalculateGridZMin         - Find minimum depth within each GRID_SIZE x GRID_SIZE tile
//   4. CalculateDensity          - Measure PointCloud point density per tile
//   5. CalculateGridLevel        - Convert density to an LOD level using log2(p'/sqrt(density))
//   6. GridMedianFilter          - Apply a 3x3 median filter to smooth the grid-level map
//   7. CalculateNeighborhoodSize - Propagate the filtered grid level to per-pixel resolution

#ifndef PCD_OCCLUSION_KERNELS_PREPROCESS_INCLUDED
#define PCD_OCCLUSION_KERNELS_PREPROCESS_INCLUDED

// ---------------------------------------------------------------------------
// Kernel 0: MergeBuffer
// Copies '_MergeCopyCount' points from _MergeSrcBuffer[_MergeSrcOffset + id.x]
// into _MergeDstBuffer[_MergeDstOffset + id.x].
// Used to combine multiple point cloud sources into a single unified buffer
// before the rest of the pipeline runs.
// ---------------------------------------------------------------------------
[numthreads(256, 1, 1)]
void MergeBuffer(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _MergeCopyCount)
        return;
    _MergeDstBuffer[_MergeDstOffset + id.x] = _MergeSrcBuffer[_MergeSrcOffset + id.x];
}

// ---------------------------------------------------------------------------
// Kernel 1: ClearMaps
// Resets all render maps to their initial/empty states before a new frame.
//   _ColorMap        -> transparent black
//   _DepthMap        -> DEPTH_MAX_UINT (no depth)
//   _ViewPositionMap -> zero position, far depth sentinel (1e9)
//   _OcclusionResultMap / _OcclusionValueMap / _FinalImage -> zero
//   _OriginMap       -> opaque black
//   _OriginTypeMap   -> 2 (Background)
// ---------------------------------------------------------------------------
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

// ---------------------------------------------------------------------------
// Kernel 2: ProjectPoints
// Transforms each point from world space to NDC using _ViewMatrix and
// _ProjectionMatrix, then writes color, depth, view-space position, and
// origin type to the corresponding maps.
// An atomic min on _DepthMap ensures only the closest point wins when
// multiple points project to the same screen pixel.
// Points outside the view frustum (NDC outside [-1,1] x [-1,1] x [0,1])
// are discarded early.
// ---------------------------------------------------------------------------
[numthreads(256, 1, 1)]
void ProjectPoints(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= _PointCount)
        return;

    Point p = _PointBuffer[id.x];
    float4 worldPos = float4(p.position, 1.0);
    float4 viewPos  = mul(_ViewMatrix, worldPos);
    float4 clipPos  = mul(_ProjectionMatrix, viewPos);

    // Perspective divide to get NDC coordinates
    float3 ndc = clipPos.xyz / clipPos.w;
    if (ndc.x < -1 || ndc.x > 1 || ndc.y < -1 || ndc.y > 1 || ndc.z < 0 || ndc.z > 1)
        return;

    uint2 screenUV = uint2((ndc.x * 0.5 + 0.5) * _ScreenParams.x, (ndc.y * 0.5 + 0.5) * _ScreenParams.y);
    float depth = ndc.z;
    uint depth_uint = (uint) (depth * (float) DEPTH_MAX_UINT);

    // Atomic depth test: only the frontmost point writes color/position data
    uint oldDepth;
    InterlockedMin(_DepthMap_RW[screenUV], depth_uint, oldDepth);

    if (depth_uint < oldDepth)
    {
        _ColorMap_RW[screenUV]        = float4(p.color, 1.0);
        _ViewPositionMap_RW[screenUV] = float4(viewPos.xyz, depth);
        _OriginTypeMap_RW[screenUV]   = p.originType;
    }
}

// ---------------------------------------------------------------------------
// Kernel 3: CalculateGridZMin
// Groups pixels into GRID_SIZE x GRID_SIZE tiles.
// Each tile computes the minimum depth among all its pixels using a
// groupshared atomic min, then writes the result to _GridZMinMap.
// This per-tile minimum is later used by CalculateDensity to determine
// which pixels belong to the "front layer" of the tile.
// ---------------------------------------------------------------------------
[numthreads(GRID_SIZE, GRID_SIZE, 1)]
void CalculateGridZMin(uint3 id : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint groupIndex : SV_GroupIndex)
{
    // Thread 0 initialises the shared minimum to the sentinel value
    if (groupIndex == 0u)
    {
        shared_z_min = DEPTH_MAX_UINT;
    }
    GroupMemoryBarrierWithGroupSync();

    uint depth_uint = _DepthMap[id.xy];

    // Only valid (non-empty) pixels participate in the reduction
    if (depth_uint < DEPTH_MAX_UINT)
    {
        InterlockedMin(shared_z_min, depth_uint);
    }
    GroupMemoryBarrierWithGroupSync();

    // Thread 0 writes the result for the entire tile
    if (groupIndex == 0u)
    {
        _GridZMinMap_RW[groupID.xy] = shared_z_min;
    }
}

// ---------------------------------------------------------------------------
// Kernel 4: CalculateDensity
// Counts the number of PointCloud-origin pixels in a tile that are within
// _DensityThreshold_e of the tile's minimum depth (i.e. the "front layer").
// The count is normalised by the tile area (GRID_SIZE^2) to produce a
// density in [0, 1] which is written to _DensityMap.
// StaticMesh and Background pixels are excluded so that density reflects
// only the dynamic point cloud data.
// ---------------------------------------------------------------------------
[numthreads(GRID_SIZE, GRID_SIZE, 1)]
void CalculateDensity(uint3 id : SV_DispatchThreadID, uint3 groupID : SV_GroupID, uint groupIndex : SV_GroupIndex)
{
    // Thread 0 initialises the shared point counter
    if (groupIndex == 0u)
    {
        shared_point_count = 0u;
    }
    GroupMemoryBarrierWithGroupSync();

    uint z_min_uint = _GridZMinMap[groupID.xy];
    float z_min     = (float) z_min_uint / (float) DEPTH_MAX_UINT;
    uint depth_uint = _DepthMap[id.xy];

    // OriginType: 0 = PointCloud (Dynamic), 1 = StaticMesh, 2 = Background
    uint originType = _OriginTypeMap[id.xy];

    // Count only PointCloud pixels near the front layer of the tile
    if (depth_uint < DEPTH_MAX_UINT && originType == 0u)
    {
        float depth = (float) depth_uint / (float) DEPTH_MAX_UINT;
        if ((depth - z_min) < _DensityThreshold_e)
        {
            InterlockedAdd(shared_point_count, 1u);
        }
    }

    GroupMemoryBarrierWithGroupSync();

    // Thread 0 normalises and writes the density
    if (groupIndex == 0u)
    {
        float density = float(shared_point_count) / float(GRID_SIZE * GRID_SIZE);
        _DensityMap_RW[groupID.xy] = density;
    }
}

// ---------------------------------------------------------------------------
// Kernel 5: CalculateGridLevel
// Converts the per-tile density into an integer LOD level using the formula:
//   L = p' / sqrt(density)
//   level = floor(log2(L))
// Tiles with near-zero density (no points) are assigned level 0.
// This level controls how large a neighbourhood is sampled during occlusion:
// sparser regions use a coarser (larger) neighbourhood.
// ---------------------------------------------------------------------------
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

// ---------------------------------------------------------------------------
// Kernel 6: GridMedianFilter
// Applies a 3x3 median filter over the grid-level map to reduce single-tile
// outliers that would produce visually jarring neighbourhood size changes.
// Uses an in-register 9-element insertion sort to find the median.
// The result is written to _FilteredGridLevelMap.
// ---------------------------------------------------------------------------
[numthreads(16, 16, 1)]
void GridMedianFilter(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _GridLevelMap.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;

    // Gather all 9 values in the 3x3 neighbourhood
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

    // Selection sort to find the median (index 4 after sorting)
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

    _FilteredGridLevelMap_RW[id.xy] = values[4]; // median element
}

// ---------------------------------------------------------------------------
// Kernel 7: CalculateNeighborhoodSize
// Samples the filtered grid-level map at the tile that contains each pixel
// and copies that level directly into _NeighborhoodSizeMap at per-pixel
// resolution.  The neighbourhood size used during occlusion is
// radius = 2^level, so level 0 => radius 1, level 1 => radius 2, etc.
// ---------------------------------------------------------------------------
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