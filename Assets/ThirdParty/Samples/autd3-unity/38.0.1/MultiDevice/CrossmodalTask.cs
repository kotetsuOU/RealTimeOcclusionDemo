using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using TMPro;

public class CrossmodalTask : MonoBehaviour
{
    public enum Side { Upper, Down, None }

    public struct Trial
    {
        public Side tactile;
        public Side visual;
    }

    [SerializeField] private string subjectID = "S01";
    [Tooltip("各条件（Tactile Upper / Tactile Down）の試行数")]
    [SerializeField] private int trialsPerCondition = 20;
    [SerializeField] private int seed = 1;
    [SerializeField] private float stimulusDuration = 0.5f;
    [SerializeField] private float itiDuration = 1.0f;
    [SerializeField] private MultiAUTD3Controller autd;
    [SerializeField] private float responseDeadline = 3.0f;
    [Tooltip("キャッチ試行（触覚なし）の数。0でなし")]
    [SerializeField] private int catchTrialCount = 0;

    [Header("Visual Stimulus")]
    [Tooltip("掌側に出す光点。Side.Upper のとき点灯")]
    [SerializeField] private GameObject visualUpper;

    [Header("Masking Noise")]
    [Tooltip("ホワイトノイズの音量")]
    [Range(0f, 1f)] [SerializeField] private float noiseVolume = 0.3f;

    [Header("Trial UI")]
    [Tooltip("Target TMP text component (TextMeshProUGUI or 3D TextMeshPro)")]
    [SerializeField] private TMP_Text trialInfoText;

    [Tooltip("Target Legacy UI Text component (if used)")]
    [SerializeField] private UnityEngine.UI.Text legacyUiText;

    [Tooltip("Optional root UI GameObject to show/hide along with the text")]
    [SerializeField] private GameObject uiRootObject;

    [Tooltip("Whether to render text overlay on screen via OnGUI")]
    [SerializeField] private bool showOnGUI = true;

    [Tooltip("Font size for OnGUI")]
    [SerializeField] private int guiFontSize = 36;

    [Tooltip("Text color for OnGUI")]
    [SerializeField] private Color guiTextColor = Color.white;

    [Tooltip("Format for next trial message ({0}: next trial number, {1}: total trials)")]
    [SerializeField] private string nextTrialFormat = "Next: {0} / {1}\n↑: Upper\n↓: Down \n no press: nothing";

    [Tooltip("Message displayed before session start ({0}: total trials)")]
    [SerializeField] private string readyFormat = "Press SPACE to start\n(Next: 1 / {0})";

    [Tooltip("Whether to show UI during response phase")]
    [SerializeField] private bool showDuringResponse = false;

    [Tooltip("Message displayed during response phase")]
    [SerializeField] private string responseFormat = "Please respond (↑: Upper / ↓: Down / no press: nothing)";

    [Tooltip("Message displayed when all trials are finished")]
    [SerializeField] private string finishedMessage = "Session Completed";

    [Header("Sample Stimulus Settings")]
    [Tooltip("本試行開始前に上側・下側のサンプル刺激を1回ずつ提示するか")]
    [SerializeField] private bool runSampleBeforeSession = true;

    [Tooltip("サンプル提示後に本試行を開始する際、スペースキー入力を待つか（falseなら自動で本試行へ）")]
    [SerializeField] private bool waitForSpaceAfterSample = true;

    [Tooltip("上側サンプル提示時の案内表示")]
    [SerializeField] private string sampleUpperText = "Sample: Upper Stimulus";

    [Tooltip("下側サンプル提示時の案内表示")]
    [SerializeField] private string sampleDownText = "Sample: Down Stimulus";

    [Tooltip("サンプル終了後の案内表示")]
    [SerializeField] private string sampleFinishedText = "Sample finished.\nPress SPACE to start trials";

    [Header("Calibration Settings")]
    [Tooltip("何回の刺激提示ごとに位置調整（キャリブレーション）インターバルを入れるか（0以下で無効）")]
    [SerializeField] private int calibrationInterval = 10;

    [Tooltip("位置調整インターバル時の表示メッセージ ({0}: 完了試行数, {1}: 総試行数)")]
    [SerializeField] private string calibrationMessage = "Calibration\nPlease adjust your hand position.\nPress SPACE to resume";

    private AudioSource noiseSource;
    private string currentUiMessage = "";
    private bool isUiVisible = false;
    private static Font cachedGuiFont;

    private List<Trial> trials = new List<Trial>();
    private double onsetTime;
    private StreamWriter writer;

    void Awake()
    {
        // シーンやInspectorに古いフォーマット（1行並びなど）が保存されている場合の自動補正
        if (string.IsNullOrEmpty(nextTrialFormat) || nextTrialFormat.Contains("|") || (!nextTrialFormat.Contains("Upper") && !nextTrialFormat.Contains("Down")))
        {
            nextTrialFormat = "Next: {0} / {1}\n↑: Upper\n↓: Down\nno press: nothing";
        }
    }

    void Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 120;

        SetVisual(true);
        SetTactile(Side.None);

        SetupNoise();
        OpenCsv();
        GenerateTrials();
        Debug.Log($"Total trials: {trials.Count}");

        SetUiText(string.Format(readyFormat, trials.Count), true);

        StartCoroutine(RunSession());
    }

    private AudioClip CreateBandNoise(float center, float bandwidth = 100f, float seconds = 1f)
{
    int rate = 44100;
    int samples = (int)(rate * seconds);
    var data = new float[samples];

    var rng = new System.Random(0);
    int n = 30;                                   // 足し合わせる成分の数

    for (int k = 0; k < n; k++)
    {
        float f = center - bandwidth / 2f + bandwidth * k / (n - 1);
        float phase = (float)(rng.NextDouble() * 2.0 * Mathf.PI);
        for (int i = 0; i < samples; i++)
            data[i] += Mathf.Sin(2f * Mathf.PI * f * i / rate + phase);
    }

    // 振幅を -1〜1 に収める
    float max = 0f;
    for (int i = 0; i < samples; i++) max = Mathf.Max(max, Mathf.Abs(data[i]));
    if (max > 0f) for (int i = 0; i < samples; i++) data[i] /= max;

    var clip = AudioClip.Create("BandNoise", samples, 1, rate, false);
    clip.SetData(data, 0);
    return clip;
    }  
    private void SetupNoise()
    {
        noiseSource = gameObject.AddComponent<AudioSource>();
        float center = (autd != null) ? autd.modFreq : 250f;
        noiseSource.clip = CreateBandNoise(center);
        noiseSource.loop = true;
        noiseSource.volume = noiseVolume;
        noiseSource.playOnAwake = false;
        noiseSource.Play();
    }

    void Update() { }

    void OnDestroy()
    {
        if (writer != null)
        {
            writer.Close();
            writer = null;
        }
    }

    private void OpenCsv()
    {
        string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"{subjectID}_{stamp}.csv";
        string path = Path.Combine(Application.persistentDataPath, fileName);

        writer = new StreamWriter(path, false);
        writer.WriteLine("subjectID,seed,trialIndex,tactileSide,visualSide,congruency,response,correct,RT_ms,onsetTime,responseTime");
        writer.Flush();

        Debug.Log($"CSV: {path}");
    }

    private void GenerateTrials()
    {
        trials.Clear();

        for (int i = 0; i < trialsPerCondition; i++)
        {
            trials.Add(new Trial { tactile = Side.Upper, visual = Side.Upper });
            trials.Add(new Trial { tactile = Side.Down,  visual = Side.Upper });
        }

        Shuffle();
        Debug.Log($"生成直後: {trials.Count} (Upper: {trialsPerCondition}回, Down: {trialsPerCondition}回)");
    }

    private void Shuffle()
    {
        System.Random rng = new System.Random(seed);

        for (int i = trials.Count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            Trial tmp = trials[i];
            trials[i] = trials[j];
            trials[j] = tmp;
        }
    }

    private Side? GetResponse()
    {
        if (Keyboard.current == null) return null;

        bool up   = Keyboard.current.upArrowKey.wasPressedThisFrame;
        bool down = Keyboard.current.downArrowKey.wasPressedThisFrame;
        bool none = Keyboard.current.leftArrowKey.wasPressedThisFrame;

        if (up)   return Side.Upper;
        if (down) return Side.Down;
        if (none) return Side.None;

        return null;
    }

    private IEnumerator RunSession()
    {
        Debug.Log("Press SPACE to start");

        yield return new WaitUntil(() => Keyboard.current != null 
        && Keyboard.current.spaceKey.wasPressedThisFrame);

        // --- サンプル提示（上側・下側を1回ずつ体験。回答の記録はしない） ---
        if (runSampleBeforeSession)
        {
            Debug.Log("--- Sample Stimulus Phase (No Recording) ---");
            yield return StartCoroutine(RunSampleStimulus(Side.Upper, sampleUpperText));
            yield return StartCoroutine(RunSampleStimulus(Side.Down, sampleDownText));

            if (waitForSpaceAfterSample)
            {
                SetVisual(true);
                SetUiText(sampleFinishedText, true);
                yield return new WaitForSecondsRealtime(0.5f); // 誤連打防止の短いクールタイム
                yield return new WaitUntil(() => Keyboard.current != null 
                && Keyboard.current.spaceKey.wasPressedThisFrame);
            }
            else
            {
                SetVisual(false);
                yield return new WaitForSecondsRealtime(itiDuration);
            }
        }

        // --- 本試行セッション ---
        for (int i = 0; i < trials.Count; i++)
        {
            yield return StartCoroutine(RunTrial(i));

            // 10回ごとのキャリブレーション・位置調整インターバル（全試行終了時は除く）
            int finishedCount = i + 1;
            if (calibrationInterval > 0 && finishedCount % calibrationInterval == 0 && finishedCount < trials.Count)
            {
                Debug.Log($"--- Calibration Break ({finishedCount}/{trials.Count}) ---");
                SetVisual(true);
                SetTactile(Side.None);
                SetUiText(string.Format(calibrationMessage, finishedCount, trials.Count), true);

                yield return new WaitForSecondsRealtime(0.5f); // 誤連打防止の短いクールタイム
                yield return new WaitUntil(() => Keyboard.current != null 
                && Keyboard.current.spaceKey.wasPressedThisFrame);
            }
        }

        Debug.Log("=== Session Finished ===");
        SetVisual(false);
        SetTactile(Side.None);
        SetUiText(finishedMessage, true);
    }

    private IEnumerator RunSampleStimulus(Side tactileSide, string label)
    {
        // 刺激前インターバル (UI表示, Visual OFF)
        SetVisual(false);
        SetTactile(Side.None);
        SetUiText(label, true);

        yield return new WaitForSecondsRealtime(itiDuration);

        // 刺激提示 (UI非表示, Visual ON, 触覚 ON)
        SetUiText("", false);
        SetVisual(true);
        SetTactile(tactileSide);

        yield return new WaitForSecondsRealtime(stimulusDuration);

        // 刺激終了 (Visual OFF, 触覚 OFF)
        SetVisual(false);
        if (autd != null) autd.StopOutput();
        SetTactile(Side.None);
    }

    private void SetTactile(Side side)
    {
        if (autd == null) return;

        switch (side)
        {
            case Side.Upper:
                autd.SetMode(MultiAUTD3Controller.OutputSide.UpperOnly);
                break;
            case Side.Down:
                autd.SetMode(MultiAUTD3Controller.OutputSide.DownOnly);
                break;
            case Side.None:
                autd.StopOutput();
                break;
        }
    }

    private void SetVisual(bool enable)
    {
        if (visualUpper != null)
        {
            visualUpper.SetActive(enable);

            // メッシュレンダラーの有効/無効も確実に制御
            var renderers = visualUpper.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = enable;
            }
        }
    }

    private void SetVisual(Side side)
    {
        SetVisual(side == Side.Upper);
    }

    private IEnumerator RunTrial(int index)
    {
        Trial trial = trials[index];

        // --- 1. Inter-Trial Interval (visual OFF, show next trial UI) ---
        SetVisual(false);
        int nextTrialNum = index + 1;
        SetUiText(string.Format(nextTrialFormat, nextTrialNum, trials.Count), true);

        yield return new WaitForSecondsRealtime(itiDuration);

        // --- 2. Stimulus Phase (visual follows trial.visual, UI OFF) ---
        SetUiText("", false);

        SetVisual(trial.visual == Side.Upper);
        SetTactile(trial.tactile);
        onsetTime = Time.realtimeSinceStartupAsDouble;

        yield return new WaitForSecondsRealtime(stimulusDuration);

        // --- 3. End of Stimulus (visual OFF, tactile OFF) ---
        SetVisual(false);
        if (autd != null) autd.StopOutput();

        // --- 4. Response Phase (visual OFF) ---
        if (showDuringResponse)
        {
            SetUiText(responseFormat, true);
        }
        else
        {
            SetUiText("", false);
        }

        Debug.Log($"[{index}] Please respond");

        Side? response = null;
        double responseTime = 0;
        double respStart = Time.realtimeSinceStartupAsDouble;

        while (Time.realtimeSinceStartupAsDouble - respStart < responseDeadline)
        {
            if (response == null)
            {
                Side? r = GetResponse();
                if (r != null)
                {
                    response = r;
                    responseTime = Time.realtimeSinceStartupAsDouble;
                    SetUiText("", false); // 回答を受理した時点で案内UIを消去
                }
            }
            yield return null;
        }

        SetUiText("", false);

        WriteTrial(index, trial, response, responseTime);
    }

    private void SetUiText(string message, bool visible = true)
    {
        currentUiMessage = message;
        isUiVisible = visible && !string.IsNullOrEmpty(message);

        if (trialInfoText != null)
        {
            trialInfoText.text = isUiVisible ? message : "";
        }

        if (legacyUiText != null)
        {
            legacyUiText.text = isUiVisible ? message : "";
        }

        if (uiRootObject != null)
        {
            uiRootObject.SetActive(isUiVisible);
        }
    }

    private static Font GetGuiFont(int size)
    {
        if (cachedGuiFont == null)
        {
            string[] fontNames = { "Yu Gothic UI", "Meiryo", "MS Gothic", "Segoe UI", "Arial" };
            cachedGuiFont = Font.CreateDynamicFontFromOSFont(fontNames, size);
        }
        return cachedGuiFont;
    }

    void OnGUI()
    {
        if (!showOnGUI || !isUiVisible || string.IsNullOrEmpty(currentUiMessage)) return;

        var font = GetGuiFont(guiFontSize);

        var style = new GUIStyle(GUI.skin.label)
        {
            font = font,
            fontSize = guiFontSize,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };

        float width = Screen.width;
        float height = guiFontSize * 5.0f;
        float x = 0;
        float y = 35f;
        Rect rect = new Rect(x, y, width, height);

        // テキストのシャドウ / 縁取り処理で背景によらず視認性を担保
        style.normal.textColor = Color.black;
        GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), currentUiMessage, style);
        GUI.Label(new Rect(rect.x - 2, rect.y - 2, rect.width, rect.height), currentUiMessage, style);
        GUI.Label(new Rect(rect.x + 2, rect.y - 2, rect.width, rect.height), currentUiMessage, style);
        GUI.Label(new Rect(rect.x - 2, rect.y + 2, rect.width, rect.height), currentUiMessage, style);

        style.normal.textColor = guiTextColor;
        GUI.Label(rect, currentUiMessage, style);
    }

    private void WriteTrial(int index, Trial trial, Side? response, double responseTime)
    {
        string modType = (autd != null && autd.useSTM) ? "STM" : "AM";
        string congruency;
            if (trial.visual == Side.None)          congruency = "novisual";
            else if (trial.tactile == trial.visual) congruency = "congruent";
            else                                     congruency = "incongruent";
        string responseStr = (response == null) ? "timeout" : response.ToString();
        string correctStr  = (response == null) ? "NA" : (response == trial.tactile).ToString();
        string rtStr       = (response == null) ? "NA" : ((responseTime - onsetTime) * 1000.0).ToString("F1");
        string respTimeStr = (response == null) ? "NA" : responseTime.ToString("F4");

        writer.WriteLine($"{subjectID},{seed},{index},{trial.tactile},{trial.visual},{congruency},{responseStr},{correctStr},{rtStr},{onsetTime:F4},{respTimeStr}");
        writer.Flush();

        Debug.Log($"[{index}] Tactile:{trial.tactile} Visual:{trial.visual} -> {responseStr} (Correct: {correctStr}) RT:{rtStr}ms");
    }
}