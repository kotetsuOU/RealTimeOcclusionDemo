// =============================================================================
// PCDPipelineContext.cs
// -----------------------------------------------------------------------------
// 1フレームのオクルージョンパイプライン実行に必要な全データを集約するコンテキスト。
// 旧 ComputePassData を置き換え、各ステージから読み書きされる Blackboard として機能する。
// =============================================================================
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// オクルージョンパイプラインの1フレーム実行に必要な全コンテキスト。
/// ステージ間でのデータ共有に使用する。
/// </summary>
internal class PCDPipelineContext
{
    // =========================================================================
    // コンピュートシェーダー本体
    // =========================================================================
    public ComputeShader ComputeShader;

    // =========================================================================
    // カメラ・画面パラメータ
    // =========================================================================
    public int ScreenWidth;
    public int ScreenHeight;
    public Vector4 ScreenParams;
    public Matrix4x4 ViewMatrix;
    public Matrix4x4 ProjectionMatrix;
    public Matrix4x4 InverseProjectionMatrix;

    // =========================================================================
    // 計算済みディスパッチパラメータ
    // =========================================================================
    public int ThreadGroupsX;
    public int ThreadGroupsY;
    public int GridGroupsX;
    public int GridGroupsY;

    // =========================================================================
    // 点群パラメータ
    // =========================================================================
    public int PointCount;

    // =========================================================================
    // レンダリング設定
    // =========================================================================
    public PCDRendererFeature.PCDRenderSettings Settings;

    // =========================================================================
    // コンポーネント参照
    // =========================================================================
    public PCDKernelRegistry Kernels;
    public PCDResourcePool Resources;

    // =========================================================================
    // バッファ管理
    // =========================================================================
    public bool UseExternal;
    public ComputeBuffer ExternalBuffer;
    public ComputeBuffer InternalBuffer;
    public int ExternalCount;
    public int InternalCount;
    public ComputeBuffer CombinedBuffer;
    public ComputeBuffer PointBuffer;           // 実際にシェーダーに渡すバッファ
    public ComputeBuffer StaticMeshCounterBuffer;

    // =========================================================================
    // 仮想深度・カメラ
    // =========================================================================
    public TextureHandle VirtualDepthTexture;
    public TextureHandle CameraColorTexture;
    public bool HasVirtualDepth;
    public bool HasVirtualObjects;
    public bool IsHalfMirrorEnabled;

    // =========================================================================
    // ユーティリティ
    // =========================================================================

    /// <summary> NeedsNeighborhoodSize: オクルージョンやホールフィリングで近傍サイズが必要か </summary>
    public bool NeedsNeighborhoodSize =>
        HasVirtualObjects &&
        (Settings.kernelType != PCDRendererFeature.PCD_OcclusionKernel.Skip ||
         Settings.holeFillingMethod == PCDRendererFeature.PCD_HoleFillingMethod.JointBilateral ||
         Settings.holeFillingMethod == PCDRendererFeature.PCD_HoleFillingMethod.Morphology_OC ||
         Settings.holeFillingMethod == PCDRendererFeature.PCD_HoleFillingMethod.Morphology_CO);

    /// <summary> NeedsDepthPyramid: 深度ピラミッドの構築が必要か </summary>
    public bool NeedsDepthPyramid =>
        HasVirtualObjects &&
        (Settings.kernelType != PCDRendererFeature.PCD_OcclusionKernel.Skip ||
         (NeedsNeighborhoodSize && Settings.enableGradientCorrection));

    /// <summary> 勾配補正・オクルージョン判定・ホールフィリングに使用する近傍サイズマップを返す </summary>
    public RTHandle ActiveNeighborhoodSizeMap => Resources.CorrectedNeighborhoodSizeMap;
}
