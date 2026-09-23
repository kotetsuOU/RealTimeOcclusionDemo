using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class ExperimentConductor : MonoBehaviour
{
    // 並び順がラテン方格の条件番号(A〜F)に対応する
    public enum Condition
    {
        EllipseVisuoHaptic,   // A: 楕円・視覚+触覚・基準強度
        EllipseHapticOnly,    // B: 楕円・触覚のみ
        LinearXVisuoHaptic,   // C: X軸方向に往復・視覚+触覚・基準強度
        Blink,                // D: palmReference付近で点滅
        EllipsePhaseShifted,  // E: 楕円・触覚の位相を半周期(180°)ずらす
        EllipseHighIntensity  // F: 楕円・高強度
    }

    private enum State { Idle, Familiarization, WaitingNext, WaitingCapture, Running, Finished }

    [Header("参加者")]
    [Tooltip("1始まり。ラテン方格の行は (participantId - 1) % 条件数 で決まる")]
    [SerializeField] private int participantId = 1;

    [Header("参照")]
    [SerializeField] private CylinderMover stickMover;
    [SerializeField] private DownTargetFollower downTargetFollower;
    [SerializeField] private MultiAUTD3Controller autdController;

    [Header("時間設定(秒)")]
    [SerializeField] private float familiarizationDuration = 30f;
    [Tooltip("Blink以外の条件の提示時間。BlinkはBlinkシーケンスの完了で終了する")]
    [SerializeField] private float conditionDuration = 60f;

    [Header("強度(0-255)")]
    [Range(0, 255)] [SerializeField] private int baseIntensity = 125;
    [Range(0, 255)] [SerializeField] private int highIntensity = 255;

    [Header("マスキングノイズ(AUTDの駆動音を隠す)")]
    [SerializeField] private bool playMaskingNoise = true;
    [Tooltip("Play中も変更が反映される")]
    [Range(0f, 1f)] [SerializeField] private float noiseVolume = 0.3f;
    [Tooltip("中心周波数はMultiAUTD3ControllerのmodFreq(AM変調周波数)")]
    [SerializeField] private float noiseBandwidth = 100f;

    [Header("キャプチャの検証")]
    [Tooltip("初回(慣らし)の手のひら高さから、Y方向にこの距離(m)以上離れたキャプチャはやり直させる。0以下で無効")]
    [SerializeField] private float recalibrationThreshold = 0.1f;

    [Header("操作キー(遷移・キャプチャ共通)")]
    [SerializeField] private KeyCode actionKey = KeyCode.Space;
    [Tooltip("状態が変わってからこの秒数はキー入力を無視する(連打で手順を飛ばさないため)")]
    [SerializeField] private float inputCooldown = 0.5f;
    [Tooltip("デバッグ用。提示中にキーを押すとその条件を打ち切って次の待機に進む。本番ではOFFにする")]
    [SerializeField] private bool allowSkipWhileRunning = true;

    [Header("案内表示(SRDに映すためWorld Space Canvasで生成)")]
    [Tooltip("メッセージを出す位置と向き。青軸(Z)を視点から奥向きにする。未設定なら表示しない")]
    [SerializeField] private Transform messageAnchor;
    [Tooltip("未設定ならUnity組み込みフォント。日本語が□になる場合は日本語フォントを割り当てる")]
    [SerializeField] private Font messageFont;
    [SerializeField] private int messageFontSize = 40;
    [Tooltip("Canvasの1ピクセルあたりのワールド長(m)")]
    [SerializeField] private float messagePixelSize = 0.0005f;
    [SerializeField] private Vector2 messageAreaSize = new Vector2(800f, 240f);
    [SerializeField] private Color messageColor = Color.white;

    private State state = State.Idle;
    private Condition[] order;
    private int conditionIndex = -1;
    private float stateStartTime;
    private string logPath;
    private Text messageText;
    private GameObject messageBacking;
    private AudioSource noiseSource;
    private float? firstPalmY;

    private const float MessageBackingOffset = 0.001f; // テキストの1mm奥に下敷きを置く

    void Start()
    {
        if (stickMover == null || downTargetFollower == null || autdController == null)
        {
            Debug.LogError("[Conductor] 参照(stickMover / downTargetFollower / autdController)が未設定です。");
            enabled = false;
            return;
        }

        // キー入力はコンダクターが一括で扱い、キャプチャもここから呼ぶ
        stickMover.CaptureEnabled = false;
        stickMover.ResetStimulus();

        order = BuildOrder(participantId);
        InitLog();
        WriteLog("order", string.Join(">", order));
        BuildMessageUI();
        StartMaskingNoise();

        Debug.Log($"[Conductor] 参加者{participantId} 条件順: {string.Join(" > ", order)}");
        EnterState(State.Idle);
    }

    void Update()
    {
        if (noiseSource != null) noiseSource.volume = noiseVolume;

        bool pressed = Input.GetKeyDown(actionKey)
            && Time.time - stateStartTime >= inputCooldown
            && !stickMover.IsMeasuring;

        switch (state)
        {
            case State.Idle:
                if (pressed) BeginMeasure(OnFamiliarizationMeasured);
                break;

            case State.Familiarization:
                if (Time.time - stateStartTime >= familiarizationDuration) EndFamiliarization();
                break;

            case State.WaitingNext:
                if (pressed) PrepareNextCondition();
                break;

            case State.WaitingCapture:
                if (pressed) BeginMeasure(OnConditionMeasured);
                break;

            case State.Running:
                if (pressed && allowSkipWhileRunning)
                {
                    Debug.Log("[Conductor] 提示をスキップしました");
                    WriteLog("skipped");
                    EndCondition();
                }
                else if (IsConditionComplete())
                {
                    EndCondition();
                }
                break;
        }
    }

    private void BeginMeasure(Action<bool, Vector3> onMeasured)
    {
        ShowMessage("計測中です\n手を動かさないでください");
        stickMover.MeasurePalm(onMeasured);
    }

    private void OnFamiliarizationMeasured(bool ok, Vector3 palmPoint)
    {
        if (state != State.Idle) return;
        if (!ok)
        {
            ShowMessage(CaptureFailedMessage);
            return;
        }

        firstPalmY = palmPoint.y;
        Debug.Log($"[Conductor] 初回の手のひら高さ: {palmPoint.y:F4}(以降はここから±{recalibrationThreshold}m以内のみ受け付け)");

        // 本番の楕円中心と同じ点を、本番と同じ計算(ミラー反転・手の厚み)で下側の焦点位置に変換する
        Vector3 target = downTargetFollower.ComputeFocusPoint(palmPoint);
        downTargetFollower.transform.position = target;
        stickMover.SetPalmMarkerVisible(false);

        // 本番で使う強度の中間を体験してもらう
        int intensity = Mathf.RoundToInt((baseIntensity + highIntensity) / 2f);
        if (!autdController.SendFocusAtWorld(target, MultiAUTD3Controller.OutputSide.DownOnly, intensity))
        {
            Debug.LogWarning("[Conductor] 慣らしの焦点を送信できませんでした(AUTD未接続の可能性)");
        }

        EnterState(State.Familiarization);
        WriteLog("familiarization_start", FormattableString.Invariant(
            $"palmY={palmPoint.y:F4} target={FormatVector(target)} intensity={intensity}"));
    }

    private void EndFamiliarization()
    {
        autdController.ClearExternalFocus();
        EnterState(State.WaitingNext);
        WriteLog("familiarization_end");
    }

    private void PrepareNextCondition()
    {
        conditionIndex++;
        if (conditionIndex >= order.Length)
        {
            if (noiseSource != null) noiseSource.Stop();
            EnterState(State.Finished);
            WriteLog("finished");
            Debug.Log($"[Conductor] ログ: {logPath}");
            return;
        }

        ApplyCondition(order[conditionIndex]);
        stickMover.ResetStimulus();

        EnterState(State.WaitingCapture);
        WriteLog("prepare");
    }

    private void OnConditionMeasured(bool ok, Vector3 palmPoint)
    {
        if (state != State.WaitingCapture) return;
        if (!ok)
        {
            ShowMessage(CaptureFailedMessage);
            return;
        }

        // トラッキングが手の下の机などに引っ張られると、Yだけが大きく下にずれる
        if (recalibrationThreshold > 0f && firstPalmY.HasValue
            && Mathf.Abs(palmPoint.y - firstPalmY.Value) >= recalibrationThreshold)
        {
            string detail = FormattableString.Invariant($"palmY={palmPoint.y:F4} first={firstPalmY.Value:F4}");
            Debug.LogWarning($"[Conductor] 初回から{recalibrationThreshold}m以上離れているため再キャリブレーション: {detail}");
            WriteLog("capture_rejected", detail);
            ShowMessage($"もう一度キャリブレーションします\n手を目印に置き直して、{actionKey}を押してください");
            return;
        }

        stickMover.StartStimulus(palmPoint.y);

        EnterState(State.Running);
        WriteLog("start", FormattableString.Invariant($"palmY={stickMover.CapturedPalmY:F4}"));
    }

    private void ApplyCondition(Condition c)
    {
        switch (c)
        {
            case Condition.EllipseVisuoHaptic:
                stickMover.Configure(CylinderMover.MovementPattern.Ellipse, true);
                downTargetFollower.Configure(true, 0f, baseIntensity);
                break;

            case Condition.EllipseHapticOnly:
                stickMover.Configure(CylinderMover.MovementPattern.Ellipse, false);
                downTargetFollower.Configure(true, 0f, baseIntensity);
                break;

            case Condition.LinearXVisuoHaptic:
                stickMover.Configure(CylinderMover.MovementPattern.LinearX, true);
                downTargetFollower.Configure(true, 0f, baseIntensity);
                break;

            case Condition.Blink:
                stickMover.Configure(CylinderMover.MovementPattern.Blink, true);
                downTargetFollower.Configure(true, 0f, baseIntensity);
                break;

            case Condition.EllipsePhaseShifted:
                stickMover.Configure(CylinderMover.MovementPattern.Ellipse, true);
                downTargetFollower.Configure(true, 0.5f, baseIntensity);
                break;

            case Condition.EllipseHighIntensity:
                stickMover.Configure(CylinderMover.MovementPattern.Ellipse, true);
                downTargetFollower.Configure(true, 0f, highIntensity);
                break;
        }
    }

    private bool IsConditionComplete()
    {
        if (order[conditionIndex] == Condition.Blink) return stickMover.IsSequenceFinished;
        return Time.time - stateStartTime >= conditionDuration;
    }

    private void EndCondition()
    {
        stickMover.ResetStimulus();
        downTargetFollower.StopOutput();

        EnterState(State.WaitingNext);
        WriteLog("end");
    }

    private void EnterState(State next)
    {
        state = next;
        stateStartTime = Time.time;

        string message = MessageFor(next);
        ShowMessage(message);

        // Consoleは実験者だけが見るので条件名も出す
        bool inCondition = conditionIndex >= 0 && conditionIndex < order.Length;
        string cond = inCondition ? $" [{order[conditionIndex]}]" : "";
        Debug.Log($"[Conductor] {next}{cond}: {message.Replace("\n", " / ")}");
    }

    private string CaptureFailedMessage =>
        $"手の位置を取得できませんでした\n手を目印に置き直して、{actionKey}を押してください";

    // 参加者にも見えるため、条件の中身は表示しない
    private string MessageFor(State s)
    {
        int total = order.Length;
        int next = conditionIndex + 2; // WaitingNextで次に始まる条件の番号

        switch (s)
        {
            case State.Idle:
                return $"これから触覚刺激の確認を行います\n手を目印の位置に置き、{actionKey}で開始します";
            case State.Familiarization:
                return "触覚刺激の確認中です";
            case State.WaitingNext:
                if (conditionIndex < 0)
                    return $"確認は終了です\n{actionKey}で条件 1 / {total} に進みます";
                if (next > total)
                    return $"アンケートに回答してください\n回答後、{actionKey}で実験を終了します";
                return $"アンケートに回答してください\n回答後、{actionKey}で条件 {next} / {total} に進みます";
            case State.WaitingCapture:
                return $"条件 {conditionIndex + 1} / {total}\n手を目印の位置に置き、{actionKey}で開始します";
            case State.Running:
                return "";
            case State.Finished:
                return "実験は終了です\nご協力ありがとうございました";
            default:
                return "";
        }
    }

    private void BuildMessageUI()
    {
        if (messageAnchor == null) return;

        int layer = messageAnchor.gameObject.layer;

        var canvasGo = new GameObject("ConductorMessageCanvas", typeof(RectTransform), typeof(Canvas));
        canvasGo.layer = layer;
        canvasGo.transform.SetParent(messageAnchor, false);
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

        var canvasRect = (RectTransform)canvasGo.transform;
        canvasRect.sizeDelta = messageAreaSize;
        canvasRect.localScale = Vector3.one * messagePixelSize;

        var textGo = new GameObject("Message", typeof(RectTransform), typeof(Text));
        textGo.layer = layer;
        textGo.transform.SetParent(canvasGo.transform, false);

        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        messageText = textGo.GetComponent<Text>();
        messageText.font = messageFont != null ? messageFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        messageText.fontSize = messageFontSize;
        messageText.color = messageColor;
        messageText.alignment = TextAnchor.MiddleCenter;
        messageText.horizontalOverflow = HorizontalWrapMode.Wrap;
        messageText.verticalOverflow = VerticalWrapMode.Overflow;

        // UIは深度を書かないため、点群オクルージョン(PCDRendererFeature)に背景扱いされて黒で消される。
        // 深度を書く黒い板をテキストの奥に置いて、その範囲を「物体あり」にする
        messageBacking = GameObject.CreatePrimitive(PrimitiveType.Quad);
        messageBacking.name = "MessageBacking";
        messageBacking.layer = layer;
        Destroy(messageBacking.GetComponent<Collider>());
        messageBacking.transform.SetParent(messageAnchor, false);
        messageBacking.transform.localPosition = new Vector3(0f, 0f, MessageBackingOffset);
        messageBacking.transform.localScale = new Vector3(
            messageAreaSize.x * messagePixelSize, messageAreaSize.y * messagePixelSize, 1f);

        var backingRenderer = messageBacking.GetComponent<MeshRenderer>();
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlit != null) backingRenderer.material = new Material(unlit);
        backingRenderer.material.color = Color.black;
        backingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        backingRenderer.receiveShadows = false;
    }

    private void ShowMessage(string message)
    {
        if (messageText == null) return;
        bool visible = !string.IsNullOrEmpty(message);
        messageText.text = message;
        messageText.enabled = visible;
        if (messageBacking != null) messageBacking.SetActive(visible);
    }

    private void StartMaskingNoise()
    {
        if (!playMaskingNoise) return;

        float center = autdController.modFreq;
        noiseSource = gameObject.AddComponent<AudioSource>();
        noiseSource.clip = CreateBandNoise(center, noiseBandwidth);
        noiseSource.loop = true;
        noiseSource.volume = noiseVolume;
        noiseSource.playOnAwake = false;
        noiseSource.Play();

        WriteLog("masking_noise", FormattableString.Invariant(
            $"center={center:F1}Hz bandwidth={noiseBandwidth:F1}Hz volume={noiseVolume:F2}"));
    }

    // centerを中心にbandwidthの帯域へ正弦波を並べ、位相をランダムにして足し合わせた1秒のノイズ。
    // 各成分を整数Hzにそろえると1秒で必ず位相が一周するので、ループのつなぎ目でクリック音が出ない
    private static AudioClip CreateBandNoise(float center, float bandwidth)
    {
        const int sampleRate = 44100;
        const int components = 30;

        var data = new float[sampleRate];
        var rng = new System.Random(0);

        for (int k = 0; k < components; k++)
        {
            float f = Mathf.Round(center - bandwidth / 2f + bandwidth * k / (components - 1));
            float phase = (float)(rng.NextDouble() * 2.0 * Math.PI);
            for (int i = 0; i < data.Length; i++)
                data[i] += Mathf.Sin(2f * Mathf.PI * f * i / sampleRate + phase);
        }

        float max = 0f;
        for (int i = 0; i < data.Length; i++) max = Mathf.Max(max, Mathf.Abs(data[i]));
        if (max > 0f) for (int i = 0; i < data.Length; i++) data[i] /= max;

        var clip = AudioClip.Create("BandNoise", data.Length, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // 釣り合い型ラテン方格(Williams法)。1行目は 0, 1, n-1, 2, n-2, ... で、行ごとに+1ずらす
    private static Condition[] BuildOrder(int participantId)
    {
        var all = (Condition[])Enum.GetValues(typeof(Condition));
        int n = all.Length;

        var firstRow = new int[n];
        int lo = 1, hi = n - 1;
        for (int j = 1; j < n; j++) firstRow[j] = (j % 2 == 1) ? lo++ : hi--;

        int row = ((participantId - 1) % n + n) % n;
        var result = new Condition[n];
        for (int j = 0; j < n; j++) result[j] = all[(firstRow[j] + row) % n];
        return result;
    }

    private void InitLog()
    {
        string dir = Path.Combine(Application.dataPath, "..", "ExperimentLogs");
        Directory.CreateDirectory(dir);
        logPath = Path.GetFullPath(Path.Combine(dir, $"P{participantId:D2}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"));
        File.WriteAllText(logPath, "participant,orderIndex,condition,event,unityTime,systemTime,note\n");
    }

    private void WriteLog(string evt, string note = "")
    {
        bool inCondition = conditionIndex >= 0 && conditionIndex < order.Length;
        string cond = inCondition ? order[conditionIndex].ToString() : "";
        int orderIndex = inCondition ? conditionIndex + 1 : 0;

        string line = FormattableString.Invariant(
            $"{participantId},{orderIndex},{cond},{evt},{Time.time:F3},{DateTime.Now:yyyy-MM-ddTHH:mm:ss.fff},{note}\n");
        File.AppendAllText(logPath, line);
    }

    private static string FormatVector(Vector3 v) =>
        FormattableString.Invariant($"({v.x:F4} {v.y:F4} {v.z:F4})");
}
