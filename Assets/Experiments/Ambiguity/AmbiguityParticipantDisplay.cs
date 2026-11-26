using UnityEngine;
using TMPro;

/// <summary>
/// 被験者向けの表示（注視点と文言）。SRD に出す。
///
/// 表示方式は2つ：
///  A) 通常（useQuad = OFF）: Canvas の文字をそのまま描く。World Space の Canvas を使う。
///     ミラー反転は mirrorRoot の X スケールで行う。
///  B) 不透明な板に貼る（useQuad = ON）: オクルージョン対策。
///     文字は半透明で深度を書かないため、手の点群が出ると点群に上書きされて消える。
///     そこで、文字を専用カメラで RenderTexture に描き、それを不透明な Quad に貼って深度を書かせる。
///     背景は黒（ディスプレイが光らない＝ハーフミラー越しでは透明に見える）。
///     ミラー反転は UV の X 反転で行う（Quad のスケールを負にすると裏面になり、描かれなくなるため）。
/// </summary>
public class AmbiguityParticipantDisplay : MonoBehaviour
{
    [Header("表示物")]
    [Tooltip("注視点「＋」")]
    [SerializeField] private TMP_Text fixationText;
    [Tooltip("教示・案内文")]
    [SerializeField] private TMP_Text messageText;

    [Header("ハーフミラー対応")]
    [Tooltip("ON なら鏡像でも読める向きに反転する。SRDMirrorCamera が既に鏡像にしている場合は OFF から試す")]
    [SerializeField] private bool mirrorX = true;
    [Tooltip("方式A用: 反転する親（Canvas の子の入れ物など）。文字と注視点の両方を含める")]
    [SerializeField] private Transform mirrorRoot;

    [Header("方式B: 不透明な板に貼る（オクルージョン対策）")]
    [Tooltip("ON なら、文字を RenderTexture に描いて、不透明な Quad に貼る")]
    [SerializeField] private bool useQuad = false;
    [Tooltip("文字が乗っている Canvas。実行時に Screen Space - Camera に切り替えて、専用カメラで描く")]
    [SerializeField] private Canvas textCanvas;
    [Tooltip("板の位置と向き。この Transform の位置・回転に板を置く（板の表側は -Z 方向を向く。被験者の視線側へ向ける）")]
    [SerializeField] private Transform quadAnchor;
    [Tooltip("板の横幅（m）。縦は RenderTexture の縦横比から決まる")]
    [SerializeField] private float quadWidthMeters = 0.2f;
    [Tooltip("RenderTexture の解像度。Canvas のサイズ（ピクセル）もこれに合わせて決まる。文字・注視点の配置は、この大きさの Canvas を基準にする")]
    [SerializeField] private Vector2Int textureSize = new Vector2Int(1024, 512);
    [Tooltip("板を置くレイヤー名。手の点群と同じパイプラインで描かれるよう、PCD など、SRD のカメラに映るレイヤーにする")]
    [SerializeField] private string quadLayerName = "PCD";
    [Tooltip("RenderTexture の背景色。黒ならディスプレイが光らず、ハーフミラー越しに透明に見える")]
    [SerializeField] private Color backgroundColor = Color.black;

    private RenderTexture _rt;
    private Camera _textCamera;
    private GameObject _quad;
    private Material _quadMaterial;

    private void Awake()
    {
        if (useQuad) SetupQuad();
        else ApplyMirrorRoot();
    }

    private void OnValidate()
    {
        if (!useQuad) ApplyMirrorRoot();
    }

    private void OnDestroy()
    {
        if (_rt != null) { _rt.Release(); Destroy(_rt); }
        if (_quadMaterial != null) Destroy(_quadMaterial);
    }

    // ------------------------------------------------------------------ 方式A

    private void ApplyMirrorRoot()
    {
        if (mirrorRoot == null) return;
        var s = mirrorRoot.localScale;
        s.x = mirrorX ? -Mathf.Abs(s.x) : Mathf.Abs(s.x);
        mirrorRoot.localScale = s;
    }

    // ------------------------------------------------------------------ 方式B

    private void SetupQuad()
    {
        if (textCanvas == null || quadAnchor == null)
        {
            Debug.LogWarning("[ParticipantDisplay] 方式B には Text Canvas と Quad Anchor の設定が必要です。");
            return;
        }

        _rt = new RenderTexture(textureSize.x, textureSize.y, 0, RenderTextureFormat.ARGB32)
        {
            name = "ParticipantTextRT",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            useMipMap = false
        };
        _rt.Create();

        // 専用カメラ。名前に "Additional" を含めて、点群オクルージョン（PCDRendererFeature）の対象から外す。
        // 文字の Canvas（UI レイヤー）だけを描く。
        var camObj = new GameObject("ParticipantTextCamera_Additional");
        camObj.transform.SetParent(transform, false);
        _textCamera = camObj.AddComponent<Camera>();
        _textCamera.clearFlags = CameraClearFlags.SolidColor;
        _textCamera.backgroundColor = backgroundColor;
        _textCamera.cullingMask = 1 << textCanvas.gameObject.layer;
        _textCamera.orthographic = true;
        _textCamera.orthographicSize = 0.5f; // Canvas は視野いっぱいに広がる。小さくして Scene ビューで邪魔にならないようにする
        _textCamera.targetTexture = _rt;
        _textCamera.allowHDR = false;
        _textCamera.allowMSAA = false;
        _textCamera.depth = -100; // メインより先に描く

        // Canvas を専用カメラで描く。サイズは RenderTexture のピクセル数になる（Constant Pixel Size のとき）
        textCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        textCanvas.worldCamera = _textCamera;
        textCanvas.planeDistance = 1f;

        // 板（Quad）。Unlit の不透明マテリアルにして、深度を書かせる
        _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _quad.name = "ParticipantTextQuad";
        var col = _quad.GetComponent<Collider>();
        if (col != null) Destroy(col);
        int layer = LayerMask.NameToLayer(quadLayerName);
        if (layer < 0)
        {
            Debug.LogWarning($"[ParticipantDisplay] レイヤー '{quadLayerName}' がありません。Default に置きます。");
            layer = 0;
        }
        _quad.layer = layer;
        _quad.transform.SetParent(quadAnchor, false);
        _quad.transform.localPosition = Vector3.zero;
        _quad.transform.localRotation = Quaternion.identity;
        float aspect = (float)textureSize.y / textureSize.x;
        _quad.transform.localScale = new Vector3(quadWidthMeters, quadWidthMeters * aspect, 1f);

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        _quadMaterial = new Material(shader) { name = "ParticipantTextMat" };
        _quadMaterial.mainTexture = _rt;
        if (_quadMaterial.HasProperty("_BaseMap")) _quadMaterial.SetTexture("_BaseMap", _rt);
        // ミラー反転は UV で行う（Quad のスケールを負にすると裏面になって描かれない）
        Vector2 uvScale = new Vector2(mirrorX ? -1f : 1f, 1f);
        Vector2 uvOffset = new Vector2(mirrorX ? 1f : 0f, 0f);
        _quadMaterial.mainTextureScale = uvScale;
        _quadMaterial.mainTextureOffset = uvOffset;
        if (_quadMaterial.HasProperty("_BaseMap"))
        {
            _quadMaterial.SetTextureScale("_BaseMap", uvScale);
            _quadMaterial.SetTextureOffset("_BaseMap", uvOffset);
        }
        _quad.GetComponent<MeshRenderer>().sharedMaterial = _quadMaterial;
    }

    // ------------------------------------------------------------------ 表示の切り替え（AmbiguityTask から）

    public void ShowFixation(bool on)
    {
        if (fixationText != null && fixationText.gameObject.activeSelf != on)
            fixationText.gameObject.SetActive(on);
    }

    /// <summary>空文字なら消す。</summary>
    public void ShowMessage(string text)
    {
        if (messageText == null) return;
        bool on = !string.IsNullOrEmpty(text);
        if (messageText.gameObject.activeSelf != on) messageText.gameObject.SetActive(on);
        if (on) messageText.text = text;
    }
}
