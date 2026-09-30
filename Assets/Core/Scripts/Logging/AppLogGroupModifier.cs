using System.Collections.Generic;

namespace Core.Logging
{
    /// <summary>
    /// LogCategoryGroup リストに対する一括操作（全有効化・無効化、グループ別有効化など）を提供するユーティリティクラス。
    /// </summary>
    public static class AppLogGroupModifier
    {
        public static void SetAllEnabled(List<LogCategoryGroup> categoryGroups, bool enable, AppLogLevel? targetLevel = null)
        {
            if (categoryGroups == null) return;
            foreach (var group in categoryGroups)
            {
                if (group?.entries == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry != null) SetEntryEnabled(entry, enable, targetLevel);
                }
            }
        }

        public static void SetGroupEnabled(List<LogCategoryGroup> categoryGroups, string categoryName, bool enable, AppLogLevel? targetLevel = null)
        {
            var group = categoryGroups?.Find(g => string.Equals(g.categoryName, categoryName, System.StringComparison.OrdinalIgnoreCase));
            if (group?.entries != null)
            {
                foreach (var entry in group.entries)
                {
                    if (entry != null) SetEntryEnabled(entry, enable, targetLevel);
                }
            }
        }

        public static void SetEntryEnabled(LogInstanceEntry entry, bool enable, AppLogLevel? targetLevel)
        {
            if (targetLevel.HasValue)
            {
                switch (targetLevel.Value)
                {
                    case AppLogLevel.Info: entry.enableInfo = enable; break;
                    case AppLogLevel.Warning: entry.enableWarning = enable; break;
                    case AppLogLevel.Error: entry.enableError = enable; break;
                }
            }
            else
            {
                entry.SetAll(enable);
            }
        }
    }
}
