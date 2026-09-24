using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CylinderMover : MonoBehaviour
{
    public enum MovementPattern { Ellipse, Linear, Blink, LinearX }

    [Header("移動パターン")]
    [SerializeField] private MovementPattern movementPattern = MovementPattern.Ellipse;

    [Header("楕円軌道設定")]
    [SerializeField] private float handWidthX = 0.07f; // 手のひらX方向全長(実測値をそのまま入力)
    [SerializeField] private float handWidthZ = 0.08f; // 手のひらZ方向全長
    [Range(0.1f, 1.0f)] [SerializeField] private float marginRatio = 0.8f; // 縮小率
    [SerializeField] private float revolutionDuration = 2.0f;

    [Header("直線移動設定(LinearはZ軸、LinearXはX軸方向に往復)")]
    [Tooltip("手の縁(handWidth/2)からさらに外側へ動く距離。片側の振れ幅 = handWidth/2 + この値。負なら手の内側で折り返す")]
    [SerializeField] private float linearHalfLength = 0.05f;
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

    [Header("手のひら高さの計測")]
    [Tooltip("この秒数の間に届いた点群フレームごとの中央値を集め、さらにその中央値を手のひら高さにする")]
    [SerializeField] private float palmMeasureDuration = 0.5f;
    [Tooltip("有効な点群フレームがこの数未満なら計測失敗")]
    [SerializeField] private int palmMeasureMinFrames = 3;

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

    private bool visualEnabled = true;

    public bool IsMoving => hasStarted;
    public Vector3 TipPosition { get; private set; }

    /// <summary>点滅モードで現在「出ている」状態かどうか。それ以外のモードでは常にtrue</summary>
    public bool IsVisible { get; private set; } = true;

    /// <summary>falseの間はキャプチャキーを受け付けない(実験コンダクターが制御)</summary>
    public bool CaptureEnabled { get; set; } = true;

    public float CapturedPalmY => capturedPalmY;
    public bool IsMeasuring { get; private set; }
    public bool IsSequenceFinished => blinkFinished;

    /// <summary>移動パターンと見た目の表示有無を設定する。触覚側の同期(IsVisible)には影響しない</summary>
    public void Configure(MovementPattern pattern, bool showVisual)
    {
        movementPattern = pattern;
        visualEnabled = showVisual;
    }

    /// <summary>提示を止めてキャプチャ待ちの状態に戻す</summary>
    public void ResetStimulus()
    {
        StopAllCoroutines(); // 計測中なら打ち切る
        IsMeasuring = false;

        hasStarted = false;
        elapsedTime = 0f;
        IsVisible = true;

        blinkSequence = null;
        blinkIndex = -1;
        blinkUsesOffsetPosition = false;
        blinkFinished = false;

        if (cylinderRenderer != null) cylinderRenderer.enabled = false;
        if (palmReference != null) palmReference.gameObject.SetActive(true);
    }

    void Start()
    {
        if (cylinderRenderer != null) cylinderRenderer.enabled = false;
    }

    /// <summary>
    /// palmReferenceのX,Zで手のひら高さを計測する(提示は開始しない)。
    /// palmMeasureDurationの間に届いた点群フレームごとの中央値を集め、その中央値を結果とする。
    /// 完了時に onDone(成功したか, 計測点) が呼ばれる
    /// </summary>
    public void MeasurePalm(System.Action<bool, Vector3> onDone)
    {
        if (IsMeasuring) return;
        StartCoroutine(MeasurePalmRoutine(onDone));
    }

    private IEnumerator MeasurePalmRoutine(System.Action<bool, Vector3> onDone)
    {
        IsMeasuring = true;

        var frameMedians = new List<float>();
        if (palmReference != null && upperSampler != null)
        {
            int lastFrame = upperSampler.FrameCount;
            float endTime = Time.time + palmMeasureDuration;

            while (Time.time < endTime)
            {
                // Unityのフレームは点群より速いので、新しい点群フレームが来たときだけ数える
                if (upperSampler.FrameCount != lastFrame)
                {
                    lastFrame = upperSampler.FrameCount;
                    if (upperSampler.TryGetFrameMedianY(palmReference.position.x, palmReference.position.z, out float y))
                        frameMedians.Add(y);
                }
                yield return null;
            }
        }

        IsMeasuring = false;

        if (frameMedians.Count < palmMeasureMinFrames)
        {
            Debug.LogWarning($"[CylinderMover] 手のひら高さの取得に失敗（有効な点群フレーム {frameMedians.Count} / 必要 {palmMeasureMinFrames}）");
            onDone(false, Vector3.zero);
            yield break;
        }

        frameMedians.Sort();
        float palmY = frameMedians[frameMedians.Count / 2];
        Debug.Log($"[CylinderMover] 手のひら高さ: {palmY:F4}（{frameMedians.Count}フレーム、範囲 {frameMedians[0]:F4}〜{frameMedians[frameMedians.Count - 1]:F4}）");

        onDone(true, new Vector3(palmReference.position.x, palmY, palmReference.position.z));
    }

    public void SetPalmMarkerVisible(bool visible)
    {
        if (palmReference != null) palmReference.gameObject.SetActive(visible);
    }

    /// <summary>計測済みの手のひら高さで提示を開始する</summary>
    public void StartStimulus(float palmY)
    {
        capturedPalmY = palmY;
        hasStarted = true;
        SetPalmMarkerVisible(false);

        if (movementPattern == MovementPattern.Blink)
        {
            BuildBlinkSequence();
            IsVisible = false; // Update()の最初のフレームで確実に1件目を消費させる
        }

        Debug.Log($"[CylinderMover] 手のひら高さを取得: {capturedPalmY:F4}");
    }

    void Update()
    {
        if (!hasStarted)
        {
            if (CaptureEnabled && !IsMeasuring && Input.GetKeyDown(captureKey))
            {
                MeasurePalm((ok, palm) => { if (ok) StartStimulus(palm.y); });
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
            case MovementPattern.Linear:
            case MovementPattern.LinearX:
                tipPosition = ComputeTrajectoryTip(elapsedTime);
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
                tipPosition = new Vector3(centerX, capturedPalmY, centerZ - blinkOffsetZ);
                break;

            default:
                tipPosition = transform.position; // フォールバック（通常到達しない）
                break;
        }

        if (movementPattern != MovementPattern.Blink) IsVisible = true;

        TipPosition = tipPosition;
        transform.position = tipPosition + Vector3.up * (cylinderHalfLength + visualHeightOffset);
        cylinderRenderer.enabled = IsVisible && visualEnabled;
    }

    /// <summary>1周期(楕円は1周、直線は1往復)の秒数</summary>
    public float CycleDuration => IsLinearPattern ? linearMoveDuration * 2f : revolutionDuration;

    private bool IsLinearPattern =>
        movementPattern == MovementPattern.Linear || movementPattern == MovementPattern.LinearX;

    /// <summary>
    /// 軌道上で、今の位置から phaseLagCycles 周期分だけ遅れた位置(0.5で半周期)。
    /// 周期運動なので開始直後から値が得られる。Blinkでは現在の位置をそのまま返す
    /// </summary>
    public Vector3 GetTipPositionWithPhaseLag(float phaseLagCycles)
    {
        if (!hasStarted || movementPattern == MovementPattern.Blink) return TipPosition;
        return ComputeTrajectoryTip(elapsedTime - phaseLagCycles * CycleDuration);
    }

    // 楕円・直線の軌道上で、開始からtime秒時点の先端位置
    private Vector3 ComputeTrajectoryTip(float time)
    {
        float centerX = palmReference.position.x;
        float centerZ = palmReference.position.z;

        if (IsLinearPattern)
        {
            // -1 〜 +1 を往復する
            float s = Mathf.PingPong(time, linearMoveDuration) / linearMoveDuration * 2f - 1f;

            return movementPattern == MovementPattern.LinearX
                ? new Vector3(centerX + s * (handWidthX / 2f + linearHalfLength), capturedPalmY, centerZ)
                : new Vector3(centerX, capturedPalmY, centerZ + s * (handWidthZ / 2f + linearHalfLength));
        }

        float radiusX = (handWidthX / 2f) * marginRatio;
        float radiusZ = (handWidthZ / 2f) * marginRatio;

        float angle = time / revolutionDuration * 2f * Mathf.PI;

        float tipX = centerX + radiusX * Mathf.Cos(angle);
        float tipZ = centerZ + radiusZ * Mathf.Sin(angle);

        return new Vector3(tipX, capturedPalmY, tipZ);
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