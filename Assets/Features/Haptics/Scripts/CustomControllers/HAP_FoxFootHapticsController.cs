using UnityEngine;
using System.Collections.Generic;
using System;

#nullable enable

/// <summary>
/// Foxのボーン階層から4本の足と尻尾の座標を特定し、
/// HAP_AUTDControllerの送信ループ（UpdateHaptics）内で位置にGSPAT等のホログラフィ触覚を照射するクラス。
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
    /// 各部位のターゲット情報をリストにして返します（基底クラスのGizmo描画や判定に利用）。
    /// 時計回り（前左 -> 前右 -> 後右 -> 後左 -> 尻尾）の順で定義し、シーケンシャルSTMの周回順序に合わせます。
    /// </summary>
    public override List<HapticsTargetInfo> TargetInfos
    {
        get
        {
            var list = new List<HapticsTargetInfo>();
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
        if (autdController == null)
        {
            autdController = FindAnyObjectByType<HAP_AUTDHapticsController>();
        }

        if (rootTransform == null || rootTransform == this.transform)
        {
            ResolveRootTransform();
        }

        AutoDetectBones();
    }

    protected virtual void OnEnable()
    {
        RegisterSelfToController();
    }

    protected virtual void OnDisable()
    {
        // 他のコントローラーに影響を与えずに安全に非アクティブ化
    }

    public void RegisterSelfToController()
    {
        if (autdController == null)
        {
            autdController = FindAnyObjectByType<HAP_AUTDHapticsController>();
        }

        if (autdController != null)
        {
            if (!autdController.objectHapticsControllers.Contains(this))
            {
                autdController.objectHapticsControllers.Add(this);
            }
        }
    }

    /// <summary>
    /// キツネモデルのルートTransform（各ボーンの親方向、またはシーン内のアクティブなモデル）を自動解決します。
    /// </summary>
    public void ResolveRootTransform()
    {
        // 1. 各ボーンが既にアサインされていれば、その親方向からルート（Animator/SkinnedMeshRenderer）を取得
        Transform? bone = frontLeftFoot ?? tailBone ?? frontRightFoot ?? backLeftFoot ?? backRightFoot;
        if (bone != null && bone.gameObject.activeInHierarchy)
        {
            var anim = bone.GetComponentInParent<Animator>();
            if (anim != null && anim.gameObject.activeInHierarchy) { rootTransform = anim.transform; return; }
            var smr = bone.GetComponentInParent<SkinnedMeshRenderer>();
            if (smr != null && smr.gameObject.activeInHierarchy) { rootTransform = smr.transform; return; }
            if (bone.root != null && bone.root != this.transform.root && bone.root.gameObject.activeInHierarchy) { rootTransform = bone.root; return; }
        }

        // 2. 自身がモデルの子階層にある場合
        var parentAnim = GetComponentInParent<Animator>();
        if (parentAnim != null && parentAnim.gameObject.activeInHierarchy) { rootTransform = parentAnim.transform; return; }
        var parentSmr = GetComponentInParent<SkinnedMeshRenderer>();
        if (parentSmr != null && parentSmr.gameObject.activeInHierarchy) { rootTransform = parentSmr.transform; return; }

        // 3. シーン内のアクティブなFoxモデルを探索
        var smrs = FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var s in smrs)
        {
            if (s.name.ToLower().Contains("fox") || s.transform.root.name.ToLower().Contains("fox"))
            {
                var a = s.GetComponentInParent<Animator>();
                rootTransform = a != null ? a.transform : s.transform.root;
                return;
            }
        }
    }

    /// <summary>
    /// Foxの標準的なボーン階層名から、4本の足および尻尾のTransformを自動検出してバインドします。
    /// </summary>
    public virtual void AutoDetectBones(bool forceOverwrite = false)
    {
        // rootTransform が未指定、自身、または非アクティブなオブジェクトの場合の自動解決
        if (rootTransform == null || !rootTransform.gameObject.activeInHierarchy || rootTransform == this.transform)
        {
            ResolveRootTransform();
        }

        Transform searchRoot = rootTransform != null ? rootTransform : this.transform;

        if (forceOverwrite)
        {
            frontLeftFoot = null;
            frontRightFoot = null;
            backLeftFoot = null;
            backRightFoot = null;
            tailBone = null;
        }

        // Fox prefabのボーン名: Fox_F_LLegDigit11 / Fox_F_RLegDigit11 / Fox_LLegDigit11 / Fox_RLegDigit11 を対象とする
        if (frontLeftFoot == null || !frontLeftFoot.IsChildOf(searchRoot))
            frontLeftFoot = FindChildRecursive(searchRoot, name => name.Contains("F_LLegDigit11") || name.Contains("Fox_F_LLegDigit11") || (name.ToLower().Contains("front") && name.ToLower().Contains("left") && (name.ToLower().Contains("foot") || name.ToLower().Contains("digit"))));
        
        if (frontRightFoot == null || !frontRightFoot.IsChildOf(searchRoot))
            frontRightFoot = FindChildRecursive(searchRoot, name => name.Contains("F_RLegDigit11") || name.Contains("Fox_F_RLegDigit11") || (name.ToLower().Contains("front") && name.ToLower().Contains("right") && (name.ToLower().Contains("foot") || name.ToLower().Contains("digit"))));

        if (backLeftFoot == null || !backLeftFoot.IsChildOf(searchRoot))
            backLeftFoot = FindChildRecursive(searchRoot, name => (name.Contains("LLegDigit11") && !name.Contains("F_")) || (name.ToLower().Contains("left") && (name.ToLower().Contains("foot") || name.ToLower().Contains("digit")) && !name.ToLower().Contains("front")));

        if (backRightFoot == null || !backRightFoot.IsChildOf(searchRoot))
            backRightFoot = FindChildRecursive(searchRoot, name => (name.Contains("RLegDigit11") && !name.Contains("F_")) || (name.ToLower().Contains("right") && (name.ToLower().Contains("foot") || name.ToLower().Contains("digit")) && !name.ToLower().Contains("front")));

        // 尻尾の自動検出（先端のTail6から探し、なければTail5、それでもなければTailを含むものを探す）
        if (tailBone == null || !tailBone.IsChildOf(searchRoot))
            tailBone = FindChildRecursive(searchRoot, name => name.Contains("Tail6") || name.Contains("Fox_Tail6"));
        if (tailBone == null || !tailBone.IsChildOf(searchRoot))
            tailBone = FindChildRecursive(searchRoot, name => name.Contains("Tail5") || name.Contains("Fox_Tail5"));
        if (tailBone == null || !tailBone.IsChildOf(searchRoot))
            tailBone = FindChildRecursive(searchRoot, name => name.ToLower().Contains("tail"));

        // 検出できなかった場合のフォールバックとして Ankle を探す（同一searchRoot内のみ）
        if (frontLeftFoot == null)
            frontLeftFoot = FindChildRecursive(searchRoot, name => name.Contains("F_LLegAnkle") || (name.ToLower().Contains("front") && name.ToLower().Contains("left") && name.ToLower().Contains("ankle")));
        if (frontRightFoot == null)
            frontRightFoot = FindChildRecursive(searchRoot, name => name.Contains("F_RLegAnkle") || (name.ToLower().Contains("front") && name.ToLower().Contains("right") && name.ToLower().Contains("ankle")));
        if (backLeftFoot == null)
            backLeftFoot = FindChildRecursive(searchRoot, name => (name.Contains("LLegAnkle") && !name.Contains("F_")) || (name.ToLower().Contains("left") && (name.ToLower().Contains("ankle") && !name.ToLower().Contains("front"))));
        if (backRightFoot == null)
            backRightFoot = FindChildRecursive(searchRoot, name => (name.Contains("RLegAnkle") && !name.Contains("F_")) || (name.ToLower().Contains("right") && (name.ToLower().Contains("ankle") && !name.ToLower().Contains("front"))));
    }

    private Transform? FindChildRecursive(Transform parent, Func<string, bool> predicate)
    {
        if (predicate(parent.name)) return parent;
        foreach (Transform child in parent)
        {
            var found = FindChildRecursive(child, predicate);
            if (found != null) return found;
        }
        return null;
    }
}
