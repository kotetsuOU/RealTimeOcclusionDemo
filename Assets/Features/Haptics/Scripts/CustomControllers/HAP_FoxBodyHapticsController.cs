using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;
using Features.Haptics.Processors;

#nullable enable

/// <summary>
/// Fox のボーン階層から頭、耳、4 本の足、尻尾の座標を特定し、
/// <see cref="HAP_AUTDHapticsController"/> 向けにターゲット情報を提供するコントローラー。
/// ボーン自動検出ロジックは <see cref="HAP_FoxBoneHierarchyResolver"/> へ委譲します。
/// </summary>
public class HAP_FoxBodyHapticsController : HAP_BaseObjectHapticsController
{
    public const string TagFoxBodyHaptics = "HAP_FoxBodyHaptics";

    [Header("Body Bone Transforms")]
    [Tooltip("頭部のボーンTransform。")]
    public Transform? headBone;
    [Tooltip("左耳のボーンTransform。")]
    public Transform? leftEarBone;
    [Tooltip("右耳のボーンTransform。")]
    public Transform? rightEarBone;
    [Tooltip("左前足のボーンTransform。")]
    public Transform? frontLeftFoot;
    [Tooltip("右前足のボーンTransform。")]
    public Transform? frontRightFoot;
    [Tooltip("左後足のボーンTransform。")]
    public Transform? backLeftFoot;
    [Tooltip("右後足のボーンTransform。")]
    public Transform? backRightFoot;
    [Tooltip("尻尾のボーンTransform。")]
    public Transform? tailBone;

    [Header("Body Part Toggles")]
    public bool enableHead = true;
    public bool enableLeftEar = true;
    public bool enableRightEar = true;
    public bool enableFrontLeft = true;
    public bool enableFrontRight = true;
    public bool enableBackLeft = true;
    public bool enableBackRight = true;
    public bool enableTail = true;

    [Header("Individual Hand Contact Thresholds (部位別 手の接触判定しきい値)")]
    [Tooltip("有効時、部位ごとに個別の手の接触判定しきい値を使用します。無効時または各部位<=0時は共通のhandContactThresholdが使用されます。")]
    public bool useIndividualContactThresholds = false;
    [Tooltip("頭部の手の接触判定しきい値（メートル）。<= 0 で共通値を使用。")]
    public float headContactThreshold = -1f;
    [Tooltip("左耳の手の接触判定しきい値（メートル）。<= 0 で共通値を使用。")]
    public float leftEarContactThreshold = -1f;
    [Tooltip("右耳の手の接触判定しきい値（メートル）。<= 0 で共通値を使用。")]
    public float rightEarContactThreshold = -1f;
    [Tooltip("左前足の手の接触判定しきい値（メートル）。<= 0 で共通値を使用。")]
    public float frontLeftContactThreshold = -1f;
    [Tooltip("右前足の手の接触判定しきい値（メートル）。<= 0 で共通値を使用。")]
    public float frontRightContactThreshold = -1f;
    [Tooltip("左後足の手の接触判定しきい値（メートル）。<= 0 で共通値を使用。")]
    public float backLeftContactThreshold = -1f;
    [Tooltip("右後足の手の接触判定しきい値（メートル）。<= 0 で共通値を使用。")]
    public float backRightContactThreshold = -1f;
    [Tooltip("尻尾の手の接触判定しきい値（メートル）。<= 0 で共通値を使用。")]
    public float tailContactThreshold = -1f;

    [Header("Target Touch Directions (照射・接触向き)")]
    [FormerlySerializedAs("headTargetNormal")]
    [Tooltip("バーチャルオブジェクト（頭部・耳）が接触対象へ触れる/押し当てる向き（デフォルト: Vector3.down）。")]
    public Vector3 headTargetTouchDirection = Vector3.down;

    /// <summary>
    /// 部位ごとの有効な接触判定しきい値を返します。個別設定が無効または未設定(&lt;=0)の場合は共通値を返します。
    /// </summary>
    public float GetEffectiveContactThreshold(float individualThreshold)
        => (useIndividualContactThresholds && individualThreshold > 0f) ? individualThreshold : handContactThreshold;

    /// <summary>
    /// 各部位のターゲット情報一覧（頭 → 左耳 → 右耳 → 前左 → 前右 → 後右 → 後左 → 尻尾）を返します。
    /// 接地判定は足（4本）のみに適用し、頭・耳・尻尾は滞空中も有効とします。
    /// </summary>
    public override List<HapticsTargetInfo> TargetInfos
    {
        get
        {
            var list = new List<HapticsTargetInfo>(8);
            AddTargetIfValid(list, "Head", headBone, enableHead, true, false, headTargetTouchDirection, headContactThreshold);
            AddTargetIfValid(list, "Left Ear", leftEarBone, enableLeftEar, true, false, headTargetTouchDirection, leftEarContactThreshold);
            AddTargetIfValid(list, "Right Ear", rightEarBone, enableRightEar, true, false, headTargetTouchDirection, rightEarContactThreshold);
            AddTargetIfValid(list, "Front Left", frontLeftFoot, enableFrontLeft, false, true, footTargetTouchDirection, frontLeftContactThreshold);
            AddTargetIfValid(list, "Front Right", frontRightFoot, enableFrontRight, false, true, footTargetTouchDirection, frontRightContactThreshold);
            AddTargetIfValid(list, "Back Right", backRightFoot, enableBackRight, false, true, footTargetTouchDirection, backRightContactThreshold);
            AddTargetIfValid(list, "Back Left", backLeftFoot, enableBackLeft, false, true, footTargetTouchDirection, backLeftContactThreshold);
            AddTargetIfValid(list, "Tail", tailBone, enableTail, true, false, footTargetTouchDirection, tailContactThreshold);
            return list;
        }
    }

    private void AddTargetIfValid(
        List<HapticsTargetInfo> list,
        string partName,
        Transform? bone,
        bool isEnabled,
        bool isTail,
        bool applyAirborneCheck,
        Vector3 touchDirection,
        float individualThreshold)
    {
        if (bone == null) return;
        list.Add(new HapticsTargetInfo
        {
            Name = partName,
            Transform = bone,
            IsEnabled = isEnabled,
            IsTail = isTail,
            ApplyAirborneCheck = applyAirborneCheck,
            Offset = Vector3.zero,
            TouchDirection = touchDirection,
            HandContactThreshold = GetEffectiveContactThreshold(individualThreshold)
        });
    }

    private void Reset()
    {
        autdController = FindAnyObjectByType<HAP_AUTDHapticsController>();
        ResolveRootTransform();
        AutoDetectBones();
    }

    private void Awake()
    {
        if (autdController == null) autdController = FindAnyObjectByType<HAP_AUTDHapticsController>();
        if (rootTransform == null || rootTransform == transform) ResolveRootTransform();
        AutoDetectBones();
    }

    protected virtual void OnEnable() => RegisterSelfToController();

    protected virtual void OnDisable() { }

    public void RegisterSelfToController()
    {
        if (autdController == null) autdController = FindAnyObjectByType<HAP_AUTDHapticsController>();
        if (autdController != null && !autdController.objectHapticsControllers.Contains(this))
        {
            autdController.objectHapticsControllers.Add(this);
        }
    }

    /// <summary>
    /// キツネモデルのルート Transform を自動解決します。
    /// </summary>
    public void ResolveRootTransform()
    {
        Transform? resolved = HAP_FoxBoneHierarchyResolver.ResolveFoxRootTransform(
            transform, frontLeftFoot, headBone, tailBone, frontRightFoot, backLeftFoot, backRightFoot);
        if (resolved != null) rootTransform = resolved;
    }

    /// <summary>
    /// Fox の標準ボーン階層から全身 8 部位の Transform を自動検出してバインドします。
    /// </summary>
    public virtual void AutoDetectBones(bool forceOverwrite = false)
    {
        if (!forceOverwrite &&
            headBone != null && leftEarBone != null && rightEarBone != null &&
            frontLeftFoot != null && frontRightFoot != null &&
            backLeftFoot != null && backRightFoot != null &&
            tailBone != null)
        {
            return;
        }

        if (rootTransform == null || !rootTransform.gameObject.activeInHierarchy || rootTransform == transform)
        {
            ResolveRootTransform();
        }

        Transform searchRoot = rootTransform != null ? rootTransform : transform;
        HAP_FoxBoneHierarchyResolver.DetectBodyBones(
            searchRoot, forceOverwrite,
            ref headBone, ref leftEarBone, ref rightEarBone,
            ref frontLeftFoot, ref frontRightFoot, ref backLeftFoot, ref backRightFoot, ref tailBone);
    }
}
