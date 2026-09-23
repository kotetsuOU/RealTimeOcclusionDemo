using System.Collections.Generic;
using UnityEngine;

public class CylinderMover : MonoBehaviour
{
    public enum MovementPattern { Ellipse, Linear, Blink }

    [Header("移動パターン")]
    [SerializeField] private MovementPattern movementPattern = MovementPattern.Ellipse;

    [Header("楕円軌道設定")]
    [SerializeField] private float handWidthX = 0.07f; // 手のひらX方向全長(実測値をそのまま入力)
    [SerializeField] private float handWidthZ = 0.08f; // 手のひらZ方向全長
    [Range(0.1f, 1.0f)] [SerializeField] private float marginRatio = 0.8f; // 縮小率
    [SerializeField] private float revolutionDuration = 2.0f;

    [Header("直線移動設定")]
    [SerializeField] private float linearHalfLength = 0.05f; // Z軸方向の片側振れ幅
    [SerializeField] private float linearMoveDuration = 2.0f; // 片道の所要時間

    [Header("点滅設定(palmReference位置に固定して点滅)")]
    [SerializeField] private float blinkOnDuration = 1.0f;
    [SerializeField] private float blinkOffDuration = 1.0f;
    [Tooltip("palmReference位置と、palmReference+handWidthZ/2位置を、それぞれ何回ずつランダムな順番で提示するか")]
    [SerializeField] private int blinkRepeatsPerPosition = 10;

    [SerializeField] private Renderer cylinderRenderer;
    [SerializeField] private PointCloudDepthSampler upperSampler;
    [SerializeField] private Transform palmReference; // 固定の目印、円/直線の中心
    [SerializeField] private KeyCode captureKey = KeyCode.Space;
    [SerializeField] private float cylinderHalfLength = 0.5f;

    [Header("見た目の微調整")]
    [Tooltip("見た目のスティック位置だけをY方向にずらす微調整値(触覚焦点の計算には影響しない)")]
    [SerializeField] private float visualHeightOffset = 0f;

    private float capturedPalmY;
    private bool hasStarted = false;
    private float elapsedTime = 0f;

    private List<bool> blinkSequence; // false: palmReference位置 / true: +handWidthZ/2位置
    private int blinkIndex = -1;
    private bool blinkUsesOffsetPosition = false;
    private bool blinkFinished = false;

    public bool IsMoving => hasStarted;
    public Vector3 TipPosition { get; private set; }

    /// <summary>点滅モードで現在「出ている」状態かどうか。それ以外のモードでは常にtrue</summary>
    public bool IsVisible { get; private set; } = true;

    void Start()
    {
        if (cylinderRenderer != null) cylinderRenderer.enabled = false;
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

                    if (movementPattern == MovementPattern.Blink)
                    {
                        BuildBlinkSequence();
                        IsVisible = false; // Update()の最初のフレームで確実に1件目を消費させる
                    }

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

        float centerX = palmReference.position.x;
        float centerZ = palmReference.position.z;

        Vector3 tipPosition;

        switch (movementPattern)
        {
            case MovementPattern.Ellipse:
                float t = (elapsedTime % revolutionDuration) / revolutionDuration;
                float angle = t * 2f * Mathf.PI;

                float radiusX = (handWidthX / 2f) * marginRatio;
                float radiusZ = (handWidthZ / 2f) * marginRatio;

                float tipX = centerX + radiusX * Mathf.Cos(angle);
                float tipZ = centerZ + radiusZ * Mathf.Sin(angle);

                tipPosition = new Vector3(tipX, capturedPalmY, tipZ);
                break;

            case MovementPattern.Linear:
                float tLinear = Mathf.PingPong(elapsedTime, linearMoveDuration) / linearMoveDuration;

                Vector3 startPoint = new Vector3(centerX, capturedPalmY, centerZ - linearHalfLength);
                Vector3 endPoint   = new Vector3(centerX, capturedPalmY, centerZ + linearHalfLength);

                tipPosition = Vector3.Lerp(startPoint, endPoint, tLinear);
                break;

            case MovementPattern.Blink:
                bool nowVisible = false;
                if (!blinkFinished)
                {
                    float blinkCycle = blinkOnDuration + blinkOffDuration;
                    float blinkT = (blinkCycle > 0f) ? elapsedTime % blinkCycle : 0f;
                    nowVisible = blinkT < blinkOnDuration;

                    if (nowVisible && !IsVisible)
                    {
                        AdvanceBlinkPosition();
                        if (blinkFinished) nowVisible = false;
                    }
                }
                IsVisible = nowVisible;

                float blinkOffsetZ = blinkUsesOffsetPosition ? (handWidthZ / 2f) : 0f;
                tipPosition = new Vector3(centerX, capturedPalmY, centerZ + blinkOffsetZ);
                break;

            default:
                tipPosition = transform.position; // フォールバック（通常到達しない）
                break;
        }

        if (movementPattern != MovementPattern.Blink) IsVisible = true;

        TipPosition = tipPosition;
        transform.position = tipPosition + Vector3.up * (cylinderHalfLength + visualHeightOffset);
        cylinderRenderer.enabled = IsVisible;
    }

    // palmReference位置とpalmReference+handWidthZ/2位置を、それぞれblinkRepeatsPerPosition回ずつ
    // ランダムな順番で並べたシーケンスを作る(Fisher-Yatesシャッフル)
    private void BuildBlinkSequence()
    {
        blinkSequence = new List<bool>(blinkRepeatsPerPosition * 2);
        for (int i = 0; i < blinkRepeatsPerPosition; i++)
        {
            blinkSequence.Add(false); // palmReference位置
            blinkSequence.Add(true);  // palmReference + handWidthZ/2位置
        }

        for (int i = blinkSequence.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (blinkSequence[i], blinkSequence[j]) = (blinkSequence[j], blinkSequence[i]);
        }

        blinkIndex = -1;
        blinkFinished = false;
    }

    // 次の提示位置に進める。シーケンスを使い切ったらblinkFinished=trueにする
    private void AdvanceBlinkPosition()
    {
        if (blinkSequence == null || blinkSequence.Count == 0)
        {
            blinkFinished = true;
            return;
        }

        blinkIndex++;
        if (blinkIndex >= blinkSequence.Count)
        {
            blinkFinished = true;
            Debug.Log("[CylinderMover] Blinkシーケンスが完了しました。");
            return;
        }

        blinkUsesOffsetPosition = blinkSequence[blinkIndex];
    }
}