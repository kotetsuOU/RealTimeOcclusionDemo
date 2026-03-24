// =============================================================================
// PCD_Occlusion_Helpers.hlsl
// Utility functions shared across occlusion pipeline kernels.
// =============================================================================
#ifndef PCD_OCCLUSION_HELPERS_INCLUDED
#define PCD_OCCLUSION_HELPERS_INCLUDED

// -----------------------------------------------------------------------------
// ZMinDownsample
// Reads a 2x2 block of depth values from `inputTex` starting at `uv` and
// returns the minimum (nearest) depth among the four samples.
// Used to build each level of the min-depth mipmap pyramid, where the goal is
// to conservatively represent the closest surface in a tile.
//
// Parameters:
//   inputTex - Source depth texture (uint-encoded depth)
//   uv       - Top-left pixel coordinate of the 2x2 block to sample
//
// Returns: minimum uint-encoded depth across the 2x2 footprint
// -----------------------------------------------------------------------------
uint ZMinDownsample(Texture2D<uint> inputTex, uint2 uv)
{
    uint z0 = inputTex[uv + uint2(0, 0)];
    uint z1 = inputTex[uv + uint2(1, 0)];
    uint z2 = inputTex[uv + uint2(0, 1)];
    uint z3 = inputTex[uv + uint2(1, 1)];
    return min(min(z0, z1), min(z2, z3));
}

// -----------------------------------------------------------------------------
// SobelOnPyramid
// Computes the Sobel edge-detection gradient magnitude at pixel `uv` in a
// uint-encoded depth pyramid level.  The raw uint depth values are first
// normalized to [0, 1] before the Sobel kernel is applied.
//
// Any sample whose normalized value exceeds MAX_DEPTH_NORMALIZED is treated as
// "empty / background", and the function returns 0 to avoid false edges at the
// boundary between real geometry and empty space.
//
// Parameters:
//   pyramidTex - One level of the min-depth mipmap (uint-encoded)
//   uv         - Pixel coordinate at which to evaluate the gradient
//
// Returns: Sobel gradient magnitude (>= 0); higher values indicate a steeper
//          depth discontinuity at this location.
// -----------------------------------------------------------------------------
float SobelOnPyramid(Texture2D<uint> pyramidTex, uint2 uv)
{
    uint2 dim;
    pyramidTex.GetDimensions(dim.x, dim.y);

    // Sample all 8 neighbours, clamping to texture borders.
    float tl = (float) pyramidTex[clamp(uv + int2(-1, -1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float t  = (float) pyramidTex[clamp(uv + int2( 0, -1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float tr = (float) pyramidTex[clamp(uv + int2( 1, -1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float l  = (float) pyramidTex[clamp(uv + int2(-1,  0), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float r  = (float) pyramidTex[clamp(uv + int2( 1,  0), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float bl = (float) pyramidTex[clamp(uv + int2(-1,  1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float b  = (float) pyramidTex[clamp(uv + int2( 0,  1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float br = (float) pyramidTex[clamp(uv + int2( 1,  1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;

    // Treat pixels whose normalized depth is above 1.1 as background / invalid.
    // If any of the 3x3 neighbourhood samples is background, return 0 to suppress
    // spurious edges at the point-cloud silhouette against empty space.
    const float MAX_DEPTH_NORMALIZED = 1.0 + 0.1;
    if (tl > MAX_DEPTH_NORMALIZED || t > MAX_DEPTH_NORMALIZED || tr > MAX_DEPTH_NORMALIZED ||
        l  > MAX_DEPTH_NORMALIZED || r  > MAX_DEPTH_NORMALIZED ||
        bl > MAX_DEPTH_NORMALIZED || b  > MAX_DEPTH_NORMALIZED || br > MAX_DEPTH_NORMALIZED)
    {
        return 0.0;
    }

    // Standard 3x3 Sobel operator: horizontal (Gx) and vertical (Gy) gradients.
    float Gx = (tr + 2.0 * r + br) - (tl + 2.0 * l + bl);
    float Gy = (bl + 2.0 * b + br) - (tl + 2.0 * t + tr);
    return sqrt(Gx * Gx + Gy * Gy);
}

#endif // PCD_OCCLUSION_HELPERS_INCLUDED