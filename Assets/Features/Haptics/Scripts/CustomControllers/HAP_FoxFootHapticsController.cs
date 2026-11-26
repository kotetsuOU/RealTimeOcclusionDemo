using UnityEngine;
using System.Collections.Generic;
using Features.Haptics.Processors;

#nullable enable

/// <summary>
/// Fox のボーン階層から 4 本の足と尻尾の座標を特定し、
/// <see cref="HAP_AUTDHapticsController"/> 向けに足部・尻尾のターゲット情報を提供するコントローラー。
/// ボーン自動検出ロジックは <see cref="HAP_FoxBoneHierarchyResolver"/> へ委譲します。
/// </summary>
public class HAP_FoxFootHapticsController : HAP_BaseObjectHapticsController
{
    [Header("Foot Bone Transforms")]
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

    [Header("Foot Toggles")]
    public bool enableFrontLeft = true;
    public bool enableFrontRight = true;
    public bool enableBackLeft = true;
    public bool enableBackRight = true;
    public bool enableTail = true;

    /// <summary>
    /// 時計回り（前左 → 前右 → 後右 → 後左 → 尻尾）の順で各部位のターゲット情報一覧を返します。
    /// </summary>
    public override List<HapticsTargetInfo> TargetInfos
    {
        get
        {
            var list = new List<HapticsTargetInfo>(5);
            if (frontLeftFoot != null) list.Add(new HapticsTargetInfo { Name = "Front Left", Transform = frontLeftFoot, IsEnabled = enableFrontLeft, IsTail = false, TouchDirection = footTargetTouchDirection });
            if (frontRightFoot != null) list.Add(new HapticsTargetInfo { Name = "Front Right", Transform = frontRightFoot, IsEnabled = enableFrontRight, IsTail = false, TouchDirection = footTargetTouchDirection });
            if (backRightFoot != null) list.Add(new HapticsTargetInfo { Name = "Back Right", Transform = backRightFoot, IsEnabled = enableBackRight, IsTail = false, TouchDirection = footTargetTouchDirection });
            if (backLeftFoot != null) list.Add(new HapticsTargetInfo { Name = "Back Left", Transform = backLeftFoot, IsEnabled = enableBackLeft, IsTail = false, TouchDirection = footTargetTouchDirection });
            if (tailBone != null) list.Add(new HapticsTargetInfo { Name = "Tail", Transform = tailBone, IsEnabled = enableTail, IsTail = true, TouchDirection = footTargetTouchDirection });
            return list;
        }
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
            transform, frontLeftFoot, tailBone, frontRightFoot, backLeftFoot, backRightFoot);
        if (resolved != null) rootTransform = resolved;
    }

    /// <summary>
    /// Fox の標準ボーン階層から 4 本の足および尻尾の Transform を自動検出してバインドします。
    /// </summary>
    public virtual void AutoDetectBones(bool forceOverwrite = false)
    {
        if (!forceOverwrite &&
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
        HAP_FoxBoneHierarchyResolver.DetectFootBones(
            searchRoot, forceOverwrite,
            ref frontLeftFoot, ref frontRightFoot, ref backLeftFoot, ref backRightFoot, ref tailBone);
    }
}
