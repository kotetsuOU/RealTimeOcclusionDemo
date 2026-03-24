using UnityEngine;

/// <summary>
/// ディスプレイ（視面）の位置とサイズを利用して、カメラの非対称な投影行列(Frustum Matrix)を計算し、
/// ハーフミラー等の環境に合わせた視点（パースペクティブ）をLateUpdateで動的に調整するクラス。
/// </summary>
public class CameraAdjuster : MonoBehaviour
{
    [Tooltip("基準となるディスプレイを表すTransform（位置とスケールから描画領域を計算）")]
    public Transform displayTransform;

    [Header ("Debug")]
    [Tooltip("ハーフミラー環境用に左右のフラスタム（投影）を反転するかどうか")]
    public bool isHalfMirrorEnabled = true;

    [Tooltip("MoveToDefaultPosition()で移動させる際のデフォルト座標")]
    public Vector3 defaultPosition = new Vector3(0.3f, 0.85f, 0.15f);

    private void LateUpdate()
    {
        if (displayTransform == null)
        {
            return;
        }

        Camera cam = GetComponent<Camera>();
        if (cam == null)
        {
            return;
        }

        // --- ディスプレイの四隅の座標をワールド空間で計算 ---
        Vector3 displayCenter = displayTransform.position;
        // Transformのスケール情報を元に、横幅(right)と縦幅(forwardとして扱う)の半分を取得
        Vector3 displayRight = Vector3.right * displayTransform.localScale.x / 2;
        Vector3 displayUp = Vector3.forward * displayTransform.localScale.z / 2;

        // ボトムレフト、ボトムライト、トップレフトの座標
        Vector3 bl = displayCenter - displayRight - displayUp;
        Vector3 br = displayCenter + displayRight - displayUp;
        Vector3 tl = displayCenter - displayRight + displayUp;

        // --- ワールド座標をカメラのローカル座標(ビュー空間)に変換 ---
        Matrix4x4 cameraTransform = cam.worldToCameraMatrix;
        bl = cameraTransform.MultiplyPoint(bl);
        br = cameraTransform.MultiplyPoint(br);
        tl = cameraTransform.MultiplyPoint(tl);

        // --- カメラのニアクリップ面(Near Plane)でのディスプレイ投影サイズを計算 ---
        float nearPlane = 0.1f;
        // 相似比を利用して、ディスプレイ面のZ距離(-z)からニアクリップ面上のx,yサイズを求める
        float right = br.x * (nearPlane / -br.z);
        float left = bl.x * (nearPlane / -bl.z);
        float top = tl.y * (nearPlane / -tl.z);
        float bottom = bl.y * (nearPlane / -bl.z);

        // --- 非対称な投影行列を構築してカメラに適用 ---
        Matrix4x4 p;
        if (isHalfMirrorEnabled)
        {
            // ハーフミラーの場合、左右の端を反転させる (right, leftの順)
            p = Matrix4x4.Frustum(right, left, bottom, top, nearPlane, 1);
        }
        else
        {
            // 通常の場合
            p = Matrix4x4.Frustum(left, right, bottom, top, nearPlane, 1);
        }
        cam.projectionMatrix = p;
    }

    /// <summary>
    /// カメラの位置を、インスペクターで指定した defaultPosition に強制的に移動させる
    /// </summary>
    public void MoveToDefaultPosition()
    {
        this.transform.position = defaultPosition;

        UnityEngine.Debug.Log($"Moved to default position: {defaultPosition}");
    }
}