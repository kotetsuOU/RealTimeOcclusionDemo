// --------------------------------------------------------------------------
// PCD_Occlusion_Kernels_Post.hlsl
// Post-processing kernels that run after the main occlusion pass.
// Execution order:
//   10. Interpolate     – dilate the occlusion result to fill 1-pixel holes
//   11. InitFromCamera  – seed depth/color maps from the URP camera textures
// --------------------------------------------------------------------------
#ifndef PCD_OCCLUSION_KERNELS_POST_INCLUDED
#define PCD_OCCLUSION_KERNELS_POST_INCLUDED

// --------------------------------------------------------------------------
// 10. Interpolate (Simple Dilation / Hole Fill)
// Fills pixels that have no valid occlusion result (alpha == 0) by averaging
// the colors of their 8-connected neighbors that do have valid results.
//
// Why dilation?  The occlusion pass can leave 1-pixel gaps along point cloud
// boundaries due to projection discretization.  A single-pass 3x3 dilation
// is a cheap way to close those gaps without blurring the content.
//
// Origin type of the filled pixel is taken from the neighbor with the
// lowest originType value (PointCloud < StaticMesh < Background), so that
// any dynamic point nearby takes priority over static mesh classification.
// --------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void Interpolate(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    float4 centerColor = _OcclusionResultMap[id.xy];

    // If the current pixel already has a valid color, pass it through directly
    if (centerColor.a > 0)
    {
        _FinalImage_RW[id.xy] = centerColor;
        return;
    }

    // Accumulate valid neighbor colors and track the dominant origin type
    float4 accumulatedColor  = float4(0, 0, 0, 0);
    uint   count             = 0u;
    uint   chosenOriginType  = 2u; // default to Background until a better neighbor is found

    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0)
                continue; // skip center (it has no data)

            uint2  uv           = clamp(id.xy + int2(x, y), 0, _ScreenParams.xy - 1);
            float4 neighborColor = _OcclusionResultMap[uv];

            if (neighborColor.a > 0)
            {
                accumulatedColor += neighborColor;
                count++;

                // Keep the most "foreground" origin type among valid neighbors
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
        _FinalImage_RW[id.xy] = accumulatedColor / (float) count; // simple average

        // Write origin map for the newly filled pixel
        if (chosenOriginType == 0u)
            _OriginMap_RW[id.xy] = float4(0, 0, 0, 1); // PointCloud -> black
        else if (chosenOriginType == 1u)
            _OriginMap_RW[id.xy] = float4(1, 1, 1, 1); // StaticMesh -> white
    }
    else
    {
        _FinalImage_RW[id.xy] = float4(0, 0, 0, 0); // no valid neighbors, stay transparent
    }
}

// --------------------------------------------------------------------------
// 11. InitFromCamera
// Seeds the depth and color maps from the URP camera's render textures so
// that virtual (rendered) objects participate in the occlusion pipeline.
// This kernel is dispatched before ProjectPoints when _UseVirtualDepth > 0.
//
// For each screen pixel:
//   - If the camera depth is at (or near) the far plane (>= 0.9999) the
//     pixel has no geometry; write sentinel values and mark as Background.
//   - Otherwise, convert the camera depth to the same uint encoding used by
//     ProjectPoints, copy the camera color, and unproject the pixel from
//     clip space back to view space using the inverse projection matrix so
//     that OcclusionAndFilter can use the view-space position.
//   - All camera-sourced pixels are classified as StaticMesh (originType 1)
//     to distinguish them from dynamic point cloud pixels.
// --------------------------------------------------------------------------
[numthreads(8, 8, 1)]
void InitFromCamera(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint) _ScreenParams.x || id.y >= (uint) _ScreenParams.y)
        return;

    float cameraDepth = _VirtualDepthMap[id.xy];

    // Far-plane pixels have no virtual geometry; reset to empty sentinels
    if (cameraDepth >= 0.9999)
    {
        _DepthMap_RW[id.xy]        = DEPTH_MAX_UINT;
        _ColorMap_RW[id.xy]        = float4(0, 0, 0, 0);
        _ViewPositionMap_RW[id.xy] = float4(0, 0, 0, 1e9);
        _OriginTypeMap_RW[id.xy]   = 2u; // Background
        return;
    }

    // Encode camera depth using the same uint scale as point cloud pixels
    uint depth_uint = (uint) (cameraDepth * (float) DEPTH_MAX_UINT);
    _DepthMap_RW[id.xy] = depth_uint;

    // Copy the camera color with full opacity
    float4 cameraColor = _CameraColorTexture[id.xy];
    _ColorMap_RW[id.xy] = float4(cameraColor.rgb, 1.0);

    // Unproject pixel -> NDC -> clip space -> view space
    // The inverse projection matrix transforms clip-space coordinates back
    // to view space, giving a 3D position usable by the occlusion kernel.
    float2 uv      = float2(id.xy) / _ScreenParams.xy;
    float2 ndc     = uv * 2.0 - 1.0;  // [0,1] -> [-1,1]
    float4 clipPos = float4(ndc.x, ndc.y, cameraDepth * 2.0 - 1.0, 1.0); // OpenGL clip-space z convention
    float4 viewPos = mul(_InverseProjectionMatrix, clipPos);
    viewPos       /= viewPos.w; // perspective divide

    _ViewPositionMap_RW[id.xy] = float4(viewPos.xyz, cameraDepth);
    _OriginTypeMap_RW[id.xy]   = 1u; // StaticMesh (virtual rendered object)
}

#endif // PCD_OCCLUSION_KERNELS_POST_INCLUDED