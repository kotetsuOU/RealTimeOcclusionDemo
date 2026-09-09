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
    UpperOnly,   // 左グループ（0,1,6,7）だけ
    DownOnly,   // 右グループ（2,3,4,5）だけ
    None
}
    [Tooltip("実行中にA/S/Dキーでモード切替を許可する（実験集はオフ）")]
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
    [Tooltip("Target に対応するデバイスのインデックス一覧（左側グループ）")]
    public int[] upperDeviceIndices = new[] { 0, 1, 6, 7 };

    [Tooltip("Target2 に対応するデバイスのインデックス一覧（右側グループ）")]
    public int[] downDeviceIndices = new[] { 2, 3, 4, 5 };

    [Tooltip("実行中に 1 / 2 / 3 キーで切り替え可能")]
    public OutputSide outputSide = OutputSide.Both; 

    [Header("STM Settings")]
    [Tooltip("ONでSTM,OFFでAM変調")]
    public bool useSTM = false;

    [Tooltip("円軌道の半径")]
    [Range(0.001f, 0.01f)]public float stmRadius = 0.003f;

    [Tooltip("周期")]
    [Range(50f, 300f)] public float stmFreq = 150f;

    [Tooltip("1周の分割")]
    [Range(8, 64)] public int stmPoints = 20;

    [SerializeField] private PointCloudDepthSampler upperSampler;
    [SerializeField] private PointCloudDepthSampler lowerSampler;  

    private Controller? _autd = null;
    private Vector3? _oldPosition;
    private Vector3? _oldPosition2;
    private HapCollisionDetectors? _collisionDetector;
    private bool _isCurrentlyOff = false;

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

        _autd.Send(new Sine(freq: modFreq * Hz, option: new SineOption()));

        if (mode == ControlMode.TargetOnly && Target != null)
        {
            _autd.Send(new Focus(pos: Target.transform.position, option: new FocusOption()));
            _oldPosition = Target.transform.position;
        }
        else if (mode == ControlMode.IndependentFocus && (Target != null || Target2 != null))
        {
            SendIndependentFocus(Target?.transform.position, Target2?.transform.position);
            _oldPosition = Target?.transform.position;
            _oldPosition2 = Target2?.transform.position;
        }
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
        var leftSet = new HashSet<int>(upperDeviceIndices);
        var rightSet = new HashSet<int>(downDeviceIndices);

        // ① 指示書を空で作って、必要なぶんだけ入れる
        var gainMap = new Dictionary<object, IGain>();
        if (pos1.HasValue)
            gainMap["left"] = new Focus(pos: pos1.Value, option: new FocusOption{Intensity = new Intensity((byte)upperIntensity)});
        if (pos2.HasValue)
            gainMap["right"] = new Focus(pos: pos2.Value, option: new FocusOption{Intensity = new Intensity((byte)downIntensity)});

        // ② 両方ないなら全停止
        if (gainMap.Count == 0)
        {
            _autd!.Send(new Null());
            return;
        }

        // ③ 名札付け。座標がない側には名札を付けない
        var gain = new GainGroup(
            keyMap: dev => tr => (pos1.HasValue && leftSet.Contains(dev.Idx()))  ? "left"
                            : (pos2.HasValue && rightSet.Contains(dev.Idx())) ? "right"
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
        private Vector3? CurrentPos1()
    {
        if(outputSide == OutputSide.None) return null;
        if (outputSide == OutputSide.DownOnly) return null;
        if (Target == null) return null;                       // 設定し忘れ対策
        
        Vector3 p = Target.transform.position;
        if (upperSampler != null && upperSampler.TryGetMedianY(p.x, p.z, out float y))
        p.y = y;
        return p;
    }

    private Vector3? CurrentPos2()
    {
        if(outputSide == OutputSide.None) return null;
        if (outputSide == OutputSide.UpperOnly) return null;    // 左だけモードなら右は出さない
        if (Target2 == null) return null;
        
        Vector3 p = Target.transform.position;
        if (lowerSampler != null && lowerSampler.TryGetMedianY(p.x, p.z, out float y))
        p.y = y;
        return p;
    }
    public void SetMode(OutputSide side)
    {
        outputSide = side;
        if (_autd == null) return;

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
    }

    public void StopOutput()
    {
        SetMode(OutputSide.None);
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

        if (mode == ControlMode.IndependentFocus)
        {
            var pos1 = CurrentPos1();
            var pos2 = CurrentPos2();

            if (pos1 != _oldPosition || pos2 != _oldPosition2)
            {
                SendIndependentFocus(pos1, pos2);
                _oldPosition = pos1;
                _oldPosition2 = pos2;
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

#nullable restore
