// --------------------------------------------------------------------------
// PCD_Occlusion_Helpers.hlsl
// Shared utility functions used by the occlusion pipeline kernels.
// --------------------------------------------------------------------------
#ifndef PCD_OCCLUSION_HELPERS_INCLUDED
#define PCD_OCCLUSION_HELPERS_INCLUDED

// --------------------------------------------------------------------------
// ZMinDownsample
// Reads a 2x2 block of uint depth values starting at 'uv' from 'inputTex'
// and returns the minimum value.  Used to build the conservative depth
// pyramid: taking the minimum (nearest depth) ensures that an object is
// only considered hidden when ALL four source pixels have geometry in front,
// making occlusion queries conservative and avoiding false occlusions.
// --------------------------------------------------------------------------
uint ZMinDownsample(Texture2D<uint> inputTex, uint2 uv)
{
    uint z0 = inputTex[uv + uint2(0, 0)];
    uint z1 = inputTex[uv + uint2(1, 0)];
    uint z2 = inputTex[uv + uint2(0, 1)];
    uint z3 = inputTex[uv + uint2(1, 1)];
    return min(min(z0, z1), min(z2, z3)); // conservative: keep the nearest (smallest) depth
}

// --------------------------------------------------------------------------
// SobelOnPyramid
// Computes the Sobel edge-detection magnitude at pixel 'uv' in a pyramid
// depth texture.  The result is used in ApplyAdaptiveGradientCorrection to
// detect depth discontinuities: near sharp depth edges, the neighborhood
// level is reduced to prevent blurring across object boundaries.
//
// Steps:
//   1. Sample the 3x3 neighborhood (clamped to texture bounds).
//   2. Normalize each uint depth to [0, 1].
//   3. If any sample exceeds MAX_DEPTH_NORMALIZED (i.e. it is an empty /
//      background pixel), return 0 – the boundary behaviour near missing
//      data should not be treated as a real depth edge.
//   4. Apply the standard Sobel kernels for Gx (horizontal) and Gy
//      (vertical) and return the gradient magnitude sqrt(Gx²+Gy²).
// --------------------------------------------------------------------------
float SobelOnPyramid(Texture2D<uint> pyramidTex, uint2 uv)
{
    uint2 dim;
    pyramidTex.GetDimensions(dim.x, dim.y);

    // Sample normalized depth for each of the 8 neighbors + center.
    // Coordinates are clamped so that border pixels read edge values instead
    // of wrapping or sampling undefined memory.
    float tl = (float) pyramidTex[clamp(uv + int2(-1, -1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float t  = (float) pyramidTex[clamp(uv + int2( 0, -1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float tr = (float) pyramidTex[clamp(uv + int2( 1, -1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float l  = (float) pyramidTex[clamp(uv + int2(-1,  0), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float r  = (float) pyramidTex[clamp(uv + int2( 1,  0), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float bl = (float) pyramidTex[clamp(uv + int2(-1,  1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float b  = (float) pyramidTex[clamp(uv + int2( 0,  1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;
    float br = (float) pyramidTex[clamp(uv + int2( 1,  1), 0, dim - 1)] / (float) DEPTH_MAX_UINT;

    // Reject the neighborhood if any pixel has no geometry (depth == sentinel).
    // A slight margin (+0.1) avoids floating-point imprecision at the boundary.
    const float MAX_DEPTH_NORMALIZED = 1.0 + 0.1;
    if (tl > MAX_DEPTH_NORMALIZED || t > MAX_DEPTH_NORMALIZED || tr > MAX_DEPTH_NORMALIZED ||
        l  > MAX_DEPTH_NORMALIZED || r  > MAX_DEPTH_NORMALIZED ||
        bl > MAX_DEPTH_NORMALIZED || b  > MAX_DEPTH_NORMALIZED || br > MAX_DEPTH_NORMALIZED)
    {
        return 0.0; // no valid gradient near empty pixels
    }

    // Standard 3x3 Sobel operator.
    // Gx detects left-right depth changes; Gy detects top-bottom changes.
    float Gx = (tr + 2.0 * r + br) - (tl + 2.0 * l + bl);
    float Gy = (bl + 2.0 * b + br) - (tl + 2.0 * t + tr);
    return sqrt(Gx * Gx + Gy * Gy); // gradient magnitude
}

#endif // PCD_OCCLUSION_HELPERS_INCLUDED