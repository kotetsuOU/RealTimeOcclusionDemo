using UnityEngine;

namespace Core.Keyboard
{
    /// <summary>
    /// アプリケーション全体の統一キーボード制御API（静的ファサード）。
    /// AppLogger と同様に、シーン内のどこからでもシンプルな記述でキー入力を取得・判定できます。
    /// 
    /// 利用例 (パターン A):
    /// if (AppKeyboard.GetKeyDown(AppKeyAction.Weather_ToggleRain))
    /// {
    ///     weatherManager.ToggleRain();
    /// }
    /// </summary>
    public static class AppKeyboard
    {
        private static AppKeyboardManager GetManager()
        {
            if (AppKeyboardManager.Instance != null) return AppKeyboardManager.Instance;
            return Object.FindFirstObjectByType<AppKeyboardManager>();
        }

        /// <summary>
        /// 指定アクションのキーが押された瞬間であるかを判定します (Input.GetKeyDown 相当)。
        /// </summary>
        public static bool GetKeyDown(AppKeyAction action)
        {
            var mgr = GetManager();
            if (mgr != null)
            {
                return mgr.IsTriggeredDown(action);
            }

            // フォールバック（マネージャー未配置シーンでも安全に動作）
            return CheckFallbackDown(action);
        }

        /// <summary>
        /// 指定アクションのキーが押されている状態かを判定します (Input.GetKey 相当)。
        /// </summary>
        public static bool GetKey(AppKeyAction action)
        {
            var mgr = GetManager();
            if (mgr != null)
            {
                return mgr.IsTriggered(action);
            }

            return CheckFallback(action);
        }

        /// <summary>
        /// 指定アクションのキーが離された瞬間であるかを判定します (Input.GetKeyUp 相当)。
        /// </summary>
        public static bool GetKeyUp(AppKeyAction action)
        {
            var mgr = GetManager();
            if (mgr != null)
            {
                return mgr.IsTriggeredUp(action);
            }

            return CheckFallbackUp(action);
        }

        /// <summary>
        /// 指定アクションの主キー (Primary Key) を取得します。
        /// </summary>
        public static KeyCode GetPrimaryKey(AppKeyAction action)
        {
            var mgr = GetManager();
            if (mgr != null)
            {
                var entry = mgr.GetBindingEntry(action);
                if (entry != null) return entry.primaryKey;
            }
            return KeyCode.None;
        }

        /// <summary>
        /// 指定アクションの副キー (Secondary Key) を取得します。
        /// </summary>
        public static KeyCode GetSecondaryKey(AppKeyAction action)
        {
            var mgr = GetManager();
            if (mgr != null)
            {
                var entry = mgr.GetBindingEntry(action);
                if (entry != null) return entry.secondaryKey;
            }
            return KeyCode.None;
        }

        /// <summary>
        /// 指定アクションのエントリ情報を取得します。
        /// </summary>
        public static AppKeyBindingEntry GetBinding(AppKeyAction action)
        {
            var mgr = GetManager();
            return mgr != null ? mgr.GetBindingEntry(action) : null;
        }

        /// <summary>
        /// 指定アクションが有効化されているかを判定します。
        /// </summary>
        public static bool IsEnabled(AppKeyAction action)
        {
            var mgr = GetManager();
            return mgr == null || mgr.IsActionEnabled(action);
        }

        #region Fallback Support

        private static bool CheckFallbackDown(AppKeyAction action)
        {
            var key = GetDefaultKey(action, out var secKey);
            if (key != KeyCode.None && Input.GetKeyDown(key)) return true;
            if (secKey != KeyCode.None && Input.GetKeyDown(secKey)) return true;
            return false;
        }

        private static bool CheckFallback(AppKeyAction action)
        {
            var key = GetDefaultKey(action, out var secKey);
            if (key != KeyCode.None && Input.GetKey(key)) return true;
            if (secKey != KeyCode.None && Input.GetKey(secKey)) return true;
            return false;
        }

        private static bool CheckFallbackUp(AppKeyAction action)
        {
            var key = GetDefaultKey(action, out var secKey);
            if (key != KeyCode.None && Input.GetKeyUp(key)) return true;
            if (secKey != KeyCode.None && Input.GetKeyUp(secKey)) return true;
            return false;
        }

        private static KeyCode GetDefaultKey(AppKeyAction action, out KeyCode secKey)
        {
            secKey = KeyCode.None;
            return action switch
            {
                AppKeyAction.Weather_ToggleRain    => KeyCode.G,
                AppKeyAction.Weather_ToggleCloud   => KeyCode.V,
                AppKeyAction.Weather_Strike        => KeyCode.B,
                AppKeyAction.Weather_RainPreset0   => KeyCode.Alpha7,
                AppKeyAction.Weather_RainPreset30  => KeyCode.Alpha8,
                AppKeyAction.Weather_RainPreset70  => KeyCode.Alpha9,
                AppKeyAction.Weather_RainPreset100 => KeyCode.Alpha0,

                AppKeyAction.PCD_ToggleMethod          => KeyCode.M,
                AppKeyAction.PCD_ToggleTagOptimization => KeyCode.Alpha1,
                AppKeyAction.PCD_ToggleDensity         => KeyCode.Alpha2,
                AppKeyAction.PCD_ToggleSoftFade        => KeyCode.Alpha3,
                AppKeyAction.PCD_CycleHoleFilling      => KeyCode.Alpha4,
                AppKeyAction.PCD_ToggleFadeWidth       => KeyCode.T,
                AppKeyAction.PCD_ToggleOcclusionMap    => KeyCode.O,
                AppKeyAction.PCD_TogglePixelTagMap     => KeyCode.P,
                AppKeyAction.PCD_CycleKernelType       => KeyCode.L,
                AppKeyAction.PCD_CycleEvaluationMode   => KeyCode.K,
                AppKeyAction.PCD_CycleMinSectors       => KeyCode.J,
                AppKeyAction.PCD_CycleColorMode        => KeyCode.C,

                AppKeyAction.PR_ResetAnimation => KeyCode.Escape,
                AppKeyAction.PR_CaptureDebug   => SetSecondary(out secKey, KeyCode.KeypadEnter, KeyCode.Return),
                AppKeyAction.PR_SwitchTarget   => KeyCode.Tab,
                AppKeyAction.PR_ToggleAutoMove => KeyCode.Space,
                AppKeyAction.PR_MoveForward    => SetSecondary(out secKey, KeyCode.UpArrow, KeyCode.W),
                AppKeyAction.PR_MoveBack       => SetSecondary(out secKey, KeyCode.DownArrow, KeyCode.S),
                AppKeyAction.PR_MoveLeft       => SetSecondary(out secKey, KeyCode.LeftArrow, KeyCode.A),
                AppKeyAction.PR_MoveRight      => SetSecondary(out secKey, KeyCode.RightArrow, KeyCode.D),
                AppKeyAction.PR_MoveUp         => KeyCode.E,
                AppKeyAction.PR_MoveDown       => KeyCode.Q,
                AppKeyAction.PR_ToggleLookAt   => KeyCode.F,

                _ => KeyCode.None
            };
        }

        private static KeyCode SetSecondary(out KeyCode secTarget, KeyCode secVal, KeyCode primaryVal)
        {
            secTarget = secVal;
            return primaryVal;
        }

        #endregion
    }
}
