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
        IndependentFocus,   // 左右のデバイスがそれぞれ独立してフォーカスを出力するモード
        GraspBased          // 握り込み中は手の接触位置ごとに複数フォーカスを出力し、それ以外は CollisionBased と同じ挙動のモード
    }

    public enum NonCollisionBehavior
    {
        KeepLastHit,        // 最後に接触した位置にフォーカスを維持する
        KeepTarget,         // Target GameObject の位置に移動させる
        TurnOff             // 出力を停止する (Nullゲインなどを送る)
    }

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
    public int[] leftDeviceIndices = new[] { 0, 1, 2, 3 };

    [Tooltip("Target2 に対応するデバイスのインデックス一覧（右側グループ）")]
    public int[] rightDeviceIndices = new[] { 4, 5, 6, 7 };

    private Controller? _autd = null;
    private HapGraspInteractor? _graspInteractor;
    private Vector3[] _deviceCenters = Array.Empty<Vector3>();
    private readonly List<Vector3> _lastGraspFoci = new List<Vector3>();
    private bool _isGraspOutput = false;
    private Vector3 _oldPosition;
    private Vector3 _oldPosition2;
    private HapCollisionDetectors? _collisionDetector;
    private bool _isCurrentlyOff = false;

    void Awake()
    {
        _collisionDetector = GetComponent<HapCollisionDetectors>();
        _graspInteractor = GetComponent<HapGraspInteractor>();

        try
        {
            var deviceObjects = FindObjectsByType<AUTD3Device>(FindObjectsSortMode.None)
                .OrderBy(obj => obj.ID)
                .ToArray();
            _deviceCenters = deviceObjects.Select(GetDeviceCenter).ToArray();
            var devices = deviceObjects
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

        _autd.Send(new Sine(freq: 150 * Hz, option: new SineOption()));

        if (mode == ControlMode.TargetOnly && Target != null)
        {
            _autd.Send(new Focus(pos: Target.transform.position, option: new FocusOption()));
            _oldPosition = Target.transform.position;
        }
        else if (mode == ControlMode.IndependentFocus && Target != null && Target2 != null)
        {
            SendIndependentFocus(Target.transform.position, Target2.transform.position);
            _oldPosition = Target.transform.position;
            _oldPosition2 = Target2.transform.position;
        }
    }

    private void SendIndependentFocus(Vector3 pos1, Vector3 pos2)
    {
        var leftSet = new HashSet<int>(leftDeviceIndices);
        var rightSet = new HashSet<int>(rightDeviceIndices);

        var gain = new GainGroup(
            keyMap: dev => tr => leftSet.Contains(dev.Idx()) ? "left"
                               : rightSet.Contains(dev.Idx()) ? "right"
                               : null,
            gainMap: new Dictionary<object, IGain>
            {
                { "left",  new Focus(pos: pos1, option: new FocusOption()) },
                { "right", new Focus(pos: pos2, option: new FocusOption()) }
            }
        );
        _autd!.Send(gain);
    }

    // デバイス(の子Renderer)の中心位置。握り込み時に各デバイスへ最寄りのフォーカスを割り当てるために使う
    private static Vector3 GetDeviceCenter(AUTD3Device device)
    {
        var renderers = device.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return device.transform.position;

        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds.center;
    }

    /// <summary>
    /// 複数のフォーカスを同時に出力する。各デバイスは最寄りのフォーカス1つを担当する
    /// （どのデバイスからも最寄りにならなかったフォーカスは出力されない）。
    /// </summary>
    private void SendMultiFocus(IReadOnlyList<Vector3> foci)
    {
        var deviceKeys = new string?[_deviceCenters.Length];
        var gainMap = new Dictionary<object, IGain>();

        for (int d = 0; d < _deviceCenters.Length; d++)
        {
            int nearest = 0;
            float nearestSqr = float.MaxValue;
            for (int f = 0; f < foci.Count; f++)
            {
                float sqr = (foci[f] - _deviceCenters[d]).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = f;
                }
            }

            string key = "focus" + nearest;
            deviceKeys[d] = key;
            if (!gainMap.ContainsKey(key))
                gainMap[key] = new Focus(pos: foci[nearest], option: new FocusOption());
        }

        var gain = new GainGroup(
            keyMap: dev => tr => deviceKeys[dev.Idx()],
            gainMap: gainMap
        );
        _autd!.Send(gain);
    }

    /// <summary>握り込み中の複数フォーカス出力。フォーカスがほとんど動いていなければ再送しない</summary>
    private void UpdateGraspOutput(List<Vector3> foci)
    {
        const float resendThresholdSqr = 0.0005f * 0.0005f; // 0.5mm

        bool changed = !_isGraspOutput || foci.Count != _lastGraspFoci.Count;
        for (int i = 0; !changed && i < foci.Count; i++)
            changed = (foci[i] - _lastGraspFoci[i]).sqrMagnitude > resendThresholdSqr;

        _isGraspOutput = true;
        _isCurrentlyOff = false;
        _oldPosition = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);

        if (!changed) return;

        SendMultiFocus(foci);
        _lastGraspFoci.Clear();
        _lastGraspFoci.AddRange(foci);
    }

    private void Update()
    {
        if (_autd == null) return;

        if (mode == ControlMode.GraspBased
            && _graspInteractor != null && _graspInteractor.IsGrasped
            && _graspInteractor.GraspFoci.Count > 0 && _deviceCenters.Length > 0)
        {
            UpdateGraspOutput(_graspInteractor.GraspFoci);
            return;
        }
        _isGraspOutput = false;

        if (mode == ControlMode.IndependentFocus)
        {
            if (Target == null || Target2 == null) return;

            var pos1 = Target.transform.position;
            var pos2 = Target2.transform.position;

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
        else if (mode == ControlMode.CollisionBased || mode == ControlMode.GraspBased)
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
