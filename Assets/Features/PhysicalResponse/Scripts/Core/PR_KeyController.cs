using System.Collections.Generic;
using UnityEngine;
using Core.Logging;
using Core.Keyboard;

namespace Features.PhysicalResponse
{
    /// <summary>
    /// PhysicalResponse (バーチャルオブジェクトの操作・アニメーション) 専用のキーボードコントローラー。
    /// キーボード入力を監視し、PR_AnimationController の各種操作メソッドを呼び出します。
    /// AppKeyboard / AppKeyboardManager (Core.Keyboard) を通じて一元管理されたキーバインドに従い動作します (パターン A)。
    /// </summary>
    [AppLoggable("PR (PhysicalResponse)")]
    [DisallowMultipleComponent]
    public class PR_KeyController : MonoBehaviour, IAppLoggable
    {
        public const string TagPRKey = "PR_KeyController";

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            const string label = "[PR_KeyController] Key Operations";
            if (!existingLabels.Contains(label))
            {
                group.entries.Add(new LogInstanceEntry
                {
                    label = label,
                    tag = TagPRKey,
                    target = this,
                    enabled = true
                });
                existingLabels.Add(label);
            }
        }

        [Header("Target Animation Controller")]
        [Tooltip("操作対象の PR_AnimationController (未指定時は同一GameObjectまたはシーンから自動取得)")]
        [SerializeField] private PR_AnimationController animationController;

        private void Awake()
        {
            if (animationController == null)
            {
                animationController = GetComponent<PR_AnimationController>() ?? FindFirstObjectByType<PR_AnimationController>();
            }
        }

        private void Update()
        {
            if (animationController == null) return;

            // 1. ゲーム終了 / アプリリセット (Escapeキー)
            if (AppKeyboard.GetKeyDown(AppKeyAction.PR_ResetAnimation))
            {
                AppLogger.Log(this, TagPRKey, "[PR_KeyController] アニメーションリセット/終了を実行");
                animationController.ResetOrQuit();
            }

            // 2. 撮影 (Enter / Returnキー) - デバッグ画像 & カメラ映像
            if (AppKeyboard.GetKeyDown(AppKeyAction.PR_CaptureDebug))
            {
                AppLogger.Log(this, TagPRKey, "[PR_KeyController] デバッグ撮影を実行");
                animationController.CaptureDebug();
            }

            // 3. オブジェクト切り替え (Tabキー)
            if (AppKeyboard.GetKeyDown(AppKeyAction.PR_SwitchTarget))
            {
                AppLogger.Log(this, TagPRKey, "[PR_KeyController] オブジェクト切り替えを実行");
                animationController.SwitchNextObject();
            }

            // 4. アニメーション一時停止 / 再開 (Spaceキー)
            if (AppKeyboard.GetKeyDown(AppKeyAction.PR_ToggleAutoMove))
            {
                AppLogger.Log(this, TagPRKey, "[PR_KeyController] アニメーション再生/停止をトグル");
                animationController.ToggleAnimationPlay();
            }

            // 5. 対象オブジェクトの移動 (W/A/S/D / Q/E / 方向キー)
            Vector3 move = Vector3.zero;
            if (AppKeyboard.GetKey(AppKeyAction.PR_MoveForward)) move += Vector3.forward;
            if (AppKeyboard.GetKey(AppKeyAction.PR_MoveBack))    move += Vector3.back;
            if (AppKeyboard.GetKey(AppKeyAction.PR_MoveLeft))    move += Vector3.left;
            if (AppKeyboard.GetKey(AppKeyAction.PR_MoveRight))   move += Vector3.right;
            if (AppKeyboard.GetKey(AppKeyAction.PR_MoveUp))      move += Vector3.up;
            if (AppKeyboard.GetKey(AppKeyAction.PR_MoveDown))    move += Vector3.down;

            if (move != Vector3.zero)
            {
                animationController.MoveTarget(move);
            }

            // 6. 視点(カメラ)への向き追従 (Fキーで切り替え)
            if (AppKeyboard.GetKeyDown(AppKeyAction.PR_ToggleLookAt))
            {
                animationController.ToggleLookAtCamera();
                AppLogger.Log(this, TagPRKey, $"[PR_KeyController] 視点追従: {(animationController.lookAtCamera ? "ON" : "OFF")}");
            }
        }
    }
}
