using System;
using System.Collections.Generic;
using UnityEngine;
using Core.Logging;

namespace Core.Keyboard
{
    /// <summary>
    /// アプリケーション全体のキーボードバインディングを一元集中管理する MonoBehaviour マネージャー。
    /// 各機能モジュール（Weather, PCD, PR, EXP, System等）のキー設定、重複検出、一括/グループ制御を提供します。
    /// AppLogManager と同様に、シーン内の最優先順位 [DefaultExecutionOrder(-1000)] で初期化されます。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    [AppLoggable("Keyboard")]
    public class AppKeyboardManager : MonoBehaviour, IAppLoggable
    {
        public static AppKeyboardManager Instance { get; private set; }

        public const string TagKeyboard = "AppKeyboard";

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            const string label = "[AppKeyboard] Key Action Triggers";
            if (!existingLabels.Contains(label))
            {
                group.entries.Add(new LogInstanceEntry
                {
                    label = label,
                    tag = TagKeyboard,
                    target = this,
                    enabled = true
                });
                existingLabels.Add(label);
            }
        }

        [Header("Global Control")]
        [Tooltip("全体的なキーボード入力の有効/無効トグル")]
        public bool globalEnableInput = true;

        [Header("Category Groups")]
        [Tooltip("機能モジュールごとにグループ化されたキーバインディングリスト")]
        public List<AppKeyBindingGroup> categoryGroups = new List<AppKeyBindingGroup>();

        private readonly AppKeyboardLookupEngine _lookupEngine = new AppKeyboardLookupEngine();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            if (transform.parent != null)
            {
                transform.SetParent(null);
            }
            DontDestroyOnLoad(gameObject);

            EnsureDefaultBindings();
            BuildLookup();
        }

        private void OnEnable()
        {
            if (Instance == null) Instance = this;
            EnsureDefaultBindings();
            BuildLookup();
        }

        private void OnValidate()
        {
            BuildLookup();
        }

        /// <summary>
        /// バインディングリストが空の場合、標準のデフォルトキー配置を適用します。
        /// </summary>
        public void EnsureDefaultBindings()
        {
            if (categoryGroups == null || categoryGroups.Count == 0)
            {
                categoryGroups = AppKeyDefaultBindings.CreateDefaultGroups();
            }
        }

        /// <summary>
        /// 全てのバインディングを標準デフォルト設定にリセットします。
        /// </summary>
        public void ResetToDefaults()
        {
            categoryGroups = AppKeyDefaultBindings.CreateDefaultGroups();
            BuildLookup();
        }

        /// <summary>
        /// 高速検索用ルックアップキャッシュを更新します。
        /// </summary>
        public void BuildLookup()
        {
            _lookupEngine.BuildLookup(categoryGroups, globalEnableInput);
        }

        /// <summary>
        /// 指定アクションの押下瞬間判定 (GetKeyDown)
        /// </summary>
        public bool IsTriggeredDown(AppKeyAction action)
        {
            return _lookupEngine.IsTriggeredDown(action);
        }

        /// <summary>
        /// 指定アクションの押下継続判定 (GetKey)
        /// </summary>
        public bool IsTriggered(AppKeyAction action)
        {
            return _lookupEngine.IsTriggered(action);
        }

        /// <summary>
        /// 指定アクションの離反判定 (GetKeyUp)
        /// </summary>
        public bool IsTriggeredUp(AppKeyAction action)
        {
            return _lookupEngine.IsTriggeredUp(action);
        }

        /// <summary>
        /// 指定アクションのエントリー情報を取得します。
        /// </summary>
        public AppKeyBindingEntry GetBindingEntry(AppKeyAction action)
        {
            return _lookupEngine.GetBindingEntry(action);
        }

        /// <summary>
        /// 現在のキー重複（衝突）リストを取得します。
        /// </summary>
        public IReadOnlyList<AppKeyConflictInfo> GetConflicts()
        {
            return _lookupEngine.GetConflicts();
        }

        /// <summary>
        /// 指定アクションが有効か判定します。
        /// </summary>
        public bool IsActionEnabled(AppKeyAction action)
        {
            return _lookupEngine.IsActionEnabled(action);
        }
    }
}
