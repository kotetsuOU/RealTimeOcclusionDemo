using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class PCDRendererFeature : ScriptableRendererFeature
{
    public static PCDRendererFeature Instance { get; private set; }

    [System.Serializable]
    public struct PCDRenderSettings
    {
        public float densityThreshold_e;
        public float neighborhoodParam_p_prime;
        public bool enableGradientCorrection;
        public float gradientThreshold_g_th;
        [Range(0f, 1f)] public float occlusionThreshold;
        [Range(0f, 1f)] public float occlusionFadeWidth;
        public bool enableOriginDebugMap;
        public bool recordOcclusionDebugMap;

        public bool enableVirtualDepthIntegration;

        public bool enableTagBasedOptimization;   // ① タグに基づく探索スキップ
        public bool enableTypeAwareDensity;       // ② 仮想物体を区別した密度計算
        public bool enableSoftOcclusionFade;      // ③ ソフトオクルージョン (FadeWidth)
        public bool enableJointBilateralHoleFilling; // ④ ジョイントバイラテラル穴埋め

        [HideInInspector] public uint _dynamicMultiplierRuntimeValue;
    }

    // 登録された静的メッシュの情報を保持するためのクラス
    private class RegisteredObject
    {
        public Mesh mesh;
        public Transform transform;
        public PCDProcessingMode mode;
    }

    [Header("Required Assets")]
    public ComputeShader pointCloudCompute;

    [Header("Algorithm Parameters")]
    [Tooltip("密度計算に用いる深度のしきい値 e")]
    public float densityThreshold_e = 0.04f;

    [Tooltip("近傍領域サイズを決定するための調整パラメータ p' ")]
    public float neighborhoodParam_p_prime = 4.8f;

    [Header("Gradient Correction")]
    [Tooltip("勾配を用いた補正を有効にする")]
    public bool enableGradientCorrection = true;

    [Tooltip("勾配しきい値 g_th")]
    public float gradientThreshold_g_th = 0.05f;

    [Header("Occlusion Filtering")]
    [Tooltip("オクルージョン判定のしきい値 (論文 2.4.2節)")]
    [Range(0f, 1f)]
    public float occlusionThreshold = 0.8f;

    [Tooltip("境界を滑らかにするためのフェード幅（閾値からの減衰範囲）")]
    [Range(0f, 1f)]
    public float occlusionFadeWidth = 0.1f;

    [Header("Blending Assets")]
    [Tooltip("最終結果のアルファブレンドを有効にするか")]
    public bool enableAlphaBlend = true;
    public Material blendMaterial;

    [Header("Layer & Bounds Optimization")]
    [Tooltip("PCDを描画するための専用レイヤー")]
    public LayerMask pcdLayer;
    [Tooltip("登録時に自動的にレイヤーを変更するか")]
    public bool autoSetLayer = true;
    [Tooltip("カリング防止のためにBoundsを拡張するか")]
    public bool expandBounds = true;
    [Tooltip("拡張するBoundsのサイズ")]
    public float boundsSize = 10000f;

    [Header("Debug")]
    [Tooltip("点群(黒)と静的メッシュ(白)の由来を示すデバッグマップを有効にします")]
    public bool enableOriginDebugMap = false;

    [Tooltip("1フレームだけOcclusionの個別の生値を記録します")]
    public bool recordOcclusionDebugMap = false;

    [Header("Novel Methods Toggles (Ablation Study)")]
    [Tooltip("仮想・現実の「相互オクルージョン」の統合を有効にするか")]
    public bool enableVirtualDepthIntegration = true;

    [Tooltip("①タグによる近傍探索の最適化 (ONで不要な自己遮蔽計算をスキップ)")]
    public bool enableTagBasedOptimization = true;

    [Tooltip("②仮想物体を区別した密度計算 (ONで従来手法のカウント漏れや過剰を補正)")]
    public bool enableTypeAwareDensity = true;

    [Tooltip("③ソフトオクルージョン (ONでグラデーションによる境界のスムージング)")]
    public bool enableSoftOcclusionFade = true;

    [Tooltip("④エッジ保持型ホールフィリング (ONでジョイントバイラテラル穴埋め)")]
    public bool enableJointBilateralHoleFilling = true;

    private PCDRenderPass _scriptablePass;

    private bool _useGlobalBufferMode = false;
    public bool IsGlobalBufferMode => _useGlobalBufferMode;

    private static List<RegisteredObject> _persistentObjects = new List<RegisteredObject>();

    public void SetUseGlobalBuffer(bool enable)
    {
        _useGlobalBufferMode = enable;
    }

    // Inspectorで設定されている値を構造体として取得する
    private PCDRenderSettings GetSettings()
    {
        return new PCDRenderSettings
        {
            densityThreshold_e = this.densityThreshold_e,
            neighborhoodParam_p_prime = this.neighborhoodParam_p_prime,
            enableGradientCorrection = this.enableGradientCorrection,
            gradientThreshold_g_th = this.gradientThreshold_g_th,
            occlusionThreshold = this.occlusionThreshold,
            occlusionFadeWidth = this.occlusionFadeWidth,
            enableOriginDebugMap = this.enableOriginDebugMap,
            recordOcclusionDebugMap = this.recordOcclusionDebugMap,
            enableVirtualDepthIntegration = this.enableVirtualDepthIntegration,
            enableTagBasedOptimization = this.enableTagBasedOptimization,
            enableTypeAwareDensity = this.enableTypeAwareDensity,
            enableSoftOcclusionFade = this.enableSoftOcclusionFade,
            enableJointBilateralHoleFilling = this.enableJointBilateralHoleFilling,
            _dynamicMultiplierRuntimeValue = _internalDynamicMultiplier
        };
    }

    [HideInInspector] public uint _internalDynamicMultiplier = 1;

    // レンダラー特徴の初期化時に呼ばれる
    public override void Create()
    {
        Instance = this;

        _scriptablePass?.Cleanup();

        // レンダリングパスのインスタンスを生成し、実行タイミングを設定
        _scriptablePass = new PCDRenderPass(this.pointCloudCompute, GetSettings(), blendMaterial, enableAlphaBlend);
        _scriptablePass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        // パス再生成時にも既存の登録済みメッシュ情報を引き継ぐ
        SyncPersistentObjectsToPass();
    }

    // 内部リストに保持している静的メッシュをスクリプタブルパスへ再登録する
    private void SyncPersistentObjectsToPass()
    {
        if (_scriptablePass == null) return;

        for (int i = _persistentObjects.Count - 1; i >= 0; i--)
        {
            var obj = _persistentObjects[i];
            if (obj.mesh != null && obj.transform != null)
            {
                _scriptablePass.AddStaticMesh(obj.mesh, obj.transform, obj.mode);
            }
            else
            {
                _persistentObjects.RemoveAt(i);
            }
        }
    }

    // オクルージョン用の静的メッシュを追加登録する
    public void AddStaticMesh(Mesh mesh, Transform transform, PCDProcessingMode mode)
    {
        if (mesh == null || transform == null) return;

        // 既に登録されているか確認し、無い場合は追加、ある場合はモードを更新
        var existing = _persistentObjects.Find(x => x.mesh == mesh && x.transform == transform);
        if (existing == null)
        {
            _persistentObjects.Add(new RegisteredObject { mesh = mesh, transform = transform, mode = mode });
        }
        else
        {
            existing.mode = mode;
        }

        ApplySettings(mesh, transform, mode);

        // 実際の描画パスにもメッシュ情報を渡す
        _scriptablePass?.AddStaticMesh(mesh, transform, mode);
    }

    // 登録された静的メッシュを削除する
    public void RemoveStaticMesh(Mesh mesh, Transform transform)
    {
        _persistentObjects.RemoveAll(x => x.mesh == mesh && x.transform == transform);
        _scriptablePass?.RemoveStaticMesh(mesh, transform);
    }

    // 動的オブジェクト用にデータ再構築をリクエストする
    public void MarkPointCloudDataDirty()
    {
        _scriptablePass?.MarkPointCloudDataDirty();
    }

    // t[??ARenderGraph?pXGL[
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // 毎フレーム、メッシュのカリング設定やレイヤーを強制適用する
        EnforceSettingsEveryFrame();

        if (pointCloudCompute == null || (enableAlphaBlend && !enableOriginDebugMap && blendMaterial == null))
        {
            return;
        }

        if (_scriptablePass != null)
        {
            // Inspectorでの変更をパスに反映
            _scriptablePass.UpdateSettings(GetSettings());
            _scriptablePass.SetDebugFlag(enableOriginDebugMap);
        }

        // 常時パスをエンキューし、描画をスキップするかどうかはRecordRenderGraph内や内部ロジックに委ねる
        renderer.EnqueuePass(_scriptablePass);
    }

    // 登録されたすべてのオブジェクトに対して、設定（BoundsやLayer）が正しく適用されているか確認する
    private void EnforceSettingsEveryFrame()
    {
        for (int i = _persistentObjects.Count - 1; i >= 0; i--)
        {
            var obj = _persistentObjects[i];
            // オブジェクトが破棄されていたらリストから削除
            if (obj.mesh == null || obj.transform == null)
            {
                _persistentObjects.RemoveAt(i);
                continue;
            }
            ApplySettings(obj.mesh, obj.transform, obj.mode);
        }
    }

    // メッシュに広大なBoundsを設定（カリング防止）し、指定されたレイヤーに変更する
    private void ApplySettings(Mesh mesh, Transform transform, PCDProcessingMode mode)
    {
        // DepthMapモード（通常のURP描画を利用するメッシュ）は、レイヤー変更やBounds拡張の対象外
        if (mode == PCDProcessingMode.DepthMap) return;

        if (expandBounds && mesh != null)
        {
            // Note: sharedMesh.bounds を上書きするとエディタ上で恒久的に変更される恐れがあるため注意が必要ですが、
            // PointCloudモードの場合はカリングを防ぐために変更します。
            // 可能であれば PlayMode のみで実行するか、Meshをインスタンス化することが望ましいです。
            if (mesh.bounds.extents.x < boundsSize * 0.5f)
            {
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * boundsSize);
            }
        }

        if (autoSetLayer && transform != null)
        {
            int layerIndex = 0;
            int mask = pcdLayer.value;
            while (mask > 1) { mask >>= 1; layerIndex++; }

            if (layerIndex >= 0 && layerIndex < 32 && transform.gameObject.layer != layerIndex)
            {
                transform.gameObject.layer = layerIndex;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _scriptablePass?.Cleanup();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void SetPointCloudData(PCV_Data data)
    {
        if (_scriptablePass != null)
        {
            _scriptablePass.SetPointCloudData(data);
        }
    }

    public void SetExternalBuffer(ComputeBuffer buffer, int count)
    {
        if (_scriptablePass != null)
        {
            _scriptablePass.SetExternalBuffer(buffer, count);
        }
    }

    public Texture GetOriginDebugMap() => _scriptablePass?.GetOriginDebugMap();

    // ==========================================
    // インスペクターの値が変更された時に自動で呼ばれる検証関数
    // ==========================================
    private void OnValidate()
    {
        float maxFadeWidth = Mathf.Min(occlusionThreshold, 1.0f - occlusionThreshold) * 2.0f;
        occlusionFadeWidth = Mathf.Clamp(occlusionFadeWidth, 0f, maxFadeWidth);
    }
}