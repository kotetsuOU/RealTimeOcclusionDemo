using UnityEngine;

public class StickMover : MonoBehaviour
{
    [Header("楕円軌道設定")]
    [Tooltip("手のひらのX方向（左右）のサイズ")]
    [SerializeField] private float handSizeX = 0.08f;
    [Tooltip("手のひらのZ方向（奥行き）のサイズ")]
    [SerializeField] private float handSizeZ = 0.10f;
    [Tooltip("手のサイズに対する軌道の縮小率（1.0で手のサイズと同じ、0.8なら一回り小さい）")]
    [Range(0.1f, 1.0f)] [SerializeField] private float marginRatio = 0.8f;
    [Tooltip("1周にかかる時間（秒）")]
    [SerializeField] private float revolutionDuration = 2.0f;

    [Header("見た目の切り替え")]
    [SerializeField] private Renderer cylinderRenderer;  // インスペクターでCylinderのRendererをアタッチ

    [Header("Palm Height Capture")]
    [Tooltip("手のひら上面のY座標を取得するPointCloudDepthSampler")]
    [SerializeField] private PointCloudDepthSampler upperSampler;
    [Tooltip("参加者が手を合わせる、固定された目印オブジェクト（楕円の中心にもなる）")]
    [SerializeField] private Transform palmReference;
    [Tooltip("このキーで開始する")]
    [SerializeField] private KeyCode captureKey = KeyCode.Space;

    [Header("Cylinder形状")]
    [Tooltip("cylinderの中心から先端（表面に接する側）までの長さ")]
    [SerializeField] private float cylinderHalfLength = 0.5f;

    private Vector3 tipOffset;

    private float capturedPalmY;
    private bool hasStarted = false;
    private float elapsedTime = 0f;

    /// <summary>回転中かどうか（開始済みなら常にtrue）</summary>
    public bool IsMoving => hasStarted;

    /// <summary>stick先端の現在のワールド座標（触覚側の追従に使う）</summary>
    public Vector3 TipPosition { get; private set; }

    void Start()
    {
        tipOffset = transform.rotation * Vector3.up * cylinderHalfLength;

        if (cylinderRenderer != null)
            cylinderRenderer.enabled = false;
    }

    void Update()
    {
        if (!hasStarted)
        {
            if (Input.GetKeyDown(captureKey) && palmReference != null)
            {
                float x = palmReference.position.x;
                float z = palmReference.position.z;

                if (upperSampler != null && upperSampler.TryGetMedianY(x, z, out float y))
                {
                    capturedPalmY = y;
                    hasStarted = true;
                    palmReference.gameObject.SetActive(false);
                    Debug.Log($"[CylinderMover] 手のひら高さを取得: {capturedPalmY:F4}");
                }
                else
                {
                    Debug.LogWarning("[CylinderMover] 手のひら高さの取得に失敗（点群が足りない）");
                }
            }
            return;
        }

        elapsedTime += Time.deltaTime;

        // 経過時間を周期で割った余りから、0〜1の進行度を出す（無限ループ）
        float t = (elapsedTime % revolutionDuration) / revolutionDuration;
        float angle = t * 2f * Mathf.PI;

        float radiusX = (handSizeX / 2f) * marginRatio;
        float radiusZ = (handSizeZ / 2f) * marginRatio;

        float centerX = palmReference.position.x;
        float centerZ = palmReference.position.z;

        float tipX = centerX + radiusX * Mathf.Cos(angle);
        float tipZ = centerZ + radiusZ * Mathf.Sin(angle);

        Vector3 tipPosition = new Vector3(tipX, capturedPalmY, tipZ);
        Vector3 centerPosition = tipPosition - tipOffset;

        TipPosition = tipPosition;
        transform.position = centerPosition;

        cylinderRenderer.enabled = true;
    }
}