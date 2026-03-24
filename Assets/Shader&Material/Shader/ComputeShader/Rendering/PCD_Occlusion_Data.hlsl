// --------------------------------------------------------------------------
// PCD_Occlusion_Data.hlsl
// Shared data declarations: structs, RW/read-only textures, uniforms, and
// group-shared variables used across all PCD occlusion kernel files.
// --------------------------------------------------------------------------
#ifndef PCD_OCCLUSION_DATA_INCLUDED
#define PCD_OCCLUSION_DATA_INCLUDED

// A single point in the merged point cloud.
// position   : world-space XYZ coordinates
// color      : linear RGB color
// originType : source classification (0 = PointCloud, 1 = StaticMesh, 2 = Background)
struct Point
{
    float3 position;
    float3 color;
    uint originType; // 0 = PointCloud, 1 = StaticMesh
};

// --------------------------------------------------------------------------
// Read-write render targets written during the pipeline passes.
// All maps share the same full-resolution screen dimensions unless noted.
// --------------------------------------------------------------------------
StructuredBuffer<Point> _PointBuffer;              // Input point cloud (read-only view)
RWTexture2D<float4> _ColorMap_RW;                  // Per-pixel color of the nearest projected point
RWTexture2D<uint>   _DepthMap_RW;                  // Per-pixel depth encoded as uint [0, DEPTH_MAX_UINT]
RWTexture2D<float4> _ViewPositionMap_RW;           // Per-pixel view-space position (xyz) + NDC depth (w)
RWTexture2D<uint>   _GridZMinMap_RW;               // Minimum depth per 16x16 grid cell (grid-space resolution)
RWTexture2D<float>  _DensityMap_RW;                // Ratio of filled pixels per grid cell [0, 1]
RWTexture2D<int>    _GridLevelMap_RW;              // Neighborhood level derived from density (grid-space)
RWTexture2D<int>    _FilteredGridLevelMap_RW;      // Median-filtered grid level (grid-space)
RWTexture2D<int>    _NeighborhoodSizeMap_RW;       // Per-pixel neighborhood level (upsampled from grid)
RWTexture2D<uint>   _DepthPyramidL1_RW;            // Depth pyramid mip 1 (1/2 resolution)
RWTexture2D<uint>   _DepthPyramidL2_RW;            // Depth pyramid mip 2 (1/4 resolution)
RWTexture2D<uint>   _DepthPyramidL3_RW;            // Depth pyramid mip 3 (1/8 resolution)
RWTexture2D<uint>   _DepthPyramidL4_RW;            // Depth pyramid mip 4 (1/16 resolution)
RWTexture2D<int>    _CorrectedNeighborhoodSizeMap_RW; // Level after gradient-based correction
RWTexture2D<uint>   _OriginTypeMap_RW;             // Per-pixel origin type (0/1/2)
RWTexture2D<float4> _OcclusionResultMap_RW;        // Per-pixel color after occlusion masking
RWTexture2D<float>  _OcclusionValueMap_RW;         // Raw occlusion value for debug visualization
RWTexture2D<float4> _FinalImage_RW;                // Final composited image (post hole-fill)
RWTexture2D<float4> _OriginMap_RW;                 // Debug map: black = PointCloud, white = StaticMesh

// --------------------------------------------------------------------------
// Read-only copies of the maps consumed in later passes.
// --------------------------------------------------------------------------
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
Texture2D<int>    _FinalNeighborhoodSizeMap;       // Final per-pixel level used in OcclusionAndFilter
Texture2D<float4> _OcclusionResultMap;

// --------------------------------------------------------------------------
// Hybrid virtual depth support (URP camera depth for virtual objects).
// When _UseVirtualDepth > 0, virtual object depth is composited with the
// point cloud depth to prevent point cloud pixels from occluding virtual objects.
// --------------------------------------------------------------------------
Texture2D<float>  _VirtualDepthMap;        // URP camera depth buffer (0 = near, 1 = far)
Texture2D<float4> _CameraColorTexture;     // URP camera color buffer
int               _UseVirtualDepth;        // 1 = enable virtual depth compositing, 0 = disable
float4x4          _InverseProjectionMatrix;// Used in InitFromCamera to unproject pixel -> view space
int               _RecordOcclusionDebug;   // 1 = write raw occlusion values to _OcclusionValueMap_RW

// --------------------------------------------------------------------------
// Merge buffer uniforms used by the MergeBuffer kernel.
// Allows copying a slice of _MergeSrcBuffer into _MergeDstBuffer.
// --------------------------------------------------------------------------
StructuredBuffer<Point>   _MergeSrcBuffer; // Source point buffer
RWStructuredBuffer<Point> _MergeDstBuffer; // Destination point buffer
int _MergeSrcOffset;  // Start index in the source buffer
int _MergeDstOffset;  // Start index in the destination buffer
int _MergeCopyCount;  // Number of points to copy

// --------------------------------------------------------------------------
// Per-frame uniform parameters.
// --------------------------------------------------------------------------
uint    _PointCount;                // Total number of points in _PointBuffer
float4  _ScreenParams;              // (width, height, 1/width, 1/height)
float4x4 _ViewMatrix;               // World-to-view transform
float4x4 _ProjectionMatrix;         // View-to-clip transform

float _DensityThreshold_e;          // Depth range epsilon for counting points inside a grid cell
float _NeighborhoodParam_p_prime;   // Scaling parameter for the neighborhood level formula: L = p'/sqrt(density)
float _GradientThreshold_g_th;      // Sobel gradient magnitude above which the level is reduced by 1
float _OcclusionThreshold;          // Occlusion average below this value marks a pixel as fully occluded
float _OcclusionFadeWidth;          // Width of the smoothstep fade band above _OcclusionThreshold (0 = hard threshold)

// --------------------------------------------------------------------------
// Constants.
// --------------------------------------------------------------------------
#define GRID_SIZE 16u               // Width and height of each density/level grid cell in pixels
#define DEPTH_MAX_UINT 0x7FFFFFFFu  // Sentinel value representing "no depth" (infinity)

// --------------------------------------------------------------------------
// Group-shared memory used within a single thread group.
// Initialized by thread 0, then updated by all threads via Interlocked ops.
// --------------------------------------------------------------------------
groupshared uint shared_z_min;        // Minimum depth across all threads in the group
groupshared uint shared_point_count;  // Number of valid PointCloud pixels in the group

#endif // PCD_OCCLUSION_DATA_INCLUDED