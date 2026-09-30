using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.Logging
{
    /// <summary>
    /// AppLogManager のログエントリーに対する高速ルックアップ（ターゲット別・タグ別・名前別）および有効判定を担当するクラス。
    /// </summary>
    public class AppLogLookupEngine
    {
        private readonly object _lock = new object();
        private readonly Dictionary<UnityEngine.Object, LogInstanceEntry> _objectLookup = new Dictionary<UnityEngine.Object, LogInstanceEntry>();
        private readonly Dictionary<string, LogInstanceEntry> _nameLookup = new Dictionary<string, LogInstanceEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LogInstanceEntry> _targetTagLookup = new Dictionary<string, LogInstanceEntry>(StringComparer.OrdinalIgnoreCase);

        public void BuildLookup(List<LogCategoryGroup> categoryGroups)
        {
            lock (_lock)
            {
                _objectLookup.Clear();
                _nameLookup.Clear();
                _targetTagLookup.Clear();

                if (categoryGroups == null) return;

                foreach (var group in categoryGroups)
                {
                    if (group == null || group.entries == null) continue;

                    foreach (var entry in group.entries)
                    {
                        if (entry == null) continue;

                        if (!ReferenceEquals(entry.target, null))
                        {
                            try
                            {
                                if (!_objectLookup.ContainsKey(entry.target))
                                {
                                    _objectLookup[entry.target] = entry;
                                }

                                if (!string.IsNullOrEmpty(entry.tag))
                                {
                                    string key = GetTargetTagKey(entry.target, entry.tag);
                                    _targetTagLookup[key] = entry;
                                }
                            }
                            catch (Exception) { }
                        }

                        if (!string.IsNullOrEmpty(entry.tag))
                        {
                            _nameLookup[entry.tag] = entry;
                        }

                        if (!string.IsNullOrEmpty(entry.label))
                        {
                            _nameLookup[entry.label] = entry;
                        }
                    }
                }
            }
        }

        public bool IsLogEnabled(UnityEngine.Object targetObject, AppLogLevel level, string subTag, bool globalEnabled)
        {
            if (!globalEnabled) return false;

            lock (_lock)
            {
                try
                {
                    if (!string.IsNullOrEmpty(subTag))
                    {
                        if (!ReferenceEquals(targetObject, null))
                        {
                            string targetTagKey = GetTargetTagKey(targetObject, subTag);
                            if (_targetTagLookup.TryGetValue(targetTagKey, out var targetTagEntry))
                            {
                                return targetTagEntry.IsEnabled(level);
                            }
                        }

                        if (_nameLookup.TryGetValue(subTag, out var tagEntry))
                        {
                            return tagEntry.IsEnabled(level);
                        }
                    }

                    if (ReferenceEquals(targetObject, null)) return true;

                    if (_objectLookup.TryGetValue(targetObject, out var entry))
                    {
                        return entry.IsEnabled(level);
                    }
                }
                catch (Exception)
                {
                    return true;
                }

                return true; // 未登録コンポーネントはデフォルト表示 (ON)
            }
        }

        public bool IsLogEnabled(string nameTag, AppLogLevel level, bool globalEnabled)
        {
            if (!globalEnabled) return false;
            if (string.IsNullOrEmpty(nameTag)) return true;

            lock (_lock)
            {
                try
                {
                    if (_nameLookup.TryGetValue(nameTag, out var entry))
                    {
                        return entry.IsEnabled(level);
                    }

                    foreach (var kvp in _nameLookup)
                    {
                        if (kvp.Key.IndexOf(nameTag, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return kvp.Value.IsEnabled(level);
                        }
                    }
                }
                catch (Exception)
                {
                    return true;
                }

                return true; // 未登録タグはデフォルト表示 (ON)
            }
        }

        public bool IsTagRegistered(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return false;
            lock (_lock)
            {
                return _nameLookup.ContainsKey(tag);
            }
        }

        public static string GetTargetTagKey(UnityEngine.Object target, string tag)
        {
            if (ReferenceEquals(target, null)) return tag;
            try
            {
                return $"{target.GetInstanceID()}:{tag}";
            }
            catch (Exception)
            {
                return tag;
            }
        }
    }
}
