// PCD_Occlusion_Helpers.hlsl
// Utility / helper functions shared by multiple occlusion kernels.
//
// Functions defined here:
//   ZMinDownsample  - 2x2 min-downsampling step used to build the depth pyramid
//   SobelOnPyramid  - Sobel edge-detection on a uint depth pyramid level

// Helper Functions
#ifndef PCD_OCCLUSION_HELPERS_INCLUDED
#define PCD_OCCLUSION_HELPERS_INCLUDED

// ZMinDownsample
// Performs a single 2x2 min-downsampling step on a uint depth texture.
// Returns the minimum depth value found among the four texels at
// (uv), (uv+1,0), (uv+0,1), (uv+1,1).
// Used when building each level of the depth mip-pyramid (BuildDepthPyramidL1-L4).
uint ZMinDownsample(Texture2D<uint> inputTex, uint2 uv)
{
    uint z0 = inputTex[uv + uint2(0, 0)];
    uint z1 = inputTex[uv + uint2(1, 0)];
    uint z2 = inputTex[uv + uint2(0, 1)];
    uint z3 = inputTex[uv + uint2(1, 1)];
    return min(min(z0, z1), min(z2, z3));
}

// SobelOnPyramid
// Computes the Sobel gradient magnitude at texel 'uv' in a uint depth pyramid level.
// Depth values are normalised to [0, 1] before computing horizontal (Gx) and
// vertical (Gy) gradient components using the standard 3x3 Sobel kernels.
// If any of the 8 neighbours exceeds DEPTH_MAX_UINT (i.e. contains no real depth),
// 0.0 is returned so that boundary pixels are never falsely detected as edges.
// Used by ApplyAdaptiveGradientCorrection to decide whether to reduce the
// neighborhood LOD level at high-gradient (depth-edge) locations.
float SobelOnPyramid(Texture2D<uint> pyramidTex, uint2 uv)
{
    uint2 dim;
    pyramidTex.GetDimensions(dim.x, dim.y);

    // Sample the 3x3 neighbourhood, clamping to texture bounds
    float tl = (float) pyramidTex[clamp(uv + int2(-1, -1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float t  = (float) pyramidTex[clamp(uv + int2( 0, -1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float tr = (float) pyramidTex[clamp(uv + int2( 1, -1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float l  = (float) pyramidTex[clamp(uv + int2(-1,  0), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float r  = (float) pyramidTex[clamp(uv + int2( 1,  0), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float bl = (float) pyramidTex[clamp(uv + int2(-1,  1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float b  = (float) pyramidTex[clamp(uv + int2( 0,  1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float br = (float) pyramidTex[clamp(uv + int2( 1,  1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;

    // If any neighbour has no depth data, treat the gradient as zero to avoid false edges
    const float MAX_DEPTH_NORMALIZED = 1.0 + 0.1;
    if (tl > MAX_DEPTH_NORMALIZED || t > MAX_DEPTH_NORMALIZED || tr > MAX_DEPTH_NORMALIZED ||
        l  > MAX_DEPTH_NORMALIZED || r > MAX_DEPTH_NORMALIZED ||
        bl > MAX_DEPTH_NORMALIZED || b > MAX_DEPTH_NORMALIZED || br > MAX_DEPTH_NORMALIZED)
    {
        return 0.0;
    }

    // Sobel horizontal and vertical gradient components
    float Gx = (tr + 2.0 * r + br) - (tl + 2.0 * l + bl);
    float Gy = (bl + 2.0 * b + br) - (tl + 2.0 * t + tr);
    return sqrt(Gx * Gx + Gy * Gy);
}

#endif // PCD_OCCLUSION_HELPERS_INCLUDED