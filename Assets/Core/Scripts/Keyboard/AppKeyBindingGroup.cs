using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.Keyboard
{
    /// <summary>
    /// 個別のキー割り当てエントリー。
    /// プライマリキー、セカンダリ（予備）キー、表示名、説明、有効フラグを保持します。
    /// </summary>
    [Serializable]
    public class AppKeyBindingEntry
    {
        [Tooltip("実行するキーアクション識別子")]
        public AppKeyAction action = AppKeyAction.None;

        [Tooltip("主キー (Primary Key)")]
        public KeyCode primaryKey = KeyCode.None;

        [Tooltip("副キー (Secondary Key / 予備・代替キー)")]
        public KeyCode secondaryKey = KeyCode.None;

        [Tooltip("Inspector表示用の日本語・識別名称")]
        public string displayName = "";

        [Tooltip("キーアクションの詳細説明")]
        public string description = "";

        [Tooltip("この個別キーアクションの有効/無効トグル")]
        public bool isEnabled = true;

        public AppKeyBindingEntry() { }

        public AppKeyBindingEntry(AppKeyAction action, KeyCode primaryKey, string displayName, string description = "", KeyCode secondaryKey = KeyCode.None)
        {
            this.action = action;
            this.primaryKey = primaryKey;
            this.secondaryKey = secondaryKey;
            this.displayName = displayName;
            this.description = description;
            this.isEnabled = true;
        }

        public AppKeyBindingEntry Clone()
        {
            return new AppKeyBindingEntry(action, primaryKey, displayName, description, secondaryKey)
            {
                isEnabled = this.isEnabled
            };
        }
    }

    /// <summary>
    /// 機能モジュール別（Weather, PCD, PR等）に整理されたキーバインドグループ。
    /// グループ単位での一括有効/無効化に対応します。
    /// </summary>
    [Serializable]
    public class AppKeyBindingGroup
    {
        [Tooltip("グループ名 (例: Weather, PCD, PhysicalResponse, Experiment)")]
        public string categoryName = "";

        [Tooltip("グループ全体の有効/無効トグル")]
        public bool isCategoryEnabled = true;

        [Tooltip("グループに含まれる個別キー割り当てリスト")]
        public List<AppKeyBindingEntry> entries = new List<AppKeyBindingEntry>();

        public AppKeyBindingGroup() { }

        public AppKeyBindingGroup(string categoryName)
        {
            this.categoryName = categoryName;
            this.isCategoryEnabled = true;
            this.entries = new List<AppKeyBindingEntry>();
        }
    }

    /// <summary>
    /// キー重複（衝突）の検出情報構造体。
    /// </summary>
    public struct AppKeyConflictInfo
    {
        public KeyCode Key;
        public AppKeyAction ActionA;
        public string CategoryA;
        public AppKeyAction ActionB;
        public string CategoryB;

        public AppKeyConflictInfo(KeyCode key, AppKeyAction actionA, string categoryA, AppKeyAction actionB, string categoryB)
        {
            Key = key;
            ActionA = actionA;
            CategoryA = categoryA;
            ActionB = actionB;
            CategoryB = categoryB;
        }

        public override string ToString()
        {
            return $"キー '{Key}' が重複しています: [{CategoryA}] {ActionA} と [{CategoryB}] {ActionB}";
        }
    }
}
