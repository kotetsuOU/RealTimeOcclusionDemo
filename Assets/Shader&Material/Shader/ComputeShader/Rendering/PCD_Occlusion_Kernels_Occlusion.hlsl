// --------------------------------------------------------------------------
// PCD_Occlusion_Kernels_Occlusion.hlsl
// Depth pyramid construction and occlusion computation kernels.
// Execution order:
//   8a-1..4  BuildDepthPyramidL1..L4       – build 4-level hierarchical depth
//   8b+8c    ApplyAdaptiveGradientCorrection – reduce level near depth edges
//   9        OcclusionAndFilter             – compute per-pixel occlusion
// --------------------------------------------------------------------------
#ifndef PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED
#define PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED

// --------------------------------------------------------------------------
// 8a-1. BuildDepthPyramidL1
// Downsamples the full-resolution depth map by 2x using ZMinDownsample,
// producing a half-resolution depth pyramid level.
// Using the minimum (nearest) value ensures the pyramid is conservative:
// a pixel at a lower resolution is only considered unoccluded if at least
// one of its four source pixels has geometry there.
// --------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void BuildDepthPyramidL1(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL1_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    // id.xy * 2 maps the L1 pixel back to the top-left of its 2x2 source block
    _DepthPyramidL1_RW[id.xy] = ZMinDownsample(_DepthMap, id.xy * 2u);
}

// --------------------------------------------------------------------------
// 8a-2. BuildDepthPyramidL2
// Further downsamples L1 to 1/4 of the original resolution.
// --------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void BuildDepthPyramidL2(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL2_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL2_RW[id.xy] = ZMinDownsample(_DepthPyramidL1, id.xy * 2u);
}

// --------------------------------------------------------------------------
// 8a-3. BuildDepthPyramidL3
// Further downsamples L2 to 1/8 of the original resolution.
// --------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void BuildDepthPyramidL3(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL3_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL3_RW[id.xy] = ZMinDownsample(_DepthPyramidL2, id.xy * 2u);
}

// --------------------------------------------------------------------------
// 8a-4. BuildDepthPyramidL4
// Further downsamples L3 to 1/16 of the original resolution.
// --------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void BuildDepthPyramidL4(uint3 id : SV_DispatchThreadID)
{
    uint2 dim;
    _DepthPyramidL4_RW.GetDimensions(dim.x, dim.y);
    if (id.x >= dim.x || id.y >= dim.y)
        return;
    _DepthPyramidL4_RW[id.xy] = ZMinDownsample(_DepthPyramidL3, id.xy * 2u);
}

// --------------------------------------------------------------------------
// 8b+8c. ApplyAdaptiveGradientCorrection
// Adjusts the per-pixel neighborhood level by detecting depth edges in the
// depth pyramid at the level assigned to each pixel.
//
// Why?  A large neighborhood level works well in smooth, uniformly dense
// areas, but at depth discontinuities (object boundaries) it would average
// depth values across different surfaces.  By evaluating the Sobel gradient
// magnitude at the appropriate pyramid level and reducing the level by 1
// when the gradient exceeds _GradientThreshold_g_th, we prevent the
// neighborhood from spanning across depth edges.
//
// Level-to-pyramid mapping:
//   level 1 -> L1 (1/2 res), sampled at fullRes / 2
//   level 2 -> L2 (1/4 res), sampled at fullRes / 4
//   level 3 -> L3 (1/8 res), sampled at fullRes / 8
//   level >= 4 -> L4 (1/16 res), sampled at fullRes / 16
// --------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void ApplyAdaptiveGradientCorrection(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    uint2 fullResUV     = id.xy;
    int   level         = _NeighborhoodSizeMap[fullResUV];
    int   correctedLevel = level;

    if (level > 0)
    {
        float  gradient  = 0.0;
        uint2  uv_lowres;

        // Select the pyramid level that corresponds to the current neighborhood size
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

        // If the depth gradient exceeds the threshold, shrink the search radius
        // by one level to stay on the correct side of the edge
        if (gradient > _GradientThreshold_g_th)
        {
            correctedLevel = max(0, level - 1);
        }
    }
    _CorrectedNeighborhoodSizeMap_RW[fullResUV] = correctedLevel;
}

// --------------------------------------------------------------------------
// 9. OcclusionAndFilter
// Core kernel that computes the final per-pixel occlusion value and writes
// the result color to _OcclusionResultMap_RW.
//
// The kernel handles three distinct cases for each pixel:
//
// Case A – No point cloud geometry at this pixel (hole):
//   A small fill radius (2 pixels) is searched.  Neighbors whose depth is
//   in front of any virtual object (or unconditionally if no virtual object
//   exists) contribute weighted color to fill the hole.  The weighted
//   average is written only if the total weight exceeds a minimum threshold
//   (0.3), otherwise the pixel is marked transparent.
//
// Case B – Virtual object is closer than the point cloud:
//   The point cloud is hidden by the virtual object.  The pixel is set
//   transparent (occlusion) and tagged as StaticMesh (originType 1).
//
// Case C – Normal point cloud pixel:
//   For each neighbor within radius = 2^level pixels, the occlusion
//   contribution is:
//     occlusionValue = 1 - dot(normalize(neighbor - current),
//                              normalize(-neighbor))
//   This is proportional to the angle between the vector from the current
//   point to its neighbor and the vector from the neighbor to the camera
//   origin.  A value near 0 means the current point is well-visible from
//   the camera relative to its surroundings; a value near 2 means it is
//   strongly self-occluded.
//   The per-neighbor values are averaged and then thresholded / faded using
//   _OcclusionThreshold and _OcclusionFadeWidth (smoothstep when > 0).
// --------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void OcclusionAndFilter(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    uint pointDepth_uint = _DepthMap[id.xy];

    // --- Fetch virtual object depth if enabled ---
    uint vDepth_uint  = DEPTH_MAX_UINT;
    bool hasVirtualObj = false;

    if (_UseVirtualDepth > 0)
    {
        float vDepthRaw = _VirtualDepthMap[id.xy];
        // URP stores depth as 0=near, 1=far; invert to match NDC convention (0=near)
        float vDepth    = 1.0 - vDepthRaw;

        if (vDepth < 0.9999) // pixel has a virtual object (not background)
        {
            hasVirtualObj = true;
            vDepth_uint   = (uint) (vDepth * (float) DEPTH_MAX_UINT);
        }
    }

    // ----------------------------------------------------------------
    // Case A: No point cloud geometry at this pixel – attempt hole fill
    // ----------------------------------------------------------------
    if (pointDepth_uint >= DEPTH_MAX_UINT)
    {
        int   fillRadius         = 2;
        float totalWeight        = 0.0;
        float4 accumulatedColor  = float4(0, 0, 0, 0);
        float weightedOriginSum  = 0.0;

        // Only accept neighbors that are in front of any virtual object
        uint thresholdDepth = (hasVirtualObj) ? vDepth_uint : DEPTH_MAX_UINT;

        for (int y = -fillRadius; y <= fillRadius; y++)
        {
            for (int x = -fillRadius; x <= fillRadius; x++)
            {
                if (x == 0 && y == 0)
                    continue; // skip the center pixel (it has no data)

                // Inverse-distance weight: closer neighbors contribute more
                float distSq = dot(float2(x, y), float2(x, y));
                float weight = 1.0 / (1.0 + distSq * 0.5);

                int2  offset  = int2(x, y);
                uint2 uv      = clamp((int2) id.xy + offset, 0, (int2) _ScreenParams.xy - 1);

                uint nDepth_uint = _DepthMap[uv];

                if (nDepth_uint < DEPTH_MAX_UINT)
                {
                    if (nDepth_uint < thresholdDepth) // only use pixels in front of virtual object
                    {
                        float4 c = _ColorMap[uv];
                        accumulatedColor   += c * weight;
                        totalWeight        += weight;

                        // Track the predominant origin type for the filled pixel
                        uint nType = _OriginTypeMap[uv];
                        weightedOriginSum += (float) nType * weight;
                    }
                }
            }
        }

        if (totalWeight > 0.3) // sufficient neighbor coverage – write filled color
        {
            _OcclusionResultMap_RW[id.xy] = accumulatedColor / totalWeight;

            // Classify as PointCloud (0) or StaticMesh (1) based on weighted average
            float avgType = weightedOriginSum / totalWeight;
            if (avgType < 0.5)
            {
                _OriginMap_RW[id.xy]     = float4(0, 0, 0, 1);
                _OriginTypeMap_RW[id.xy] = 0u;
            }
            else
            {
                _OriginMap_RW[id.xy]     = float4(1, 1, 1, 1);
                _OriginTypeMap_RW[id.xy] = 1u;
            }
        }
        else // not enough neighbors – leave transparent
        {
            _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0);

            if (hasVirtualObj)
            {
                // The empty region belongs to the virtual object's silhouette
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

    // ----------------------------------------------------------------
    // Case B: Virtual object is in front of the point cloud pixel
    // A small depth bias prevents z-fighting at exactly equal depths.
    // ----------------------------------------------------------------
    uint depthBias = DEPTH_MAX_UINT / 1000;

    if (hasVirtualObj && (vDepth_uint + depthBias) < pointDepth_uint)
    {
        // Virtual object occludes this point cloud pixel
        _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0);
        _OriginMap_RW[id.xy]          = float4(1, 1, 1, 1);
        _OriginTypeMap_RW[id.xy]      = 1u;
        return;
    }

    // ----------------------------------------------------------------
    // Case C: Normal point cloud pixel – compute occlusion value
    // ----------------------------------------------------------------
    float3 currentPos   = _ViewPositionMap[id.xy].xyz;
    float  currentDepth = _ViewPositionMap[id.xy].w;

    // Search radius = 2^level pixels (at least 1)
    int  level  = _FinalNeighborhoodSizeMap[id.xy];
    uint radius = (uint) pow(2.0, (float) level);
    radius      = max(radius, 1u);

    float occlusionSum  = 0.0;
    uint  neighborCount = 0u;

    for (uint y = 0u; y <= radius * 2u; y++)
    {
        for (uint x = 0u; x <= radius * 2u; x++)
        {
            if (x == radius && y == radius)
                continue; // skip the center pixel (comparing with itself is meaningless)

            int2  offset = int2((int) x - (int) radius, (int) y - (int) radius);
            uint2 uv     = clamp((int2) id.xy + offset, 0, (int2) _ScreenParams.xy - 1);

            uint neighborDepth_uint = _DepthMap[uv];

            if (neighborDepth_uint < DEPTH_MAX_UINT)
            {
                float3 neighborPos = _ViewPositionMap[uv].xyz;

                /*
                // (Disabled) Depth-gated variant: only consider neighbors that
                // are closer than the current pixel to avoid back-face influence.
                if (currentDepth - neighborDepth > 0.01)
                {
                    float3 y_minus_x = neighborPos - currentPos;
                    float3 minus_y   = -neighborPos;

                    if (length(y_minus_x) > 1e-6 && length(minus_y) > 1e-6)
                    {
                        float occlusionValue = 1.0 - dot(normalize(y_minus_x), normalize(minus_y));
                        occlusionSum += occlusionValue;
                        neighborCount++;
                    }
                }*/

                // Compute the angle-based occlusion contribution:
                //   y_minus_x : vector from current point to neighbor (direction of occlusion)
                //   minus_y   : vector from neighbor to the camera origin (direction of view)
                // dot product = 1 when they align (not occluded), near -1 when opposed (fully occluded)
                // 1 - dot maps this to [0, 2]: 0 = no occlusion, ~2 = strong occlusion
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

    // --- Map average occlusion to an alpha value ---
    float alpha = 1.0;
    if (neighborCount > 0)
    {
        float occlusionAverage = occlusionSum / (float) neighborCount;

        // Optionally record the raw occlusion value for debug visualization
        if (_RecordOcclusionDebug > 0)
        {
            _OcclusionValueMap_RW[id.xy] = occlusionAverage;
        }

        if (_OcclusionFadeWidth > 1e-4)
        {
            // Smooth fade: alpha goes from 0 at _OcclusionThreshold to 1 at threshold+fadeWidth
            float fadeEnd = _OcclusionThreshold + _OcclusionFadeWidth;
            alpha = smoothstep(_OcclusionThreshold, fadeEnd, occlusionAverage);
        }
        else
        {
            // Hard threshold: pixel is either fully visible or fully occluded
            if (occlusionAverage < _OcclusionThreshold)
            {
                alpha = 0.0;
            }
        }
    }

    // Write the final color, modulated by the computed alpha
    if (alpha <= 0.0)
    {
        _OcclusionResultMap_RW[id.xy] = float4(0, 0, 0, 0); // fully occluded
    }
    else
    {
        float4 col = _ColorMap[id.xy];
        col.a     *= alpha; // blend original alpha with occlusion fade
        _OcclusionResultMap_RW[id.xy] = col;

        // Propagate origin type to the origin debug map
        uint originType = _OriginTypeMap[id.xy];
        if (originType == 0u)
            _OriginMap_RW[id.xy] = float4(0, 0, 0, 1); // PointCloud -> black
        else if (originType == 1u)
            _OriginMap_RW[id.xy] = float4(1, 1, 1, 1); // StaticMesh -> white
    }
}

#endif // PCD_OCCLUSION_KERNELS_OCCLUSION_INCLUDED