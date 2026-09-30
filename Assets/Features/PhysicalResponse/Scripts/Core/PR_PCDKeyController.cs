using System;
using System.Collections.Generic;
using UnityEngine;
using static PCDRendererFeature;
using Core.Logging;

namespace Features.PhysicalResponse
{
    /// <summary>
    /// PCD（点群）レンダラーの各種パラメータをキーボードでリアルタイム切り替えするコントローラー。
    /// Ablation Study 用の手法ON/OFF、カーネル・評価モード、FadeWidth等を一元管理します。
    /// PCD関連以外（アニメ/移動/LookAt）は PR_AnimationController に委譲します。
    /// </summary>
    [AppLoggable("PR (PhysicalResponse)")]
    public class PR_PCDKeyController : MonoBehaviour, IAppLoggable
    {
        public const string TagPCDKey = "PR_PCDKeyController";

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            const string label = "[PR_PCDKeyController] PCD Key State";
            if (!existingLabels.Contains(label))
            {
                group.entries.Add(new LogInstanceEntry { label = label, tag = TagPCDKey, target = this, enabled = true });
                existingLabels.Add(label);
            }
        }

        [Tooltip("マテリアル切り替え用コントローラー（Cキー用）")]
        public RsMaterialController materialController;

        private void Update()
        {
            if (PCDRendererFeature.Instance == null || PCDRendererFeature.Instance.settings == null) return;
            var s = PCDRendererFeature.Instance.settings;

            // Mキー: 提案手法 / 従来手法 瞬時切り替え
            if (Input.GetKeyDown(KeyCode.M))
            {
                bool isAnyOn = s.enableTagBasedOptimization || s.enableTypeAwareDensity ||
                               s.enableSoftOcclusionFade || (s.holeFillingMethod != PCD_HoleFillingMethod.None);
                bool toggleTo = !isAnyOn;
                s.enableTagBasedOptimization = toggleTo;
                s.enableTypeAwareDensity     = toggleTo;
                s.enableSoftOcclusionFade    = toggleTo;
                s.holeFillingMethod = toggleTo ? PCD_HoleFillingMethod.JointBilateral : PCD_HoleFillingMethod.None;
                AppLogger.Log(this, $"[PR_PCDKeyController] 手法切り替え: {(toggleTo ? "提案手法 (全てON)" : "従来手法 (全てOFF)")}");
            }

            // 1キー: タグスキップ最適化
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                s.enableTagBasedOptimization = !s.enableTagBasedOptimization;
                AppLogger.Log(this, $"[PR_PCDKeyController] ① タグスキップ最適化: {(s.enableTagBasedOptimization ? "ON" : "OFF")}");
            }

            // 2キー: 密度計算補正
            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                s.enableTypeAwareDensity = !s.enableTypeAwareDensity;
                AppLogger.Log(this, $"[PR_PCDKeyController] ② 密度計算補正: {(s.enableTypeAwareDensity ? "ON" : "OFF")}");
            }

            // 3キー: ソフトフェード
            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                s.enableSoftOcclusionFade = !s.enableSoftOcclusionFade;
                AppLogger.Log(this, $"[PR_PCDKeyController] ③ ソフトフェード: {(s.enableSoftOcclusionFade ? "ON" : "OFF")}");
            }

            // 4キー: Hole Filling メソッドサイクル
            if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                s.holeFillingMethod = s.holeFillingMethod switch
                {
                    PCD_HoleFillingMethod.None         => PCD_HoleFillingMethod.JointBilateral,
                    PCD_HoleFillingMethod.JointBilateral => PCD_HoleFillingMethod.PullPush,
                    PCD_HoleFillingMethod.PullPush      => PCD_HoleFillingMethod.Morphology_OC,
                    PCD_HoleFillingMethod.Morphology_OC => PCD_HoleFillingMethod.Morphology_CO,
                    _                                  => PCD_HoleFillingMethod.None
                };
                AppLogger.Log(this, $"[PR_PCDKeyController] ④ 穴埋め(Hole Filling): {s.holeFillingMethod}");
            }

            // Tキー: FadeWidth 0↔0.2 切り替え
            if (Input.GetKeyDown(KeyCode.T))
            {
                bool isSharp = s.occlusionFadeWidth <= 0.05f;
                s.occlusionFadeWidth = isSharp ? 0.2f : 0.0f;
                AppLogger.Log(this, $"[PR_PCDKeyController] FadeWidth: {(isSharp ? "0.2 (滑らかマスク)" : "0.0 (くっきりマスク)")}");
            }

            // Oキー: Occlusion Map
            if (Input.GetKeyDown(KeyCode.O))
            {
                s.enableOcclusionMap = !s.enableOcclusionMap;
                AppLogger.Log(this, $"[PR_PCDKeyController] Occlusion Map: {(s.enableOcclusionMap ? "ON" : "OFF")}");
            }

            // Pキー: PixelTag Map
            if (Input.GetKeyDown(KeyCode.P))
            {
                s.enablePixelTagMap = !s.enablePixelTagMap;
                AppLogger.Log(this, $"[PR_PCDKeyController] PixelTag Map: {(s.enablePixelTagMap ? "ON" : "OFF")}");
            }

            // Lキー: Kernel Type サイクル
            if (Input.GetKeyDown(KeyCode.L))
            {
                s.kernelType = (PCD_OcclusionKernel)(((int)s.kernelType + 1) % Enum.GetValues(typeof(PCD_OcclusionKernel)).Length);
                AppLogger.Log(this, $"[PR_PCDKeyController] Kernel Type: {s.kernelType}");
            }

            // Kキー: Evaluation Mode サイクル
            if (Input.GetKeyDown(KeyCode.K))
            {
                s.evaluationMode = (PCD_OcclusionEvaluationMode)(((int)s.evaluationMode + 1) % Enum.GetValues(typeof(PCD_OcclusionEvaluationMode)).Length);
                AppLogger.Log(this, $"[PR_PCDKeyController] Evaluation Mode: {s.evaluationMode}");
            }

            // Jキー: Min Occluded Sectors サイクル (1-8)
            if (Input.GetKeyDown(KeyCode.J))
            {
                int next = (s.minOccludedSectors % 8) + 1;
                s.minOccludedSectors = next;
                AppLogger.Log(this, $"[PR_PCDKeyController] Min Occluded Sectors: {next}");
            }

            // Cキー: カラーモードサイクル
            if (Input.GetKeyDown(KeyCode.C))
            {
                if (materialController != null)
                {
                    PointCloudColorMode nextMode = (PointCloudColorMode)(((int)materialController.colorMode + 1) % Enum.GetValues(typeof(PointCloudColorMode)).Length);
                    materialController.ChangeColorMode(nextMode);
                    AppLogger.Log(this, $"[PR_PCDKeyController] カラーモード切り替え: {nextMode}");
                }
                else
                {
                    AppLogger.LogWarning(this, "[PR_PCDKeyController] materialController が設定されていません。");
                }
            }
        }
    }
}
