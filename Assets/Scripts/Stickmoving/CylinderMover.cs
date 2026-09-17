using UnityEngine;

public class CylinderMover : MonoBehaviour
{
    private enum CylinderState { Moving, Waiting }

    [Header("移動範囲")]
    [SerializeField] private float startZ = -1.0f;   // 手前側のZ座標
    [SerializeField] private float endZ = 1.0f;      // 奥側のZ座標
    [SerializeField] private float fixedX = 0.0f;    // X座標（固定値）
    [SerializeField] private float fixedY = 0.0f;    // Y座標のフォールバック値（upperSampler未設定・取得失敗時に使用）

    [Header("タイミング")]
    [SerializeField] private float moveDuration = 2.0f;  // 手前→奥までの所要時間（秒）
    [SerializeField] private float waitDuration = 1.0f;  // 待機時間（秒）

    [Header("見た目の切り替え")]
    [SerializeField] private Renderer cylinderRenderer;  // インスペクターでCylinderのRendererをアタッチ

    [Header("Depth Settings")]
    [Tooltip("手のひら上面のY座標を取得するPointCloudDepthSampler（MultiAUTD3Controllerと同じ仕組み）。未設定ならfixedYを使う")]
    [SerializeField] private PointCloudDepthSampler upperSampler;

    [Tooltip("点群から取得したYの平滑化速度。大きいほど追従が速く、小さいほどノイズに強い")]
    [SerializeField] private float ySmoothSpeed = 10f;

    private CylinderState state = CylinderState.Moving;
    private float elapsedTime = 0f;
    private float waitTimer = 0f;
    private int passIndex = 0;
    private float smoothedY;
    private bool ySmoothInitialized = false;

    /// <summary>移動中（＝stickが見えている区間）かどうか</summary>
    public bool IsMoving => state == CylinderState.Moving;

    /// <summary>手前→奥の通過が何回目か（0始まり）。ログの照合用</summary>
    public int PassIndex => passIndex;

    /// <summary>今回の通過の進捗（0→1）</summary>
    public float NormalizedProgress => Mathf.Clamp01(elapsedTime / moveDuration);

    void Update()
    {
        switch (state)
        {
            case CylinderState.Moving:
                elapsedTime += Time.deltaTime;
                float t = Mathf.Clamp01(elapsedTime / moveDuration);

                float z = Mathf.Lerp(startZ, endZ, t);
                float y = GetSmoothedSurfaceY(z);
                transform.position = new Vector3(fixedX, y, z);

                cylinderRenderer.enabled = true;

                if (t >= 1f)
                {
                    state = CylinderState.Waiting;
                    waitTimer = 0f;
                    cylinderRenderer.enabled = false;
                }
                break;

            case CylinderState.Waiting:
                waitTimer += Time.deltaTime;
                if (waitTimer >= waitDuration)
                {
                    elapsedTime = 0f;
                    passIndex++;
                    state = CylinderState.Moving;
                }
                break;
        }
    }

    // MultiAUTD3Controller.GetSurfaceY() と同じ仕組み：
    // 点群の中央値Yを取得し、生値のジッターをLerpで平滑化してから使う
    private float GetSmoothedSurfaceY(float z)
    {
        float rawY = fixedY;
        if (upperSampler != null && upperSampler.TryGetMedianY(fixedX, z, out float sampledY))
            rawY = sampledY;

        if (!ySmoothInitialized)
        {
            smoothedY = rawY;
            ySmoothInitialized = true;
        }
        else
        {
            smoothedY = Mathf.Lerp(smoothedY, rawY, 1f - Mathf.Exp(-ySmoothSpeed * Time.deltaTime));
        }

        return smoothedY;
    }
}