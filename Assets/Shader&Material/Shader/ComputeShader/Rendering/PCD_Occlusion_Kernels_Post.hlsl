// PCD_Occlusion_Kernels_Post.hlsl
// Post-process stage kernels for the PCD Occlusion pipeline.
//
// Kernels defined here (in dispatch order):
//   10. Interpolate     - Fill remaining holes in _OcclusionResultMap via 1-pixel dilation
//   11. InitFromCamera  - Seed the depth/color maps from the URP camera depth texture
//                         (used as an alternative starting point instead of ProjectPoints)

#ifndef PCD_OCCLUSION_KERNELS_POST_INCLUDED
#define PCD_OCCLUSION_KERNELS_POST_INCLUDED

// ---------------------------------------------------------------------------
// Kernel 10: Interpolate (Simple Dilation / Hole-fill)
// For each pixel:
//   - If the pixel already has a colour in _OcclusionResultMap (alpha > 0),
//     it is copied directly to _FinalImage unchanged.
//   - Otherwise, the 8-connected neighbourhood is sampled.  If any neighbour
//     has a colour, their colours are averaged (unweighted) and written to
//     _FinalImage.  The origin type of the pixel is set to the minimum origin
//     type found among the contributing neighbours (PointCloud=0 takes
//     priority over StaticMesh=1, which takes priority over Background=2).
//   - If no coloured neighbour is found, the pixel is set to transparent black.
// This simple one-pass dilation fills single-pixel gaps left by the occlusion
// and projection steps, reducing the visual impact of sparse point cloud data.
// ---------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void Interpolate(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    float4 centerColor = _OcclusionResultMap[id.xy];

    // Pixel already has valid colour - pass it through unchanged
    if (centerColor.a > 0)
    {
        _FinalImage_RW[id.xy] = centerColor;
        return;
    }

    // Pixel is empty - average colours from the 8-connected neighbourhood
    float4 accumulatedColor = float4(0, 0, 0, 0);
    uint   count             = 0u;
    uint   chosenOriginType  = 2u; // Start with Background; lower types win

    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0)
                continue;

            uint2  uv            = clamp(id.xy + int2(x, y), 0, _ScreenParams.xy - 1);
            float4 neighborColor = _OcclusionResultMap[uv];

            if (neighborColor.a > 0)
            {
                accumulatedColor += neighborColor;
                count++;

                // Priority: PointCloud (0) > StaticMesh (1) > Background (2)
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
        _FinalImage_RW[id.xy] = accumulatedColor / (float) count;

        // Write the dominant origin type to the origin map
        if (chosenOriginType == 0u)
            _OriginMap_RW[id.xy] = float4(0, 0, 0, 1); // PointCloud -> black
        else if (chosenOriginType == 1u)
            _OriginMap_RW[id.xy] = float4(1, 1, 1, 1); // StaticMesh -> white
    }
    else
    {
        _FinalImage_RW[id.xy] = float4(0, 0, 0, 0); // No neighbours -> transparent black
    }
}

// ---------------------------------------------------------------------------
// Kernel 11: InitFromCamera
// Seeds _DepthMap, _ColorMap, _ViewPositionMap, and _OriginTypeMap from the
// URP camera colour and depth textures.  This kernel is dispatched instead of
// (or before) ProjectPoints when the pipeline should incorporate camera image
// data directly (e.g. for background fill in AR mode).
//
// - Pixels with cameraDepth >= 0.9999 are treated as background (no geometry):
//   depth is set to DEPTH_MAX_UINT, colour to transparent black.
// - For all other pixels, the camera depth is converted to uint and stored in
//   _DepthMap.  The camera colour is stored in _ColorMap.  The view-space
//   position is reconstructed from the depth value using _InverseProjectionMatrix
//   and stored in _ViewPositionMap.  The origin type is set to StaticMesh (1)
//   so that downstream kernels treat this data as a virtual/static surface.
// ---------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void InitFromCamera(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    float cameraDepth = _VirtualDepthMap[id.xy];

    // Background pixel (depth at far plane) - treat as empty
    if (cameraDepth >= 0.9999)
    {
        _DepthMap_RW[id.xy]         = DEPTH_MAX_UINT;
        _ColorMap_RW[id.xy]         = float4(0, 0, 0, 0);
        _ViewPositionMap_RW[id.xy]  = float4(0, 0, 0, 1e9);
        _OriginTypeMap_RW[id.xy]    = 2u; // Background
        return;
    }

    // Write uint-encoded depth
    uint depth_uint = (uint) (cameraDepth * (float) DEPTH_MAX_UINT);
    _DepthMap_RW[id.xy] = depth_uint;

    // Write camera colour (full opacity)
    float4 cameraColor  = _CameraColorTexture[id.xy];
    _ColorMap_RW[id.xy] = float4(cameraColor.rgb, 1.0);

    // Reconstruct view-space position from depth using the inverse projection matrix
    float2 uv      = float2(id.xy) / _ScreenParams.xy;
    float2 ndc     = uv * 2.0 - 1.0;
    float4 clipPos = float4(ndc.x, ndc.y, cameraDepth * 2.0 - 1.0, 1.0);
    float4 viewPos = mul(_InverseProjectionMatrix, clipPos);
    viewPos /= viewPos.w; // perspective divide

    _ViewPositionMap_RW[id.xy] = float4(viewPos.xyz, cameraDepth);
    _OriginTypeMap_RW[id.xy]   = 1u; // StaticMesh (virtual surface from camera)
}

#endif // PCD_OCCLUSION_KERNELS_POST_INCLUDED