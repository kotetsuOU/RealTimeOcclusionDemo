// =============================================================================
// PCD_Occlusion_Kernels_Occlusion.hlsl
// Depth pyramid construction, adaptive gradient correction, and the main
// per-pixel occlusion/filtering kernel.
// =============================================================================
#ifndef PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED
#define PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED

// -----------------------------------------------------------------------------
// Kernels 8a-1 to 8a-4 -- BuildDepthPyramidL1 / L2 / L3 / L4
// Build a 4-level min-depth mipmap pyramid from the full-resolution depth map.
// Each level halves the resolution by downsampling a 2x2 block to its minimum
// value (see ZMinDownsample in PCD_Occlusion_Helpers.hlsl).
//
//   L1: 1/2  resolution of the screen
//   L2: 1/4  resolution of the screen
//   L3: 1/8  resolution of the screen
//   L4: 1/16 resolution of the screen
//
// The pyramid is later sampled by ApplyAdaptiveGradientCorrection to detect
// depth discontinuities at the resolution appropriate for each pixel's LOD.
// All four kernels are dispatched as 8x8 2-D grids over the output dimensions.
// -----------------------------------------------------------------------------

// Level 1: Downsample from full-resolution _DepthMap (2x2 -> 1 min)
[numthreads(8, 8, 1)]
void BuildDepthPyramidL1(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL1_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    // Each output pixel covers a 2x2 block in the source; take the minimum depth.
    _DepthPyramidL1_RW[id.xy] = ZMinDownsample(_DepthMap, id.xy * 2u);
}

// Level 2: Downsample from L1 (each pixel covers a 4x4 block of the screen)
[numthreads(8, 8, 1)]
void BuildDepthPyramidL2(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL2_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL2_RW[id.xy] = ZMinDownsample(_DepthPyramidL1, id.xy * 2u);
}

// Level 3: Downsample from L2 (each pixel covers an 8x8 block of the screen)
[numthreads(8, 8, 1)]
void BuildDepthPyramidL3(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL3_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL3_RW[id.xy] = ZMinDownsample(_DepthPyramidL2, id.xy * 2u);
}

// Level 4: Downsample from L3 (each pixel covers a 16x16 block of the screen)
[numthreads(8, 8, 1)]
void BuildDepthPyramidL4(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL4_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL4_RW[id.xy] = ZMinDownsample(_DepthPyramidL3, id.xy * 2u);
}

// -----------------------------------------------------------------------------
// Kernel 8b+8c -- ApplyAdaptiveGradientCorrection
// Reduces the per-pixel neighbourhood LOD level by 1 at depth edges to prevent
// the occlusion kernel from averaging across a foreground/background boundary.
//
// For each pixel:
//   1. Read the current LOD level from _NeighborhoodSizeMap.
//   2. Select the depth pyramid level that matches the LOD resolution.
//   3. Compute the Sobel gradient magnitude at the corresponding pyramid pixel.
//   4. If the gradient exceeds _GradientThreshold_g_th, reduce the level by 1
//      (minimum 0) so the occlusion search radius shrinks at edges.
//
// The corrected level is written to _CorrectedNeighborhoodSizeMap_RW.
// Dispatched as an 8x8 2-D grid over the full screen.
// -----------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void ApplyAdaptiveGradientCorrection(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)_ScreenParams.x || id.y >= (uint)_ScreenParams.y)
        return;

    uint2 fullResUV   = id.xy;
    int   level        = _NeighborhoodSizeMap[fullResUV];
    int   correctedLevel = level;

    if (level > 0)
    {
        float gradient = 0.0;
        uint2 uv_lowres;

        // Select the pyramid level whose resolution matches the neighbourhood.
        // Level 1 -> pyramid L1 (1/2), level 2 -> L2 (1/4), etc.
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
        else // level >= 4
        {
            uv_lowres = fullResUV / 16u;
            gradient  = SobelOnPyramid(_DepthPyramidL4, uv_lowres);
        }

        // If a strong depth edge is detected, reduce the LOD by 1 to keep the
        // occlusion search within the same surface.
        if (gradient > _GradientThreshold_g_th)
        {
            correctedLevel = max(0, level - 1);
        }
    }
    _CorrectedNeighborhoodSizeMap_RW[fullResUV] = correctedLevel;
}

// -----------------------------------------------------------------------------
// Kernel 9 -- OcclusionAndFilter
// Main per-pixel occlusion kernel.  Determines whether each projected point
// should be visible (and at what opacity) given the surrounding geometry and
// any virtual (URP) objects.
//
// The kernel handles three distinct cases:
//
// Case A -- No point projected here (depth == DEPTH_MAX_UINT):
//   Attempt to fill the empty pixel by averaging the colors of nearby valid
//   pixels weighted by inverse squared distance (small-radius dilation).
//   If a virtual object is present at this pixel and it is closer than the
//   fill candidates, virtual depth is used as a threshold to exclude
//   background fill.
//
// Case B -- Virtual object is closer than the real point
//   (vDepth + bias < pointDepth):
//   The virtual object occludes this point; write transparent black.
//
// Case C -- Real point is in front (or no virtual object):
//   Compute an occlusion score by examining a (2*radius+1)^2 neighbourhood
//   of depth-map pixels.  For each valid neighbour, the score measures how
//   much the vector from the current point to the neighbour deviates from
//   the view direction (a proxy for the point being hidden inside a surface).
//   The average score is mapped to an opacity via a step or smoothstep over
//   [_OcclusionThreshold, _OcclusionThreshold + _OcclusionFadeWidth].
//
// Dispatched as an 8x8 2-D grid over the full screen.
// -----------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void OcclusionAndFilter(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)_ScreenParams.x || id.y >= (uint)_ScreenParams.y)
        return;

    uint pointDepth_uint = _DepthMap[id.xy];

    // --- Read virtual (URP camera) depth if enabled ---
    uint vDepth_uint  = DEPTH_MAX_UINT;
    bool hasVirtualObj = false;

    if (_UseVirtualDepth > 0)
    {
        float vDepthRaw = _VirtualDepthMap[id.xy];
        // Unity's depth buffer is 0 at the far plane and 1 at the near plane;
        // invert so that 0 = near, 1 = far (same convention as the point depth).
        float vDepth = 1.0 - vDepthRaw;

        if (vDepth < 0.9999) // Ignore sky/background pixels
        {
            hasVirtualObj = true;
            vDepth_uint   = (uint)(vDepth * (float)DEPTH_MAX_UINT);
        }
    }

    // =========================================================================
    // Case A: No point at this pixel -- try to fill from neighbours
    // =========================================================================
    if (pointDepth_uint >= DEPTH_MAX_UINT)
    {
        int   fillRadius         = 2;
        float totalWeight        = 0.0;
        float4 accumulatedColor  = float4(0, 0, 0, 0);
        float weightedOriginSum  = 0.0;

        // When a virtual object is present, only accept fill candidates that
        // are in front of the virtual surface (avoids filling with background
        // color behind a virtual object).
        uint thresholdDepth = (hasVirtualObj) ? vDepth_uint : DEPTH_MAX_UINT;

        for (int y = -fillRadius; y <= fillRadius; y++)
        {
            for (int x = -fillRadius; x <= fillRadius; x++)
            {
                if (x == 0 && y == 0)
                    continue; // Skip the centre (it has no depth)

                // Weight falls off with squared distance from the centre.
                float distSq = dot(float2(x, y), float2(x, y));
                float weight = 1.0 / (1.0 + distSq * 0.5);

                int2  offset   = int2(x, y);
                uint2 uv       = clamp((int2)id.xy + offset, 0, (int2)_ScreenParams.xy - 1);
                uint  nDepth_uint = _DepthMap[uv];

                if (nDepth_uint < DEPTH_MAX_UINT)
                {
                    if (nDepth_uint < thresholdDepth)
                    {
                        // Accumulate weighted color and track origin type.
                        float4 c = _ColorMap[uv];
                        accumulatedColor  += c * weight;
                        totalWeight       += weight;

                        uint nType = _OriginTypeMap[uv];
                        weightedOriginSum += (float)nType * weight;
                    }
                }
            }
        }

        // Write fill result if enough weight was gathered.
        if (totalWeight > 0.3)
        {
            _OcclusionResultMap_RW[id.xy] = accumulatedColor / totalWeight;

            // Determine origin type of the filled pixel from the weighted average.
            float avgType = weightedOriginSum / totalWeight;
            if (avgType < 0.5) // Majority is PointCloud
            {
                _OriginMap_RW[id.xy]     = float4(0, 0, 0, 1);
                _OriginTypeMap_RW[id.xy] = 0u;
            }
            else               // Majority is StaticMesh
            {
                _OriginMap_RW[id.xy]     = float4(1, 1, 1, 1);
                _OriginTypeMap_RW[id.xy] = 1u;
            }
        }
        else
        {
            // Not enough fill candidates -- leave transparent.
            _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0);

            if (hasVirtualObj)
            {
                // Mark as virtual/static so compositing layers can handle it.
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

    // =========================================================================
    // Case B: Virtual object is closer than the point cloud surface
    // (with a small depth bias to avoid z-fighting)
    // =========================================================================
    uint depthBias = DEPTH_MAX_UINT / 1000;

    if (hasVirtualObj && (vDepth_uint + depthBias) < pointDepth_uint)
    {
        // The virtual object is in front; this point is occluded.
        _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0);
        _OriginMap_RW[id.xy]          = float4(1, 1, 1, 1);
        _OriginTypeMap_RW[id.xy]      = 1u;
        return;
    }

    // =========================================================================
    // Case C: Real point is visible -- compute occlusion score from neighbours
    // =========================================================================
    float3 currentPos   = _ViewPositionMap[id.xy].xyz;
    float  currentDepth = _ViewPositionMap[id.xy].w;

    // LOD-dependent search radius: radius = 2^level (minimum 1 pixel)
    int  level  = _FinalNeighborhoodSizeMap[id.xy];
    uint radius = (uint)pow(2.0, (float)level);
    radius = max(radius, 1u);

    float occlusionSum  = 0.0;
    uint  neighborCount = 0u;

    // Iterate over the (2*radius+1) x (2*radius+1) neighbourhood.
    for (uint y = 0u; y <= radius * 2u; y++)
    {
        for (uint x = 0u; x <= radius * 2u; x++)
        {
            if (x == radius && y == radius)
                continue; // Skip the centre pixel

            int2  offset           = int2((int)x - (int)radius, (int)y - (int)radius);
            uint2 uv               = clamp((int2)id.xy + offset, 0, (int2)_ScreenParams.xy - 1);
            uint  neighborDepth_uint = _DepthMap[uv];

            if (neighborDepth_uint < DEPTH_MAX_UINT)
            {
                float3 neighborPos   = _ViewPositionMap[uv].xyz;
                float  neighborDepth = _ViewPositionMap[uv].w;

                /*
                if (currentDepth - neighborDepth > 0.01)
                {
                    float3 y_minus_x = neighborPos - currentPos;
                    float3 minus_y = -neighborPos;

                    if (length(y_minus_x) > 1e-6 && length(minus_y) > 1e-6)
                    {
                        float occlusionValue = 1.0 - dot(normalize(y_minus_x), normalize(minus_y));
                        occlusionSum += occlusionValue;
                        neighborCount++;
                    }
                }*/

                // Occlusion score: 1 - cos(angle between view-direction of
                // neighbour and direction from current to neighbour).
                // A score near 0 means the neighbour is in roughly the same
                // direction as seen from the camera -> low occlusion.
                // A score near 2 means the current point is behind the
                // neighbour -> high occlusion.
                float3 y_minus_x = neighborPos - currentPos;
                float3 minus_y   = -neighborPos;

                if (length(y_minus_x) > 1e-6 && length(minus_y) > 1e-6)
                {
                    float occlusionValue = 1.0 - dot(normalize(y_minus_x), normalize(minus_y));
                    occlusionSum  += occlusionValue;
                    neighborCount++;
                }
            }
        }
    }

    // --- Map average occlusion score to pixel opacity ---
    float alpha = 1.0;
    if (neighborCount > 0)
    {
        float occlusionAverage = occlusionSum / (float)neighborCount;

        // Optionally record the raw occlusion value for debugging.
        if (_RecordOcclusionDebug > 0)
        {
            _OcclusionValueMap_RW[id.xy] = occlusionAverage;
        }

        if (_OcclusionFadeWidth > 1e-4)
        {
            // Smooth fade between _OcclusionThreshold and threshold+fadeWidth.
            float fadeEnd = _OcclusionThreshold + _OcclusionFadeWidth;
            alpha = smoothstep(_OcclusionThreshold, fadeEnd, occlusionAverage);
        }
        else
        {
            // Hard threshold: fully hide the point if below the threshold.
            if (occlusionAverage < _OcclusionThreshold)
            {
                alpha = 0.0;
            }
        }
    }

    // --- Write the final occlusion result for this pixel ---
    if (alpha <= 0.0)
    {
        // Point is hidden by occlusion; write transparent black.
        _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0);
    }
    else
    {
        // Point is visible; apply the opacity and write the color.
        float4 col = _ColorMap[id.xy];
        col.a *= alpha;
        _OcclusionResultMap_RW[id.xy] = col;

        // Update the origin debug map based on the point's source type.
        uint originType = _OriginTypeMap[id.xy];
        if (originType == 0u)
            _OriginMap_RW[id.xy] = float4(0, 0, 0, 1); // PointCloud -> black
        else if (originType == 1u)
            _OriginMap_RW[id.xy] = float4(1, 1, 1, 1); // StaticMesh -> white
    }
}

#endif // PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED