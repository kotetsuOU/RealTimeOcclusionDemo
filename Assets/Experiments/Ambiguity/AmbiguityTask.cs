using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 触覚定位の曖昧域の調査（恒常法・3パターン予備）。
/// 1回の実行 = 1パターン。208試行（側2 × 水準26 × 視覚2 × 繰り返し2）。
/// 触覚の出力は IAmbiguityHaptics 経由（MultiAUTD3Controller は AmbiguityMultiAUTD3Haptics が担当）。
/// </summary>
public class AmbiguityTask : MonoBehaviour
{
    private enum Phase { Setup, Running, Rest, Finished }

    [Header("被験者・パターン（開始前UIでも変更可）")]
    [Tooltip("P1, P2, ... （本人は S0）。氏名は入れない")]
    public string participant = "S0";
    [Range(1, 3)] public int pattern = 1;
    [Tooltip("パターン2・3の固定側の強度")]
    [Range(0, 255)] public int fixedValue = 125;
    [Tooltip("0 = 被験者ラベルとパターンから自動で決める")]
    public int seed = 0;

    [Header("出力・視覚")]
    [Tooltip("IAmbiguityHaptics を実装した MonoBehaviour。未設定なら同じ GameObject から探し、無ければダミー出力")]
    [SerializeField] private MonoBehaviour hapticsBehaviour;
    [Tooltip("手のひら側に出す視覚刺激（仮実装）。視覚あり試行のみ SetActive(true)")]
    [SerializeField] private GameObject visualStimulus;
    [Tooltip("ONなら触覚終了と同時に視覚を消す。OFFなら視覚も触覚と同じ長さ（どちらも 1.0 s で同じ）")]
    public bool visualOffWithHaptics = true;

    [Header("時間（秒 / ms）")]
    public float fixationSec = 1.0f;
    public float hapticDurationSec = 1.0f;
    public float visualDelayMinMs = 0f;
    public float visualDelayMaxMs = 500f;
    public float responseWindowSec = 3.0f;
    public float itiMinSec = 1.5f;
    public float itiMaxSec = 2.5f;

    [Header("進行")]
    public int breakEvery = 40;
    [Tooltip("空なら persistentDataPath/AmbiguityLogs")]
    public string logDirectory = "";
    [Tooltip("現在の試行番号・側・水準・出力強度を Console に出す（本番では OFF）")]
    public bool debugLog = true;

    private Phase _phase = Phase.Setup;
    private IAmbiguityHaptics _haptics;
    private List<AmbiguityTrial> _trials;
    private StreamWriter _writer;
    private System.Random _timingRng;
    private int _trialIndex;      // 実行中の試行（1始まり）
    private int _usedSeed;
    private string _status = "";
    private bool _showFixation;
    private string _setupParticipant, _setupSeedText, _setupFixedText;
    private int _setupPattern;

    // ------------------------------------------------------------------

    private void Awake()
    {
        _haptics = hapticsBehaviour as IAmbiguityHaptics;
        if (_haptics == null)
        {
            foreach (var mb in GetComponents<MonoBehaviour>())
                if (mb is IAmbiguityHaptics h) { _haptics = h; break; }
        }
        if (_haptics == null)
        {
            Debug.LogWarning("[Ambiguity] 触覚出力が未設定のため、ダミー出力（ログのみ）で動作します。");
            _haptics = gameObject.AddComponent<AmbiguityLogOnlyHaptics>();
        }

        SetVisual(false);
        _setupParticipant = participant;
        _setupPattern = pattern;
        _setupSeedText = seed.ToString(CultureInfo.InvariantCulture);
        _setupFixedText = fixedValue.ToString(CultureInfo.InvariantCulture);
    }

    private void OnDisable() => Shutdown();
    private void OnApplicationQuit() => Shutdown();

    private void Shutdown()
    {
        StopAllCoroutines();
        SetVisual(false);
        _haptics?.Stop();
        if (_writer != null)
        {
            try { _writer.Flush(); _writer.Dispose(); } catch { }
            _writer = null;
        }
    }

    // ------------------------------------------------------------------ UI

    private void OnGUI()
    {
        const int w = 420;
        var area = new Rect((Screen.width - w) / 2f, Screen.height * 0.25f, w, 400);
        GUILayout.BeginArea(area);
        switch (_phase)
        {
            case Phase.Setup:
                GUILayout.Label("触覚定位の曖昧域 実験");
                GUILayout.BeginHorizontal(); GUILayout.Label("被験者ラベル", GUILayout.Width(110));
                _setupParticipant = GUILayout.TextField(_setupParticipant); GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal(); GUILayout.Label("パターン", GUILayout.Width(110));
                _setupPattern = GUILayout.SelectionGrid(Mathf.Clamp(_setupPattern, 1, 3) - 1,
                    new[] { "1 同値", "2 手のひら固定", "3 甲固定" }, 3) + 1; GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal(); GUILayout.Label("固定値(0-255)", GUILayout.Width(110));
                _setupFixedText = GUILayout.TextField(_setupFixedText); GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal(); GUILayout.Label("シード(0=自動)", GUILayout.Width(110));
                _setupSeedText = GUILayout.TextField(_setupSeedText); GUILayout.EndHorizontal();
                if (GUILayout.Button("開始", GUILayout.Height(32))) BeginFromUi();
                break;
            case Phase.Rest:
                GUILayout.Label($"休憩（{_trialIndex}/{_trials.Count} 終了）\nEnter キーで再開");
                break;
            case Phase.Finished:
                GUILayout.Label("終了しました");
                break;
        }
        GUILayout.EndArea();

        if (_phase == Phase.Running && _showFixation)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 48, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, 0, Screen.width, Screen.height), "+", style);
        }
        if (!string.IsNullOrEmpty(_status) && debugLog)
            GUI.Label(new Rect(8, 8, 600, 24), _status);
    }

    private void BeginFromUi()
    {
        participant = SanitizeLabel(_setupParticipant);
        pattern = Mathf.Clamp(_setupPattern, 1, 3);
        if (int.TryParse(_setupFixedText, out int f)) fixedValue = Mathf.Clamp(f, 0, 255);
        if (int.TryParse(_setupSeedText, out int s)) seed = s;
        StartExperiment();
    }

    // ------------------------------------------------------------------ 開始

    /// <summary>Inspector の設定で開始する（UI を使わない場合はこれを呼ぶ）。</summary>
    public void StartExperiment()
    {
        if (_phase == Phase.Running) return;
        participant = SanitizeLabel(participant);
        _usedSeed = seed != 0 ? seed : AmbiguityTrialGenerator.DeriveSeed(participant, pattern);
        _trials = AmbiguityTrialGenerator.Generate(pattern, fixedValue, _usedSeed, visualDelayMinMs, visualDelayMaxMs);

        string err = AmbiguityTrialGenerator.Validate(_trials, pattern, fixedValue);
        if (err != null) { Debug.LogError("[Ambiguity] 試行リスト検証に失敗: " + err); return; }

        _timingRng = new System.Random(_usedSeed ^ 0x5A5A5A5A); // ITI 用（試行順序とは独立）
        OpenLog();
        _phase = Phase.Running;
        StartCoroutine(RunAll());
    }

    private void OpenLog()
    {
        string dir = string.IsNullOrEmpty(logDirectory)
            ? Path.Combine(Application.persistentDataPath, "AmbiguityLogs") : logDirectory;
        Directory.CreateDirectory(dir);
        string name = $"{participant}_pattern{pattern}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string path = Path.Combine(dir, name);
        _writer = new StreamWriter(path, false, new UTF8Encoding(true)) { AutoFlush = true };
        _writer.WriteLine("participant,pattern,seed,trialIndex,side,level,outputIntensity,visual,visualDelayMs,response,rtMs,timestamp");
        _writer.Flush();
        Debug.Log("[Ambiguity] ログ: " + path);
    }

    // ------------------------------------------------------------------ 進行

    private IEnumerator RunAll()
    {
        for (int i = 0; i < _trials.Count; i++)
        {
            _trialIndex = i + 1;
            yield return RunTrial(_trials[i]);

            bool last = _trialIndex == _trials.Count;
            if (!last && breakEvery > 0 && _trialIndex % breakEvery == 0)
            {
                _showFixation = false;
                _phase = Phase.Rest;
                while (!EnterPressed()) yield return null;
                _phase = Phase.Running;
            }
        }
        _showFixation = false;
        _phase = Phase.Finished;
        _haptics.Stop();
        if (_writer != null) { _writer.Flush(); _writer.Dispose(); _writer = null; }
    }

    private IEnumerator RunTrial(AmbiguityTrial t)
    {
        _status = $"trial {_trialIndex}/{_trials.Count} side={t.side} level={t.level} out={t.outputIntensity} visual={t.visual}";
        if (debugLog) Debug.Log("[Ambiguity] " + _status);

        // 1. 注視・準備
        _showFixation = true;
        _haptics.Stop();
        SetVisual(false);
        yield return WaitSeconds(fixationSec);

        // 2. 触覚提示開始（t = 0）
        double t0 = Time.realtimeSinceStartupAsDouble;
        _haptics.Present(t.side, t.outputIntensity);
        bool hapticOn = true;
        bool visualOn = false;
        double visualOnAt = t.visualDelayMs / 1000.0;
        double hapticOff = hapticDurationSec;
        // 視覚は触覚終了と同時に消す（既定）。そうでなければ触覚と同じ長さ＝開始から 1.0 s 提示
        double visualOffAt = visualOffWithHaptics ? hapticOff : visualOnAt + hapticDurationSec;

        string response = "none";
        double rtMs = double.NaN;

        // 3-4. 視覚の制御と回答受付（触覚開始から最大 responseWindowSec）
        while (true)
        {
            double now = Time.realtimeSinceStartupAsDouble - t0;

            if (t.visual && !visualOn && now >= visualOnAt && now < visualOffAt) { SetVisual(true); visualOn = true; }
            if (visualOn && now >= visualOffAt) { SetVisual(false); visualOn = false; }
            if (hapticOn && now >= hapticOff) { _haptics.Stop(); hapticOn = false; }

            if (TryReadResponse(out string r))
            {
                // 入力を取りこぼさないよう、押下を検出したフレームの時刻を RT とする
                rtMs = (Time.realtimeSinceStartupAsDouble - t0) * 1000.0;
                response = r;
                break;
            }
            if (now >= responseWindowSec) break;
            yield return null;
        }

        _haptics.Stop();   // 前の試行の出力を残さない
        SetVisual(false);
        WriteRow(t, response, rtMs);

        // 5. 試行間隔
        _showFixation = false;
        double iti = itiMinSec + _timingRng.NextDouble() * (itiMaxSec - itiMinSec);
        yield return WaitSeconds((float)iti);
    }

    private static IEnumerator WaitSeconds(float sec)
    {
        double end = Time.realtimeSinceStartupAsDouble + sec;
        while (Time.realtimeSinceStartupAsDouble < end) yield return null;
    }

    private void SetVisual(bool on)
    {
        if (visualStimulus != null && visualStimulus.activeSelf != on) visualStimulus.SetActive(on);
    }

    // ------------------------------------------------------------------ ログ

    private void WriteRow(AmbiguityTrial t, string response, double rtMs)
    {
        if (_writer == null) return;
        var inv = CultureInfo.InvariantCulture;
        string rt = double.IsNaN(rtMs) ? "" : rtMs.ToString("F1", inv);
        _writer.WriteLine(string.Join(",",
            participant, pattern, _usedSeed, _trialIndex,
            t.side.ToString().ToLowerInvariant(), t.level, t.outputIntensity,
            t.visual ? 1 : 0, t.visualDelayMs.ToString("F1", inv),
            response, rt, DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff", inv)));
        _writer.Flush();
    }

    private static string SanitizeLabel(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "S0";
        var sb = new StringBuilder();
        foreach (char c in s.Trim())
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        return sb.ToString();
    }

    // ------------------------------------------------------------------ 入力

    private static bool TryReadResponse(out string response)
    {
        response = null;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return false;
        if (kb.upArrowKey.wasPressedThisFrame) response = "up";
        else if (kb.downArrowKey.wasPressedThisFrame) response = "down";
        else if (kb.spaceKey.wasPressedThisFrame) response = "felt_nothing";
#else
        if (Input.GetKeyDown(KeyCode.UpArrow)) response = "up";
        else if (Input.GetKeyDown(KeyCode.DownArrow)) response = "down";
        else if (Input.GetKeyDown(KeyCode.Space)) response = "felt_nothing";
#endif
        return response != null;
    }

    private static bool EnterPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        return kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
    }

    // ------------------------------------------------------------------ 動作確認（仕様 10章）

    [ContextMenu("Self Check (全パターン)")]
    private void SelfCheck()
    {
        bool ok = true;
        for (int p = 1; p <= 3; p++)
        {
            int sd = AmbiguityTrialGenerator.DeriveSeed(participant, p);
            var a = AmbiguityTrialGenerator.Generate(p, fixedValue, sd, visualDelayMinMs, visualDelayMaxMs);
            var b = AmbiguityTrialGenerator.Generate(p, fixedValue, sd, visualDelayMinMs, visualDelayMaxMs);
            string err = AmbiguityTrialGenerator.Validate(a, p, fixedValue);
            bool same = a.Count == b.Count;
            for (int i = 0; same && i < a.Count; i++)
                same = a[i].side == b[i].side && a[i].level == b[i].level && a[i].visual == b[i].visual
                    && Mathf.Approximately(a[i].visualDelayMs, b[i].visualDelayMs);
            Debug.Log($"[Ambiguity SelfCheck] pattern {p}: n={a.Count} validate={(err ?? "OK")} 同シード再現={same}");
            ok &= err == null && same;
        }
        Debug.Log("[Ambiguity SelfCheck] " + (ok ? "ALL OK" : "FAILED"));
    }
}
