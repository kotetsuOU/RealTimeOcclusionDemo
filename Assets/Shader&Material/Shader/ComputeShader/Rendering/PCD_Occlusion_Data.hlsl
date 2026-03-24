// PCD_Occlusion_Data.hlsl
// Shared data definitions for the PCD Occlusion pipeline.
// This file declares all structured buffers, render textures, uniforms,
// constants, and groupshared variables used across the pipeline kernels.
// It must be included before any kernel implementation header.

// Data Structures
#ifndef PCD_OCCLUSION_DATA_INCLUDED
#define PCD_OCCLUSION_DATA_INCLUDED

// A single point in the point cloud.
//   position   - 3-D world-space position
//   color      - linear RGB color
//   originType - source tag: 0 = PointCloud (dynamic), 1 = StaticMesh
struct Point
{
    float3 position;
    float3 color;
    uint originType; // 0 = PointCloud, 1 = StaticMesh
};

// ---------------------------------------------------------------------------
// Read/Write render textures (written by compute kernels)
// ---------------------------------------------------------------------------

StructuredBuffer<Point> _PointBuffer;       // Input point cloud (read-only)
RWTexture2D<float4> _ColorMap_RW;           // Per-pixel color written during projection
RWTexture2D<uint>   _DepthMap_RW;           // Per-pixel depth (uint-encoded NDC z) written during projection
RWTexture2D<float4> _ViewPositionMap_RW;    // Per-pixel view-space XYZ + NDC depth
RWTexture2D<uint>   _GridZMinMap_RW;        // Per-grid minimum depth value
RWTexture2D<float>  _DensityMap_RW;         // Per-grid point density (0-1)
RWTexture2D<int>    _GridLevelMap_RW;       // Per-grid LOD level derived from density
RWTexture2D<int>    _FilteredGridLevelMap_RW;    // Median-filtered version of _GridLevelMap
RWTexture2D<int>    _NeighborhoodSizeMap_RW;     // Per-pixel neighborhood radius (from filtered level)
RWTexture2D<uint>   _DepthPyramidL1_RW;     // Depth mip-pyramid level 1 (1/2 resolution)
RWTexture2D<uint>   _DepthPyramidL2_RW;     // Depth mip-pyramid level 2 (1/4 resolution)
RWTexture2D<uint>   _DepthPyramidL3_RW;     // Depth mip-pyramid level 3 (1/8 resolution)
RWTexture2D<uint>   _DepthPyramidL4_RW;     // Depth mip-pyramid level 4 (1/16 resolution)
RWTexture2D<int>    _CorrectedNeighborhoodSizeMap_RW; // Gradient-corrected neighborhood size
RWTexture2D<uint>   _OriginTypeMap_RW;      // Per-pixel origin type (0=PointCloud, 1=StaticMesh, 2=Background)
RWTexture2D<float4> _OcclusionResultMap_RW; // Per-pixel color after occlusion test (alpha encodes visibility)
RWTexture2D<float>  _OcclusionValueMap_RW;  // Debug: raw occlusion average value per pixel
RWTexture2D<float4> _FinalImage_RW;         // Final composited output after hole-fill / interpolation
RWTexture2D<float4> _OriginMap_RW;          // Encodes origin type as color (black=PointCloud, white=Mesh)

// ---------------------------------------------------------------------------
// Read-only texture versions (sampled by kernels that only read these maps)
// ---------------------------------------------------------------------------

Texture2D<float4> _ColorMap;
Texture2D<uint>   _DepthMap;
Texture2D<float4> _ViewPositionMap;
Texture2D<uint>   _GridZMinMap;
Texture2D<float>  _DensityMap;
Texture2D<int>    _GridLevelMap;
Texture2D<int>    _FilteredGridLevelMap;
Texture2D<int>    _NeighborhoodSizeMap;
Texture2D<uint>   _DepthPyramidL1;
Texture2D<uint>   _DepthPyramidL2;
Texture2D<uint>   _DepthPyramidL3;
Texture2D<uint>   _DepthPyramidL4;
Texture2D<uint>   _OriginTypeMap;
Texture2D<int>    _FinalNeighborhoodSizeMap; // Alias for the corrected neighborhood size map used in OcclusionAndFilter
Texture2D<float4> _OcclusionResultMap;

// ---------------------------------------------------------------------------
// Virtual depth integration (URP camera depth / hybrid occlusion)
// ---------------------------------------------------------------------------

Texture2D<float>  _VirtualDepthMap;       // Depth buffer from the URP camera (used for virtual object occlusion)
Texture2D<float4> _CameraColorTexture;    // Color buffer from the URP camera (used by InitFromCamera)
int _UseVirtualDepth;                      // Non-zero when virtual depth should be considered during occlusion
float4x4 _InverseProjectionMatrix;        // Inverse of the projection matrix (NDC -> view space reconstruction)
int _RecordOcclusionDebug;                 // Non-zero to write raw occlusion values to _OcclusionValueMap

// ---------------------------------------------------------------------------
// Merge buffer parameters (used by MergeBuffer kernel)
// ---------------------------------------------------------------------------

StructuredBuffer<Point>   _MergeSrcBuffer; // Source point buffer for the merge copy operation
RWStructuredBuffer<Point> _MergeDstBuffer; // Destination point buffer for the merge copy operation
int _MergeSrcOffset;  // Starting index in the source buffer
int _MergeDstOffset;  // Starting index in the destination buffer
int _MergeCopyCount;  // Number of points to copy

// ---------------------------------------------------------------------------
// Per-frame uniforms
// ---------------------------------------------------------------------------

uint     _PointCount;                // Total number of points in _PointBuffer
float4   _ScreenParams;              // xy = render target width/height in pixels
float4x4 _ViewMatrix;               // World-to-view transformation matrix
float4x4 _ProjectionMatrix;         // View-to-clip (projection) matrix
float _DensityThreshold_e;          // Depth tolerance used when counting points per grid cell
float _NeighborhoodParam_p_prime;   // Scale parameter controlling the neighborhood size formula
float _GradientThreshold_g_th;      // Sobel gradient magnitude above which the level is decreased by 1
float _OcclusionThreshold;          // Occlusion average below which a point is considered occluded
float _OcclusionFadeWidth;          // Width of the smooth fade zone around _OcclusionThreshold (0 = hard cut)

// ---------------------------------------------------------------------------
// Constants and groupshared variables
// ---------------------------------------------------------------------------

#define GRID_SIZE 16u           // Number of pixels per grid cell side (grid cells are GRID_SIZE x GRID_SIZE)
#define DEPTH_MAX_UINT 0x7FFFFFFFu  // Sentinel value representing "no depth" (maximum uint depth)

// Used within CalculateGridZMin and CalculateDensity to accumulate results across the thread group
groupshared uint shared_z_min;
groupshared uint shared_point_count;

#endif // PCD_OCCLUSION_DATA_INCLUDED