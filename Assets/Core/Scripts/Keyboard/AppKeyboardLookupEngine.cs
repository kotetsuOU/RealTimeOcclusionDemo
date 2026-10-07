using System.Collections.Generic;
using UnityEngine;

namespace Core.Keyboard
{
    /// <summary>
    /// キーボードバインドの高速ルックアップおよび重複（衝突）検出エンジン (Pure C#)。
    /// 毎フレームの Update 呼び出しにおいて GC アロケーションを発生させない O(1) 検索を実現します。
    /// </summary>
    public class AppKeyboardLookupEngine
    {
        private struct RuntimeBindingInfo
        {
            public AppKeyBindingEntry Entry;
            public bool IsEffective; // グローバル、グループ、個別エントリがすべて有効か
        }

        private readonly Dictionary<AppKeyAction, RuntimeBindingInfo> _actionLookup = new Dictionary<AppKeyAction, RuntimeBindingInfo>(64);
        private readonly List<AppKeyConflictInfo> _conflictCache = new List<AppKeyConflictInfo>();

        /// <summary>
        /// インスペクター等で定義されたグループリストから高速検索用キャッシュを再構築します。
        /// </summary>
        public void BuildLookup(List<AppKeyBindingGroup> groups, bool globalEnabled)
        {
            _actionLookup.Clear();
            _conflictCache.Clear();

            if (groups == null) return;

            // 1. ルックアップ辞書の構築
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                if (group == null || group.entries == null) continue;

                bool groupActive = globalEnabled && group.isCategoryEnabled;

                for (int e = 0; e < group.entries.Count; e++)
                {
                    var entry = group.entries[e];
                    if (entry == null || entry.action == AppKeyAction.None) continue;

                    bool isEffective = groupActive && entry.isEnabled;
                    _actionLookup[entry.action] = new RuntimeBindingInfo
                    {
                        Entry = entry,
                        IsEffective = isEffective
                    };
                }
            }

            // 2. 衝突（重複）の検出
            DetectConflicts(groups);
        }

        /// <summary>
        /// 現在の設定におけるキー重複（衝突）を検出してキャッシュします。
        /// </summary>
        public void DetectConflicts(List<AppKeyBindingGroup> groups)
        {
            _conflictCache.Clear();
            if (groups == null) return;

            // (KeyCode -> List<(category, action)>)
            var keyMap = new Dictionary<KeyCode, List<(string category, AppKeyAction action)>>();

            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                if (group == null || group.entries == null) continue;

                for (int e = 0; e < group.entries.Count; e++)
                {
                    var entry = group.entries[e];
                    if (entry == null || !entry.isEnabled || entry.action == AppKeyAction.None) continue;

                    RegisterKey(keyMap, entry.primaryKey, group.categoryName, entry.action);
                    RegisterKey(keyMap, entry.secondaryKey, group.categoryName, entry.action);
                }
            }

            foreach (var kvp in keyMap)
            {
                var list = kvp.Value;
                if (list.Count > 1)
                {
                    for (int i = 0; i < list.Count - 1; i++)
                    {
                        for (int j = i + 1; j < list.Count; j++)
                        {
                            // 同一アクションの Primary と Secondary が同キーの場合は衝突とみなさない
                            if (list[i].action == list[j].action) continue;

                            _conflictCache.Add(new AppKeyConflictInfo(
                                kvp.Key,
                                list[i].action,
                                list[i].category,
                                list[j].action,
                                list[j].category
                            ));
                        }
                    }
                }
            }
        }

        private static void RegisterKey(Dictionary<KeyCode, List<(string category, AppKeyAction action)>> map, KeyCode key, string category, AppKeyAction action)
        {
            if (key == KeyCode.None) return;

            if (!map.TryGetValue(key, out var list))
            {
                list = new List<(string category, AppKeyAction action)>();
                map[key] = list;
            }
            list.Add((category, action));
        }

        /// <summary>
        /// キャッシュされた重複（衝突）リストを取得します。
        /// </summary>
        public IReadOnlyList<AppKeyConflictInfo> GetConflicts()
        {
            return _conflictCache;
        }

        /// <summary>
        /// 対象アクションが今フレームで押された瞬間 (GetKeyDown) か判定します。
        /// </summary>
        public bool IsTriggeredDown(AppKeyAction action)
        {
            if (!_actionLookup.TryGetValue(action, out var info) || !info.IsEffective)
            {
                return false;
            }

            var entry = info.Entry;
            if (entry.primaryKey != KeyCode.None && Input.GetKeyDown(entry.primaryKey))
            {
                return true;
            }

            if (entry.secondaryKey != KeyCode.None && Input.GetKeyDown(entry.secondaryKey))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 対象アクションが現在押されている状態 (GetKey) か判定します。
        /// </summary>
        public bool IsTriggered(AppKeyAction action)
        {
            if (!_actionLookup.TryGetValue(action, out var info) || !info.IsEffective)
            {
                return false;
            }

            var entry = info.Entry;
            if (entry.primaryKey != KeyCode.None && Input.GetKey(entry.primaryKey))
            {
                return true;
            }

            if (entry.secondaryKey != KeyCode.None && Input.GetKey(entry.secondaryKey))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 対象アクションが離された瞬間 (GetKeyUp) か判定します。
        /// </summary>
        public bool IsTriggeredUp(AppKeyAction action)
        {
            if (!_actionLookup.TryGetValue(action, out var info) || !info.IsEffective)
            {
                return false;
            }

            var entry = info.Entry;
            if (entry.primaryKey != KeyCode.None && Input.GetKeyUp(entry.primaryKey))
            {
                return true;
            }

            if (entry.secondaryKey != KeyCode.None && Input.GetKeyUp(entry.secondaryKey))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 対象アクションのエントリー定義を取得します。
        /// </summary>
        public AppKeyBindingEntry GetBindingEntry(AppKeyAction action)
        {
            return _actionLookup.TryGetValue(action, out var info) ? info.Entry : null;
        }

        /// <summary>
        /// 対象アクションが有効かどうか判定します。
        /// </summary>
        public bool IsActionEnabled(AppKeyAction action)
        {
            return _actionLookup.TryGetValue(action, out var info) && info.IsEffective;
        }
    }
}
