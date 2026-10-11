using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.Logging
{
    /// <summary>
    /// 各ログエントリー（カテゴリ・タグ・ラベル）ごとのON/OFF状態を保持するモデル。
    /// </summary>
    [Serializable]
    public class LogEntryState
    {
        public string key;
        public string tag;
        public bool enableInfo = true;
        public bool enableWarning = true;
        public bool enableError = true;
    }

    /// <summary>
    /// ログ設定プロファイル（「通常用」「バグ調査1」など）のデータモデル。
    /// </summary>
    [Serializable]
    public class LogProfile
    {
        public string profileName = "通常用";
        public List<LogEntryState> states = new List<LogEntryState>();
    }

    /// <summary>
    /// プロファイルとカテゴリグループ間の状態保存・復元を行うサービスクラス (Pure C#)。
    /// </summary>
    public static class AppLogProfileService
    {
        public static string BuildEntryKey(string categoryName, LogInstanceEntry entry)
        {
            if (entry == null) return string.Empty;
            string identifier = !string.IsNullOrEmpty(entry.label) ? entry.label : entry.tag;
            return $"{categoryName}:{identifier}";
        }

        public static void CaptureStates(List<LogCategoryGroup> groups, LogProfile profile)
        {
            if (groups == null || profile == null) return;
            if (profile.states == null) profile.states = new List<LogEntryState>();
            profile.states.Clear();

            foreach (var group in groups)
            {
                if (group?.entries == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry == null) continue;
                    profile.states.Add(new LogEntryState
                    {
                        key = BuildEntryKey(group.categoryName, entry),
                        tag = entry.tag,
                        enableInfo = entry.enableInfo,
                        enableWarning = entry.enableWarning,
                        enableError = entry.enableError
                    });
                }
            }
        }

        public static void ApplyStates(List<LogCategoryGroup> groups, LogProfile profile)
        {
            if (groups == null || profile?.states == null) return;

            var stateDict = new Dictionary<string, LogEntryState>(StringComparer.OrdinalIgnoreCase);
            var tagDict = new Dictionary<string, LogEntryState>(StringComparer.OrdinalIgnoreCase);

            foreach (var state in profile.states)
            {
                if (state == null) continue;
                if (!string.IsNullOrEmpty(state.key)) stateDict[state.key] = state;
                if (!string.IsNullOrEmpty(state.tag)) tagDict[state.tag] = state;
            }

            foreach (var group in groups)
            {
                if (group?.entries == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry == null) continue;

                    string key = BuildEntryKey(group.categoryName, entry);
                    if (stateDict.TryGetValue(key, out var state) ||
                        (!string.IsNullOrEmpty(entry.tag) && tagDict.TryGetValue(entry.tag, out state)))
                    {
                        entry.enableInfo = state.enableInfo;
                        entry.enableWarning = state.enableWarning;
                        entry.enableError = state.enableError;
                    }
                }
            }
        }
    }
}
