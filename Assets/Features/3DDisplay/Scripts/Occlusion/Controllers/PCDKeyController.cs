using System;
using System.Collections.Generic;
using UnityEngine;
using static PCDRendererFeature;
using Core.Logging;
using Core.Keyboard;

/// <summary>
/// PCD（点群）リアルタイムオクルージョンレンダラーの各種パラメータをキーボードで切り替えるコントローラー。
/// AppKeyboard / AppKeyboardManager (Core.Keyboard) を通じて一元管理されたキーバインドに従い動作します (パターン A)。
/// 提案手法/従来手法の切替、各最適化のAblation切替、カーネル・評価モード、FadeWidth、点群カラーモードを集中管理します。
/// </summary>
[AppLoggable("PCD (Occlusion)")]
public class PCDKeyController : MonoBehaviour, IAppLoggable
{
    public const string TagPCDKey = "PCDKeyController";

    public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
    {
        const string label = "[PCDKeyController] PCD Key State";
        if (!existingLabels.Contains(label))
        {
            group.entries.Add(new LogInstanceEntry { label = label, tag = TagPCDKey, target = this, enabled = true });
            existingLabels.Add(label);
        }
    }

    [Tooltip("マテリアル切り替え用コントローラー（カラーモード切り替え用）")]
    public RsMaterialController materialController;

    protected virtual void Update()
    {
        if (PCDRendererFeature.Instance == null || PCDRendererFeature.Instance.settings == null) return;
        var s = PCDRendererFeature.Instance.settings;

        // 提案手法 / 従来手法 瞬時切り替え (デフォルト: Mキー)
        if (AppKeyboard.GetKeyDown(AppKeyAction.PCD_ToggleMethod))
        {
            bool isAnyOn = s.enableTagBasedOptimization || s.enableTypeAwareDensity ||
                           s.enableSoftOcclusionFade || (s.holeFillingMethod != PCD_HoleFillingMethod.None);
            bool toggleTo = !isAnyOn;
            s.enableTagBasedOptimization = toggleTo;
            s.enableTypeAwareDensity     = toggleTo;
            s.enableSoftOcclusionFade    = toggleTo;
            s.holeFillingMethod = toggleTo ? PCD_HoleFillingMethod.JointBilateral : PCD_HoleFillingMethod.None;
            AppLogger.Log(this, TagPCDKey, $"[PCDKeyController] 手法切り替え: {(toggleTo ? "提案手法 (全てON)" : "従来手法 (全てOFF)")}");
        }

        // ① タグスキップ最適化 (デフォルト: 1キー)
        if (AppKeyboard.GetKeyDown(AppKeyAction.PCD_ToggleTagOptimization))
        {
            s.enableTagBasedOptimization = !s.enableTagBasedOptimization;
            AppLogger.Log(this, TagPCDKey, $"[PCDKeyController] ① タグスキップ最適化: {(s.enableTagBasedOptimization ? "ON" : "OFF")}");
        }

        // ② 密度計算補正 (デフォルト: 2キー)
        if (AppKeyboard.GetKeyDown(AppKeyAction.PCD_ToggleDensity))
        {
            s.enableTypeAwareDensity = !s.enableTypeAwareDensity;
            AppLogger.Log(this, TagPCDKey, $"[PCDKeyController] ② 密度計算補正: {(s.enableTypeAwareDensity ? "ON" : "OFF")}");
        }

        // ③ ソフトフェード (デフォルト: 3キー)
        if (AppKeyboard.GetKeyDown(AppKeyAction.PCD_ToggleSoftFade))
        {
            s.enableSoftOcclusionFade = !s.enableSoftOcclusionFade;
            AppLogger.Log(this, TagPCDKey, $"[PCDKeyController] ③ ソフトフェード: {(s.enableSoftOcclusionFade ? "ON" : "OFF")}");
        }

        // ④ Hole Filling メソッドサイクル (デフォルト: 4キー)
        if (AppKeyboard.GetKeyDown(AppKeyAction.PCD_CycleHoleFilling))
        {
            s.holeFillingMethod = s.holeFillingMethod switch
            {
                PCD_HoleFillingMethod.None           => PCD_HoleFillingMethod.JointBilateral,
                PCD_HoleFillingMethod.JointBilateral => PCD_HoleFillingMethod.PullPush,
                PCD_HoleFillingMethod.PullPush        => PCD_HoleFillingMethod.Morphology_OC,
                PCD_HoleFillingMethod.Morphology_OC   => PCD_HoleFillingMethod.Morphology_CO,
                _                                    => PCD_HoleFillingMethod.None
            };
            AppLogger.Log(this, TagPCDKey, $"[PCDKeyController] ④ 穴埋め(Hole Filling): {s.holeFillingMethod}");
        }

        // FadeWidth 0↔0.2 切り替え (デフォルト: Tキー)
        if (AppKeyboard.GetKeyDown(AppKeyAction.PCD_ToggleFadeWidth))
        {
            bool isSharp = s.occlusionFadeWidth <= 0.05f;
            s.occlusionFadeWidth = isSharp ? 0.2f : 0.0f;
            AppLogger.Log(this, TagPCDKey, $"[PCDKeyController] FadeWidth: {(isSharp ? "0.2 (滑らかマスク)" : "0.0 (くっきりマスク)")}");
        }

        // Occlusion Map トグル (デフォルト: Oキー)
        if (AppKeyboard.GetKeyDown(AppKeyAction.PCD_ToggleOcclusionMap))
        {
            s.enableOcclusionMap = !s.enableOcclusionMap;
            AppLogger.Log(this, TagPCDKey, $"[PCDKeyController] Occlusion Map: {(s.enableOcclusionMap ? "ON" : "OFF")}");
        }

        // PixelTag Map トグル (デフォルト: Pキー)
        if (AppKeyboard.GetKeyDown(AppKeyAction.PCD_TogglePixelTagMap))
        {
            s.enablePixelTagMap = !s.enablePixelTagMap;
            AppLogger.Log(this, TagPCDKey, $"[PCDKeyController] PixelTag Map: {(s.enablePixelTagMap ? "ON" : "OFF")}");
        }

        // Kernel Type サイクル (デフォルト: Lキー)
        if (AppKeyboard.GetKeyDown(AppKeyAction.PCD_CycleKernelType))
        {
            s.kernelType = (PCD_OcclusionKernel)(((int)s.kernelType + 1) % Enum.GetValues(typeof(PCD_OcclusionKernel)).Length);
            AppLogger.Log(this, TagPCDKey, $"[PCDKeyController] Kernel Type: {s.kernelType}");
        }

        // Evaluation Mode サイクル (デフォルト: Kキー)
        if (AppKeyboard.GetKeyDown(AppKeyAction.PCD_CycleEvaluationMode))
        {
            s.evaluationMode = (PCD_OcclusionEvaluationMode)(((int)s.evaluationMode + 1) % Enum.GetValues(typeof(PCD_OcclusionEvaluationMode)).Length);
            AppLogger.Log(this, TagPCDKey, $"[PCDKeyController] Evaluation Mode: {s.evaluationMode}");
        }

        // Min Occluded Sectors サイクル (1-8) (デフォルト: Jキー)
        if (AppKeyboard.GetKeyDown(AppKeyAction.PCD_CycleMinSectors))
        {
            int next = (s.minOccludedSectors % 8) + 1;
            s.minOccludedSectors = next;
            AppLogger.Log(this, TagPCDKey, $"[PCDKeyController] Min Occluded Sectors: {next}");
        }

        // 点群カラーモードサイクル (デフォルト: Cキー)
        if (AppKeyboard.GetKeyDown(AppKeyAction.PCD_CycleColorMode))
        {
            if (materialController != null)
            {
                PointCloudColorMode nextMode = (PointCloudColorMode)(((int)materialController.colorMode + 1) % Enum.GetValues(typeof(PointCloudColorMode)).Length);
                materialController.ChangeColorMode(nextMode);
                AppLogger.Log(this, TagPCDKey, $"[PCDKeyController] カラーモード切り替え: {nextMode}");
            }
            else
            {
                AppLogger.LogWarning(this, TagPCDKey, "[PCDKeyController] materialController が設定されていません。");
            }
        }
    }
}
