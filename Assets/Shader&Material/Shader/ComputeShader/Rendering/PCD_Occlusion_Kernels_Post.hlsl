// =============================================================================
// PCD_Occlusion_Kernels_Post.hlsl
// Post-processing kernels: hole-filling interpolation and camera-depth
// initialization for virtual object compositing.
// =============================================================================
#ifndef PCD_OCCLUSION_KERNELS_POST_INCLUDED
#define PCD_OCCLUSION_KERNELS_POST_INCLUDED

// -----------------------------------------------------------------------------
// Kernel 10 -- Interpolate (Simple Dilation / Hole Filling)
// Copies occluded/visible point colors from _OcclusionResultMap into
// _FinalImage_RW.  Pixels that already have an alpha > 0 are copied directly.
// Pixels with alpha == 0 (holes) are filled by averaging the colors of their
// 8 immediate neighbours that have alpha > 0 (1-pixel dilation).
//
// The origin type of a filled pixel is inherited from the neighbour with the
// lowest originType value (PointCloud < StaticMesh < Background), so that a
// filled pixel is attributed to the most "real" nearby surface.
//
// Dispatched as an 8x8 2-D grid over the full screen.
// -----------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void Interpolate(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)_ScreenParams.x || id.y >= (uint)_ScreenParams.y)
        return;

    float4 centerColor = _OcclusionResultMap[id.xy];

    // If this pixel already has a visible color, write it directly and exit.
    if (centerColor.a > 0)
    {
        _FinalImage_RW[id.xy] = centerColor;
        return;
    }

    // --- Hole filling: average valid 8-neighbours ---
    float4 accumulatedColor  = float4(0, 0, 0, 0);
    uint   count             = 0u;
    uint   chosenOriginType  = 2u; // Start with Background (highest value)

    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0)
                continue; // Skip centre

            uint2  uv            = clamp(id.xy + int2(x, y), 0, _ScreenParams.xy - 1);
            float4 neighborColor = _OcclusionResultMap[uv];

            if (neighborColor.a > 0)
            {
                accumulatedColor += neighborColor;
                count++;

                // Prefer the origin type with the lowest value (PointCloud wins).
                uint neighborOriginType = _OriginTypeMap[uv];
                if (neighborOriginType < chosenOriginType)
                {
                    chosenOriginType = neighborOriginType;
                }
            }
        }
    }

    if (count > 0u)
    {
        // Write the average color of valid neighbours.
        _FinalImage_RW[id.xy] = accumulatedColor / (float)count;

        // Update the origin debug map with the winning origin type.
        if (chosenOriginType == 0u)
            _OriginMap_RW[id.xy] = float4(0, 0, 0, 1); // PointCloud -> black
        else if (chosenOriginType == 1u)
            _OriginMap_RW[id.xy] = float4(1, 1, 1, 1); // StaticMesh -> white
    }
    else
    {
        // No valid neighbours; keep the pixel transparent black.
        _FinalImage_RW[id.xy] = float4(0, 0, 0, 0);
    }
}

// -----------------------------------------------------------------------------
// Kernel 11 -- InitFromCamera
// Pre-populates the depth/color/viewPosition maps from the URP camera's color
// and depth buffers.  This allows rendered virtual (StaticMesh) objects to
// participate in the same depth-test and density pipeline as the point cloud.
//
// For each pixel:
//   - If the camera depth is at the far plane (>= 0.9999), treat the pixel as
//     background and initialize maps to their empty values.
//   - Otherwise, encode the depth as uint, store the camera color, reconstruct
//     the view-space position from the depth value using the inverse projection
//     matrix, and mark the pixel as StaticMesh (originType = 1).
//
// Dispatched as an 8x8 2-D grid over the full screen.
// Note: Must be dispatched BEFORE ProjectPoints so that point-cloud pixels can
// overwrite camera pixels when they are closer.
// -----------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void InitFromCamera(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)_ScreenParams.x || id.y >= (uint)_ScreenParams.y)
        return;

    float cameraDepth = _VirtualDepthMap[id.xy];

    // Far-plane pixels are treated as background; clear maps to empty.
    if (cameraDepth >= 0.9999)
    {
        _DepthMap_RW[id.xy]        = DEPTH_MAX_UINT;
        _ColorMap_RW[id.xy]        = float4(0, 0, 0, 0);
        _ViewPositionMap_RW[id.xy] = float4(0, 0, 0, 1e9);
        _OriginTypeMap_RW[id.xy]   = 2u; // Background
        return;
    }

    // --- Encode depth and write camera color ---
    uint depth_uint          = (uint)(cameraDepth * (float)DEPTH_MAX_UINT);
    _DepthMap_RW[id.xy]      = depth_uint;

    float4 cameraColor       = _CameraColorTexture[id.xy];
    _ColorMap_RW[id.xy]      = float4(cameraColor.rgb, 1.0);

    // --- Reconstruct view-space position from depth via inverse projection ---
    // Convert pixel coordinate to normalized UV, then to NDC [-1, 1].
    float2 uv    = float2(id.xy) / _ScreenParams.xy;
    float2 ndc   = uv * 2.0 - 1.0;

    // Unity's clip-space depth convention: z in [-1, 1] for clip, but the
    // depth buffer stores [0, 1]; remap to [-1, 1] for the inverse projection.
    float4 clipPos = float4(ndc.x, ndc.y, cameraDepth * 2.0 - 1.0, 1.0);
    float4 viewPos = mul(_InverseProjectionMatrix, clipPos);
    viewPos /= viewPos.w; // Perspective divide to get view-space position

    _ViewPositionMap_RW[id.xy] = float4(viewPos.xyz, cameraDepth);
    _OriginTypeMap_RW[id.xy]   = 1u; // StaticMesh
}

#endif // PCD_OCCLUSION_KERNELS_POST_INCLUDED