// PCD_Occlusion_Kernels_Occlusion.hlsl
// Occlusion stage kernels for the PCD Occlusion pipeline.
//
// Kernels defined here (in dispatch order):
//   8a-1. BuildDepthPyramidL1              - Build depth mip level 1 from the full-res depth map
//   8a-2. BuildDepthPyramidL2              - Build depth mip level 2 from level 1
//   8a-3. BuildDepthPyramidL3              - Build depth mip level 3 from level 2
//   8a-4. BuildDepthPyramidL4              - Build depth mip level 4 from level 3
//   8b+8c. ApplyAdaptiveGradientCorrection - Lower neighbourhood level at depth-edge pixels
//   9.    OcclusionAndFilter               - Per-pixel occlusion test and alpha computation

#ifndef PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED
#define PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED

// ---------------------------------------------------------------------------
// Kernels 8a-1 through 8a-4: BuildDepthPyramidL1 / L2 / L3 / L4
// Each kernel builds one level of a 4-level hierarchical depth (min) pyramid.
//   L1: full-res depth -> 1/2 resolution
//   L2: L1            -> 1/4 resolution
//   L3: L2            -> 1/8 resolution
//   L4: L3            -> 1/16 resolution
// Each level is built by calling ZMinDownsample (see PCD_Occlusion_Helpers.hlsl),
// which takes the minimum of a 2x2 texel block from the previous level.
// The pyramid is later sampled by ApplyAdaptiveGradientCorrection to detect
// depth discontinuities at different spatial scales.
// ---------------------------------------------------------------------------

// Kernel 8a-1: Build depth pyramid level 1 (1/2 resolution)
[numthreads(8, 8, 1)]
void BuildDepthPyramidL1(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL1_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL1_RW[id.xy] = ZMinDownsample(_DepthMap, id.xy * 2u);
}

// Kernel 8a-2: Build depth pyramid level 2 (1/4 resolution)
[numthreads(8, 8, 1)]
void BuildDepthPyramidL2(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL2_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL2_RW[id.xy] = ZMinDownsample(_DepthPyramidL1, id.xy * 2u);
}

// Kernel 8a-3: Build depth pyramid level 3 (1/8 resolution)
[numthreads(8, 8, 1)]
void BuildDepthPyramidL3(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL3_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL3_RW[id.xy] = ZMinDownsample(_DepthPyramidL2, id.xy * 2u);
}

// Kernel 8a-4: Build depth pyramid level 4 (1/16 resolution)
[numthreads(8, 8, 1)]
void BuildDepthPyramidL4(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL4_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL4_RW[id.xy] = ZMinDownsample(_DepthPyramidL3, id.xy * 2u);
}

// ---------------------------------------------------------------------------
// Kernel 8b+8c: ApplyAdaptiveGradientCorrection
// For each pixel, checks the Sobel gradient magnitude at the corresponding
// position in the appropriate depth pyramid level:
//   level 1 -> sample _DepthPyramidL1 at (id / 2)
//   level 2 -> sample _DepthPyramidL2 at (id / 4)
//   level 3 -> sample _DepthPyramidL3 at (id / 8)
//   level 4 -> sample _DepthPyramidL4 at (id / 16)
// If the gradient exceeds _GradientThreshold_g_th the neighbourhood level is
// reduced by 1 (clamped to 0).  This prevents large-radius occlusion sampling
// across sharp depth discontinuities (object silhouettes), which would cause
// unwanted blending / ghosting artifacts.
// The corrected level is written to _CorrectedNeighborhoodSizeMap_RW.
// ---------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void ApplyAdaptiveGradientCorrection(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    uint2 fullResUV = id.xy;
    int level = _NeighborhoodSizeMap[fullResUV];
    int correctedLevel = level;

    if (level > 0)
    {
        float gradient = 0.0;
        uint2 uv_lowres;

        // Sample the pyramid level that corresponds to the current neighbourhood scale
        if (level == 1)
        {
            uv_lowres = fullResUV / 2u;
            gradient  = SobelOnPyramid(_DepthPyramidL1, uv_lowres);
        }
        else if (level == 2)
        {
            uv_lowres = fullResUV / 4u;
            gradient  = SobelOnPyramid(_DepthPyramidL2, uv_lowres);
        }
        else if (level == 3)
        {
            uv_lowres = fullResUV / 8u;
            gradient  = SobelOnPyramid(_DepthPyramidL3, uv_lowres);
        }
        else
        {
            uv_lowres = fullResUV / 16u;
            gradient  = SobelOnPyramid(_DepthPyramidL4, uv_lowres);
        }

        // Reduce the neighbourhood level at depth edges to avoid cross-edge sampling
        if (gradient > _GradientThreshold_g_th)
        {
            correctedLevel = max(0, level - 1);
        }
    }
    _CorrectedNeighborhoodSizeMap_RW[fullResUV] = correctedLevel;
}

// ---------------------------------------------------------------------------
// Kernel 9: OcclusionAndFilter
// Main occlusion kernel.  For each pixel this kernel:
//
//   Case A - Pixel has no point (empty pixel):
//     Attempt a small-radius (fillRadius=2) weighted color fill from nearby
//     filled pixels that are in front of any virtual object depth.  If enough
//     weight is accumulated, the averaged color is written to
//     _OcclusionResultMap and the origin type is set from the weighted average
//     of neighbour origin types.  If not enough neighbours are found and a
//     virtual object occupies this pixel, it is tagged as StaticMesh (1);
//     otherwise it is tagged as Background (2).
//
//   Case B - Pixel has a point AND a virtual object is closer (virtual occludes point):
//     The point is hidden behind the virtual object.  The pixel is cleared
//     and tagged as StaticMesh so the compositing pass renders the virtual
//     object instead.
//
//   Case C - Pixel has a point that is in front of (or there is no) virtual object:
//     Compute a neighbourhood occlusion score: for each neighbour within
//     radius = 2^level, compute the angle-based occlusion term
//       occlusionValue = 1 - dot(normalize(neighbor - current), normalize(-neighbor))
//     Average the values, then map to an alpha using either a hard threshold
//     or a smooth fade (controlled by _OcclusionFadeWidth):
//       - Hard:  alpha = 0 if occlusionAverage < _OcclusionThreshold, else 1
//       - Soft:  alpha = smoothstep(_OcclusionThreshold,
//                                   _OcclusionThreshold + _OcclusionFadeWidth,
//                                   occlusionAverage)
//     The final color (with the computed alpha) is stored in _OcclusionResultMap.
// ---------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void OcclusionAndFilter(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    uint pointDepth_uint = _DepthMap[id.xy];

    // --- Fetch virtual (camera/mesh) depth if available ---
    uint vDepth_uint = DEPTH_MAX_UINT;
    bool hasVirtualObj = false;

    if (_UseVirtualDepth > 0)
    {
        float vDepthRaw = _VirtualDepthMap[id.xy];
        // URP stores depth in [0,1] with 0=near; invert so 0=far, 1=near
        float vDepth = 1.0 - vDepthRaw;

        if (vDepth < 0.9999)
        {
            hasVirtualObj = true;
            vDepth_uint = (uint) (vDepth * (float) DEPTH_MAX_UINT);
        }
    }

    // -----------------------------------------------------------------------
    // Case A: Empty pixel - attempt weighted color fill from neighbours
    // -----------------------------------------------------------------------
    if (pointDepth_uint >= DEPTH_MAX_UINT)
    {
        int fillRadius = 2;
        float totalWeight = 0.0;
        float4 accumulatedColor = float4(0, 0, 0, 0);
        float weightedOriginSum = 0.0;

        // Only accept neighbours that are in front of the virtual object (if any)
        uint thresholdDepth = (hasVirtualObj) ? vDepth_uint : DEPTH_MAX_UINT;

        for (int y = -fillRadius; y <= fillRadius; y++)
        {
            for (int x = -fillRadius; x <= fillRadius; x++)
            {
                if (x == 0 && y == 0)
                    continue;

                // Distance-based weight: closer neighbours contribute more
                float distSq = dot(float2(x, y), float2(x, y));
                float weight = 1.0 / (1.0 + distSq * 0.5);

                int2  offset = int2(x, y);
                uint2 uv     = clamp((int2) id.xy + offset, 0, (int2) _ScreenParams.xy - 1);

                uint nDepth_uint = _DepthMap[uv];

                if (nDepth_uint < DEPTH_MAX_UINT)
                {
                    if (nDepth_uint < thresholdDepth)
                    {
                        float4 c = _ColorMap[uv];
                        accumulatedColor += c * weight;
                        totalWeight += weight;

                        uint nType = _OriginTypeMap[uv];
                        weightedOriginSum += (float) nType * weight;
                    }
                }
            }
        }

        if (totalWeight > 0.3)
        {
            _OcclusionResultMap_RW[id.xy] = accumulatedColor / totalWeight;

            // Determine origin type from weighted average (< 0.5 = PointCloud, >= 0.5 = StaticMesh)
            float avgType = weightedOriginSum / totalWeight;
            if (avgType < 0.5)
            {
                _OriginMap_RW[id.xy]      = float4(0, 0, 0, 1);
                _OriginTypeMap_RW[id.xy]  = 0u;
            }
            else
            {
                _OriginMap_RW[id.xy]      = float4(1, 1, 1, 1);
                _OriginTypeMap_RW[id.xy]  = 1u;
            }
        }
        else
        {
            _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0);

            if (hasVirtualObj)
            {
                // Tag as virtual/mesh so the compositor shows the camera image here
                _OriginMap_RW[id.xy]     = float4(1, 1, 1, 1);
                _OriginTypeMap_RW[id.xy] = 1u;
            }
            else
            {
                _OriginTypeMap_RW[id.xy] = 2u; // Background
            }
        }
        return;
    }

    // -----------------------------------------------------------------------
    // Case B: Virtual object is closer than the point cloud point
    //         -> hide the point; the virtual object takes priority
    // -----------------------------------------------------------------------
    uint depthBias = DEPTH_MAX_UINT / 1000; // small bias to avoid z-fighting

    if (hasVirtualObj && (vDepth_uint + depthBias) < pointDepth_uint)
    {
        _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0);
        _OriginMap_RW[id.xy]          = float4(1, 1, 1, 1);
        _OriginTypeMap_RW[id.xy]      = 1u;
        return;
    }

    // -----------------------------------------------------------------------
    // Case C: Point is visible - compute occlusion score from neighbourhood
    // -----------------------------------------------------------------------
    float3 currentPos   = _ViewPositionMap[id.xy].xyz;
    float  currentDepth = _ViewPositionMap[id.xy].w;

    // Neighbourhood radius grows exponentially with the LOD level: radius = 2^level
    int  level  = _FinalNeighborhoodSizeMap[id.xy];
    uint radius = (uint) pow(2.0, (float) level);
    radius = max(radius, 1u);

    float occlusionSum   = 0.0;
    uint  neighborCount  = 0u;

    for (uint y = 0u; y <= radius * 2u; y++)
    {
        for (uint x = 0u; x <= radius * 2u; x++)
        {
            if (x == radius && y == radius)
                continue; // skip the centre pixel

            int2  offset = int2((int) x - (int) radius, (int) y - (int) radius);
            uint2 uv     = clamp((int2) id.xy + offset, 0, (int2) _ScreenParams.xy - 1);

            uint neighborDepth_uint = _DepthMap[uv];

            if (neighborDepth_uint < DEPTH_MAX_UINT)
            {
                float3 neighborPos = _ViewPositionMap[uv].xyz;

                // Angle-based occlusion term:
                //   occlusionValue = 1 - cos(angle between (neighbor-current) and (-neighbor))
                // A value near 0 means the current point is fully visible from this direction;
                // a value near 2 means it is strongly occluded.
                float3 y_minus_x = neighborPos - currentPos;
                float3 minus_y   = -neighborPos;

                if (length(y_minus_x) > 1e-6 && length(minus_y) > 1e-6)
                {
                    float occlusionValue = 1.0 - dot(normalize(y_minus_x), normalize(minus_y));
                    occlusionSum += occlusionValue;
                    neighborCount++;
                }
            }
        }
    }

    // Map the average occlusion to an alpha value
    float alpha = 1.0;
    if (neighborCount > 0)
    {
        float occlusionAverage = occlusionSum / (float) neighborCount;

        // Optionally record the raw occlusion value for debugging / visualisation
        if (_RecordOcclusionDebug > 0)
        {
            _OcclusionValueMap_RW[id.xy] = occlusionAverage;
        }

        if (_OcclusionFadeWidth > 1e-4)
        {
            // Smooth fade: alpha ramps from 0 to 1 over [threshold, threshold + fadeWidth]
            float fadeEnd = _OcclusionThreshold + _OcclusionFadeWidth;
            alpha = smoothstep(_OcclusionThreshold, fadeEnd, occlusionAverage);
        }
        else
        {
            // Hard threshold: fully occluded (alpha=0) below threshold, fully visible above
            if (occlusionAverage < _OcclusionThreshold)
            {
                alpha = 0.0;
            }
        }
    }

    if (alpha <= 0.0)
    {
        _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0);
    }
    else
    {
        float4 col = _ColorMap[id.xy];
        col.a *= alpha;
        _OcclusionResultMap_RW[id.xy] = col;

        // Propagate origin type to the origin map for downstream compositing
        uint originType = _OriginTypeMap[id.xy];
        if (originType == 0u)
            _OriginMap_RW[id.xy] = float4(0, 0, 0, 1); // PointCloud -> black
        else if (originType == 1u)
            _OriginMap_RW[id.xy] = float4(1, 1, 1, 1); // StaticMesh -> white
    }
}

#endif // PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED