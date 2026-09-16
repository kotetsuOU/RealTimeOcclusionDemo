using System;
using System.Collections.Generic;
using System.Linq;
using AUTD3Sharp;
using AUTD3Sharp.Driver.Datagram;
using AUTD3Sharp.Gain;
using AUTD3Sharp.Modulation;
using UnityEngine;
using static AUTD3Sharp.Units;

#nullable enable

public class MultiAUTD3Controller : MonoBehaviour
{
    public enum ControlMode
    {
        TargetOnly,         // 常に Target の位置にフォーカスを出力するモード
        CollisionBased,     // HapCollisionDetectors の接触判定を使うモード
        IndependentFocus    // 左右のデバイスがそれぞれ独立してフォーカスを出力するモード
    }

    public enum OutputSide
    {
        Both,       // 両方出す
        UpperOnly,  // 上グループ（0,1,6,7）だけ
        DownOnly,   // 下グループ（2,3,4,5）だけ
        None
    }

    [Tooltip("実行中にW/S/D/Fキーでモード・変調切替を許可する（実験時はオフ）")]
    public bool enableDebugKeys = false;

    public enum NonCollisionBehavior
    {
        KeepLastHit,        // 最後に接触した位置にフォーカスを維持する
        KeepTarget,         // Target GameObject の位置に移動させる
        TurnOff             // 出力を停止する (Nullゲインなどを送る)
    }

    [Header("Modulation Settings")]
    [Tooltip("AM変調の周波数")]
    [Range(50f, 400f)] public float modFreq = 150f;

    [Header("Intensity Settings")]
    [Tooltip("上グループの出力（255が最大）")]
    [Range(0, 255)] public int upperIntensity = 255;

    [Tooltip("下グループの出力（255が最大）")]
    [Range(0, 255)] public int downIntensity = 255;

    [Header("Debug Settings")]
    [Tooltip("TwinCATなしでテストする場合はONにする（Nopリンクを使用）")]
    public bool useMock = false;

    [Header("Mode Settings")]
    [Tooltip("出力モードの設定")]
    public ControlMode mode = ControlMode.TargetOnly;

    [Tooltip("CollisionBased モード時、接触していない場合の挙動")]
    public NonCollisionBehavior nonCollisionBehavior = NonCollisionBehavior.TurnOff;

    [Header("Target Settings")]
    public GameObject? Target = null;

    [Tooltip("IndependentFocus モード時の第2フォーカス位置")]
    public GameObject? Target2 = null;

    [Header("Independent Focus Settings")]
    [Tooltip("Target に対応するデバイスのインデックス一覧（上側グループ）")]
    public int[] upperDeviceIndices = new[] { 0, 1, 6, 7 };

    [Tooltip("Target2 に対応するデバイスのインデックス一覧（下側グループ）")]
    public int[] downDeviceIndices = new[] { 2, 3, 4, 5 };

    [Tooltip("実行中に W / S / D キーで切り替え可能")]
    public OutputSide outputSide = OutputSide.Both;

    [Header("STM Settings")]
    [Tooltip("ONでSTM,OFFでAM変調")]
    public bool useSTM = false;

    [Tooltip("円軌道の半径")]
    [Range(0.001f, 0.01f)] public float stmRadius = 0.003f;

    [Tooltip("周期")]
    [Range(50f, 300f)] public float stmFreq = 125f;

    [Tooltip("1周の分割")]
    [Range(4, 64)] public int stmPoints = 8;

    [Header("Depth Settings")]
    [Tooltip("UpperTargetに対応するPointCloudDepthSampler")]
    [SerializeField] private PointCloudDepthSampler? upperSampler;

    [Tooltip("手の厚み（上面のY座標からこの値を引いて下側の焦点にする）")]
    [SerializeField] private float handThickness = 0.03f;

    [Tooltip("この距離以上動いたときだけ送信し直す（点群の揺れ対策）")]
    [SerializeField] private float moveThreshold = 0.002f;

    [Header("Debug Markers")]
    [Tooltip("実際に送信している焦点位置を表示するマーカー")]
    [SerializeField] private Transform? focusMarker1;
    [SerializeField] private Transform? focusMarker2;

    [Header("External Focus Settings")]
    [Tooltip("座標指定型の出力（SendFocusAtWorld）を使うとき、AM変調(Sine)を自動で適用する。"
           + "useSTM=ON のままだと Static 変調のままになり知覚できないため")]
    [SerializeField] private bool externalFocusForcesAM = true;

    private Controller? _autd = null;
    private Vector3? _oldPosition;
    private Vector3? _oldPosition2;
    private HapCollisionDetectors? _collisionDetector;
    private bool _isCurrentlyOff = false;

    // 座標指定型の出力が有効な間は Update() の自動送信を止める
    private bool _externalFocusActive = false;

    // 直近に送った変調がAM(Sine)かどうか。null は未送信
    private bool? _modIsAM = null;

    void Awake()
    {
        _collisionDetector = GetComponent<HapCollisionDetectors>();

        try
        {
            var devices = FindObjectsByType<AUTD3Device>(FindObjectsSortMode.None)
                .OrderBy(obj => obj.ID)
                .Select(obj => new AUTD3(pos: obj.transform.position, rot: obj.transform.rotation));

            if (useMock)
            {
                _autd = Controller.Open(devices, new AUTD3Sharp.Link.Nop());
                UnityEngine.Debug.Log("AUTD3: Nopリンク（モック）で起動しました。");
            }
            else
            {
                _autd = Controller.Open(devices, new AUTD3Sharp.Link.TwinCAT());
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError(ex);
            UnityEngine.Debug.LogError("Failed to connect to real device via TwinCAT. Please ensure TwinCAT is running and configured correctly.");
            return;
        }

        ApplyModulation();

        if (mode == ControlMode.TargetOnly && Target != null)
        {
            _autd.Send(new Focus(pos: Target.transform.position, option: new FocusOption()));
            _oldPosition = Target.transform.position;
        }
        else if (mode == ControlMode.IndependentFocus && (Target != null || Target2 != null))
        {
            var pos1 = CurrentPos1();
            var pos2 = CurrentPos2();
            SendIndependentFocus(pos1, pos2);
            _oldPosition = pos1;
            _oldPosition2 = pos2;
        }
    }

    // 上面のY座標を点群から取得する。失敗したらnull
    private float? GetSurfaceY()
    {
        if (upperSampler == null || Target == null) return null;

        Vector3 t = Target.transform.position;
        if (upperSampler.TryGetMedianY(t.x, t.z, out float y)) return y;
        return null;
    }

    private Vector3? CurrentPos1()
    {
        if (outputSide == OutputSide.None) return null;
        if (outputSide == OutputSide.DownOnly) return null;
        if (Target == null) return null;

        Vector3 p = Target.transform.position;
        float? surfaceY = GetSurfaceY();
        if (surfaceY.HasValue) p.y = surfaceY.Value;
        return p;
    }

    private Vector3? CurrentPos2()
    {
        if (outputSide == OutputSide.None) return null;
        if (outputSide == OutputSide.UpperOnly) return null;
        if (Target2 == null) return null;

        Vector3 p = Target2.transform.position;
        float? surfaceY = GetSurfaceY();
        if (surfaceY.HasValue) p.y = surfaceY.Value - handThickness;
        return p;
    }

    // 点群の揺れで毎フレーム再送信されないよう、一定以上動いたときだけtrue
    private bool Moved(Vector3? a, Vector3? b)
    {
        if (a == null && b == null) return false;
        if (a == null || b == null) return true;
        return Vector3.Distance(a.Value, b.Value) > moveThreshold;
    }

    private void SendIndependentFocus(Vector3? pos1, Vector3? pos2)
    {
        if (pos1 == null && pos2 == null)
        {
            _autd!.Send(new Null());
            return;
        }

        if (useSTM) SendSTM(pos1, pos2);
        else        SendStaticFocus(pos1, pos2);
    }

    private void SendStaticFocus(Vector3? pos1, Vector3? pos2)
    {
        var upperSet = new HashSet<int>(upperDeviceIndices);
        var downSet  = new HashSet<int>(downDeviceIndices);

        var gainMap = new Dictionary<object, IGain>();
        if (pos1.HasValue)
            gainMap["left"] = new Focus(pos: pos1.Value,
                option: new FocusOption { Intensity = new Intensity((byte)upperIntensity) });
        if (pos2.HasValue)
            gainMap["right"] = new Focus(pos: pos2.Value,
                option: new FocusOption { Intensity = new Intensity((byte)downIntensity) });

        if (gainMap.Count == 0)
        {
            _autd!.Send(new Null());
            return;
        }

        var gain = new GainGroup(
            keyMap: dev => tr => (pos1.HasValue && upperSet.Contains(dev.Idx())) ? "left"
                            : (pos2.HasValue && downSet.Contains(dev.Idx())) ? "right"
                            : null,
            gainMap: gainMap
        );
        _autd!.Send(gain);
    }

    private void SendSTM(Vector3? pos1, Vector3? pos2)
    {
        var upperSet = new HashSet<int>(upperDeviceIndices);
        var downSet  = new HashSet<int>(downDeviceIndices);

        var gains = new List<IGain>();

        for (int i = 0; i < stmPoints; i++)
        {
            float theta = 2f * Mathf.PI * i / stmPoints;
            Vector3 offset = new Vector3(
                stmRadius * Mathf.Cos(theta),
                0f,
                stmRadius * Mathf.Sin(theta));

            var gainMap = new Dictionary<object, IGain>();
            if (pos1.HasValue)
                gainMap["left"] = new Focus(pos: pos1.Value + offset,
                    option: new FocusOption { Intensity = new Intensity((byte)upperIntensity) });
            if (pos2.HasValue)
                gainMap["right"] = new Focus(pos: pos2.Value + offset,
                    option: new FocusOption { Intensity = new Intensity((byte)downIntensity) });

            gains.Add(new GainGroup(
                keyMap: dev => tr => (pos1.HasValue && upperSet.Contains(dev.Idx())) ? "left"
                                : (pos2.HasValue && downSet.Contains(dev.Idx())) ? "right"
                                : null,
                gainMap: gainMap
            ));
        }

        _autd!.Send(new GainSTM(gains, stmFreq * Hz, new GainSTMOption()).IntoNearest());
    }

    public void SetMode(OutputSide side)
    {
        outputSide = side;
        if (_autd == null) return;

        // OutputSide による決め打ち出力に戻すので、座標指定型の出力は解除扱いにする
        _externalFocusActive = false;

        var pos1 = CurrentPos1();
        var pos2 = CurrentPos2();

        SendIndependentFocus(pos1, pos2);

        _oldPosition  = pos1;
        _oldPosition2 = pos2;
    }

    public void ApplyModulation()
    {
        if (_autd == null) return;

        if (useSTM)
            _autd.Send(new Static());
        else
            _autd.Send(new Sine(freq: modFreq * Hz, option: new SineOption()));

        _modIsAM = !useSTM;
    }

    public void StopOutput()
    {
        SetMode(OutputSide.None);
    }

    // ------------------------------------------------------------------
    // 座標指定型の出力経路（刷り込みタスク用）
    // OutputSide(Upper/Down/Both/None) による決め打ちとは独立に、
    // 任意のワールド座標へ静止フォーカス(Focus)を出す。
    // AUTD3Sharp の Focus はワールド座標をそのまま受け取る（Controller.Open 時に
    // 各デバイスの position/rotation を渡しているため）ので、手動の座標変換は不要。
    // ------------------------------------------------------------------

    /// <summary>座標指定型の出力が有効かどうか</summary>
    public bool IsExternalFocusActive => _externalFocusActive;

    /// <summary>
    /// 指定したワールド座標へ静止フォーカスを出す。呼ばれている間は Update() の自動送信を止める。
    /// </summary>
    /// <param name="worldPos">焦点のワールド座標</param>
    /// <param name="side">出力するデバイスグループ</param>
    /// <param name="intensity">出力強度。null なら side に応じた upperIntensity / downIntensity</param>
    /// <returns>送信できたら true</returns>
    public bool SendFocusAtWorld(Vector3 worldPos, OutputSide side, int? intensity = null)
    {
        if (_autd == null) return false;

        if (side == OutputSide.None)
        {
            ClearExternalFocus();
            return false;
        }

        var devIndices = side switch
        {
            OutputSide.UpperOnly => upperDeviceIndices,
            OutputSide.DownOnly  => downDeviceIndices,
            _                    => upperDeviceIndices.Concat(downDeviceIndices).ToArray()
        };

        var devSet = new HashSet<int>(devIndices);
        if (devSet.Count == 0)
        {
            ClearExternalFocus();
            return false;
        }

        // useSTM=ON のまま Awake で Static() が送られているとAM変調が無く知覚できないため、
        // 座標指定型の出力に入るタイミングで Sine に切り替える
        if (externalFocusForcesAM && _modIsAM != true)
        {
            _autd.Send(new Sine(freq: modFreq * Hz, option: new SineOption()));
            _modIsAM = true;
        }

        int amp = intensity ?? (side == OutputSide.UpperOnly ? upperIntensity : downIntensity);

        var gainMap = new Dictionary<object, IGain>
        {
            ["ext"] = new Focus(pos: worldPos,
                option: new FocusOption { Intensity = new Intensity((byte)Mathf.Clamp(amp, 0, 255)) })
        };

        _autd.Send(new GainGroup(
            keyMap: dev => tr => devSet.Contains(dev.Idx()) ? "ext" : null,
            gainMap: gainMap
        ));

        _externalFocusActive = true;

        // 解除後に Update() が必ず再送するよう、キャッシュしている前回位置を無効化しておく
        _oldPosition  = null;
        _oldPosition2 = null;

        if (focusMarker1 != null) focusMarker1.position = worldPos;

        return true;
    }

    /// <summary>座標指定型の出力を止めて、Update() の通常制御に戻す。</summary>
    public void ClearExternalFocus()
    {
        if (!_externalFocusActive) return;

        _externalFocusActive = false;
        _autd?.Send(new Null());

        _oldPosition  = null;
        _oldPosition2 = null;
    }

    private void Update()
    {
        if (enableDebugKeys)
        {
            if (Input.GetKeyDown(KeyCode.W)) SetMode(OutputSide.UpperOnly);
            if (Input.GetKeyDown(KeyCode.S)) SetMode(OutputSide.DownOnly);
            if (Input.GetKeyDown(KeyCode.D)) SetMode(OutputSide.Both);
            if (Input.GetKeyDown(KeyCode.F)) ApplyModulation();
        }

        if (_autd == null) return;

        // 座標指定型の出力中は、こちらの自動送信で上書きしない
        if (_externalFocusActive) return;

        if (mode == ControlMode.IndependentFocus)
        {
            var pos1 = CurrentPos1();
            var pos2 = CurrentPos2();

            if (Moved(pos1, _oldPosition) || Moved(pos2, _oldPosition2))
            {
                SendIndependentFocus(pos1, pos2);
                _oldPosition = pos1;
                _oldPosition2 = pos2;

                if (focusMarker1 != null && pos1.HasValue) focusMarker1.position = pos1.Value;
                if (focusMarker2 != null && pos2.HasValue) focusMarker2.position = pos2.Value;
            }
            return;
        }

        Vector3 currentFocusPos = Vector3.zero;
        bool shouldUpdateFocus = false;
        bool shouldTurnOff = false;

        if (mode == ControlMode.TargetOnly)
        {
            if (Target != null)
            {
                currentFocusPos = Target.transform.position;
                shouldUpdateFocus = true;
            }
        }
        else if (mode == ControlMode.CollisionBased)
        {
            if (_collisionDetector != null && _collisionDetector.IsColliding)
            {
                currentFocusPos = _collisionDetector.HitPosition;
                shouldUpdateFocus = true;
                _isCurrentlyOff = false;
            }
            else
            {
                switch (nonCollisionBehavior)
                {
                    case NonCollisionBehavior.KeepLastHit:
                        break;

                    case NonCollisionBehavior.KeepTarget:
                        if (Target != null)
                        {
                            currentFocusPos = Target.transform.position;
                            shouldUpdateFocus = true;
                        }
                        _isCurrentlyOff = false;
                        break;

                    case NonCollisionBehavior.TurnOff:
                        if (!_isCurrentlyOff)
                        {
                            shouldTurnOff = true;
                            _isCurrentlyOff = true;
                        }
                        break;
                }
            }
        }

        if (shouldTurnOff)
        {
            _autd.Send(new Null());
            _oldPosition = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        }
        else if (shouldUpdateFocus && currentFocusPos != _oldPosition)
        {
            _autd.Send(new Focus(pos: currentFocusPos, option: new FocusOption()));
            _oldPosition = currentFocusPos;
        }
    }

    private void OnApplicationQuit()
    {
        _autd?.Dispose();
    }
}