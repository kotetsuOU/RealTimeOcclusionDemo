// =============================================================================
// PCD_Occlusion_Data.hlsl
// Shared data types, buffer/texture bindings, and constants used across all
// occlusion pipeline kernels.
// =============================================================================
#ifndef PCD_OCCLUSION_DATA_INCLUDED
#define PCD_OCCLUSION_DATA_INCLUDED

// -----------------------------------------------------------------------------
// Point -- a single entry in the input point cloud buffer.
// -----------------------------------------------------------------------------
struct Point
{
    float3 position;   // World-space XYZ position of the point
    float3 color;      // Linear RGB color of the point
    uint originType;   // Source category: 0 = PointCloud (dynamic), 1 = StaticMesh
};

// -----------------------------------------------------------------------------
// Read-write textures written by the pipeline each frame.
// Each map is in screen space (width x height pixels) unless noted otherwise.
// -----------------------------------------------------------------------------
StructuredBuffer<Point> _PointBuffer;              // Input point cloud (read-only)
RWTexture2D<float4> _ColorMap_RW;                  // Per-pixel projected color
RWTexture2D<uint>   _DepthMap_RW;                  // Per-pixel depth stored as uint (range [0, DEPTH_MAX_UINT])
RWTexture2D<float4> _ViewPositionMap_RW;           // Per-pixel view-space position (xyz) + NDC depth (w)
RWTexture2D<uint>   _GridZMinMap_RW;               // Per-tile minimum depth (resolution: screen / GRID_SIZE)
RWTexture2D<float>  _DensityMap_RW;                // Per-tile point cloud density [0, 1]
RWTexture2D<int>    _GridLevelMap_RW;              // Per-tile raw LOD level derived from density
RWTexture2D<int>    _FilteredGridLevelMap_RW;      // Per-tile LOD level after 3x3 median smoothing
RWTexture2D<int>    _NeighborhoodSizeMap_RW;       // Per-pixel neighborhood LOD level (upsampled from tile)
RWTexture2D<uint>   _DepthPyramidL1_RW;            // Min-depth mipmap level 1 (1/2 resolution)
RWTexture2D<uint>   _DepthPyramidL2_RW;            // Min-depth mipmap level 2 (1/4 resolution)
RWTexture2D<uint>   _DepthPyramidL3_RW;            // Min-depth mipmap level 3 (1/8 resolution)
RWTexture2D<uint>   _DepthPyramidL4_RW;            // Min-depth mipmap level 4 (1/16 resolution)
RWTexture2D<int>    _CorrectedNeighborhoodSizeMap_RW; // Per-pixel LOD after gradient-based correction
RWTexture2D<uint>   _OriginTypeMap_RW;             // Per-pixel source type (0=PointCloud, 1=StaticMesh, 2=Background)
RWTexture2D<float4> _OcclusionResultMap_RW;        // Per-pixel color after occlusion test (alpha = visibility)
RWTexture2D<float>  _OcclusionValueMap_RW;         // Per-pixel raw occlusion score (debug output)
RWTexture2D<float4> _FinalImage_RW;                // Final composited color written after hole-filling
RWTexture2D<float4> _OriginMap_RW;                 // Per-pixel origin debug map (black=PointCloud, white=Mesh)

// -----------------------------------------------------------------------------
// Read-only texture views of the maps written above.
// Separate RW / read-only pairs allow one kernel to write and the next to read.
// -----------------------------------------------------------------------------
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
Texture2D<int>    _FinalNeighborhoodSizeMap;       // Final corrected neighborhood size (alias for _CorrectedNeighborhoodSizeMap_RW)
Texture2D<float4> _OcclusionResultMap;

// -----------------------------------------------------------------------------
// Virtual (URP camera) depth integration.
// When _UseVirtualDepth > 0, the pipeline blends real-camera depth with the
// point-cloud depth so that rendered virtual objects correctly occlude points.
// -----------------------------------------------------------------------------
Texture2D<float>  _VirtualDepthMap;        // Depth buffer from the URP camera (0=far, 1=near in Unity convention)
Texture2D<float4> _CameraColorTexture;     // Color buffer from the URP camera
int               _UseVirtualDepth;        // Flag: 1 = enable virtual depth compositing, 0 = disable
float4x4          _InverseProjectionMatrix;// Inverse projection matrix used to reconstruct view-space position from depth
int               _RecordOcclusionDebug;   // Flag: 1 = write occlusion scores to _OcclusionValueMap_RW for debugging

// -----------------------------------------------------------------------------
// Buffer merge parameters (used by the MergeBuffer kernel).
// Allows copying a slice of one point buffer into another in a single dispatch.
// -----------------------------------------------------------------------------
StructuredBuffer<Point>    _MergeSrcBuffer;  // Source buffer to copy points from
RWStructuredBuffer<Point>  _MergeDstBuffer;  // Destination buffer to copy points into
int _MergeSrcOffset;  // Starting index in the source buffer
int _MergeDstOffset;  // Starting index in the destination buffer
int _MergeCopyCount;  // Number of points to copy

// -----------------------------------------------------------------------------
// Per-dispatch uniforms
// -----------------------------------------------------------------------------
uint     _PointCount;                  // Total number of valid points in _PointBuffer
float4   _ScreenParams;                // Screen resolution: (width, height, 1+1/width, 1+1/height)
float4x4 _ViewMatrix;                  // World-to-view-space transform
float4x4 _ProjectionMatrix;            // View-to-clip-space projection matrix
float    _DensityThreshold_e;          // Depth range (epsilon) around the tile Z-min used to count nearby points
float    _NeighborhoodParam_p_prime;   // Scaling constant for converting density to neighborhood LOD level
float    _GradientThreshold_g_th;      // Sobel gradient magnitude above which the LOD level is reduced by 1
float    _OcclusionThreshold;          // Occlusion score below which a point is considered hidden
float    _OcclusionFadeWidth;          // Width of the smooth fade zone around the occlusion threshold

// -----------------------------------------------------------------------------
// Compile-time constants
// -----------------------------------------------------------------------------
#define GRID_SIZE      16u             // Width/height of a single density/LOD tile in pixels
#define DEPTH_MAX_UINT 0x7FFFFFFFu     // Sentinel value representing "no depth / infinite distance"

// -----------------------------------------------------------------------------
// Group-shared memory (used for intra-tile reductions)
// -----------------------------------------------------------------------------
groupshared uint shared_z_min;         // Tile minimum depth accumulated across threads in CalculateGridZMin
groupshared uint shared_point_count;   // Tile point count accumulated across threads in CalculateDensity

#endif // PCD_OCCLUSION_DATA_INCLUDED