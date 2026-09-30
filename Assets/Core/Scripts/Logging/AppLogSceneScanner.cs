using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Core.Logging
{
    /// <summary>
    /// シーンおよびメモリ上の AppLogger 対応要素（アクティブ・非アクティブ含む実在要素）を検出し、
    /// 既存設定を引き継ぎながら存在しない残骸をクリーンアップして最新グループリストを生成するスキャナー。
    /// </summary>
    public static class AppLogSceneScanner
    {
        private struct EntryState
        {
            public bool info;
            public bool warn;
            public bool err;
        }

        /// <summary>
        /// 現在のグループリストから設定を引き継ぎつつ、シーンおよびメモリ上の実在コンポーネントをスキャンして新リストを返します。
        /// </summary>
        public static List<LogCategoryGroup> Scan(List<LogCategoryGroup> currentGroups, UnityEngine.Object managerInstance = null)
        {
            // 1. 既存のユーザー設定（ON/OFF状態およびフォルダ開閉状態）を退避
            var stateByInstanceTag = new Dictionary<string, EntryState>();
            var stateByNameTypeTag = new Dictionary<string, EntryState>(StringComparer.OrdinalIgnoreCase);
            var stateByLabel = new Dictionary<string, EntryState>(StringComparer.OrdinalIgnoreCase);
            var savedExpandedStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            if (currentGroups != null)
            {
                foreach (var group in currentGroups)
                {
                    if (group == null) continue;
                    if (!string.IsNullOrEmpty(group.categoryName))
                    {
                        savedExpandedStates[group.categoryName] = group.isExpanded;
                    }

                    if (group.entries == null) continue;
                    foreach (var entry in group.entries)
                    {
                        if (entry == null) continue;

                        var s = new EntryState { info = entry.enableInfo, warn = entry.enableWarning, err = entry.enableError };

                        if (!string.IsNullOrEmpty(entry.label))
                        {
                            stateByLabel[entry.label] = s;
                        }

                        if (entry.target != null && !string.IsNullOrEmpty(entry.tag))
                        {
                            try
                            {
                                string idKey = $"{entry.target.GetInstanceID()}:{entry.tag}";
                                stateByInstanceTag[idKey] = s;

                                string nameKey = $"{entry.target.name}:{entry.target.GetType().Name}:{entry.tag}";
                                stateByNameTypeTag[nameKey] = s;
                            }
                            catch (Exception) { }
                        }
                    }
                }
            }

            // 2. 新しいグループリストを作成（現在実在する要素のみで再構成）
            List<LogCategoryGroup> newGroups = new List<LogCategoryGroup>();

            LogCategoryGroup GetOrCreateNewGroup(string catName)
            {
                var g = newGroups.Find(x => string.Equals(x.categoryName, catName, StringComparison.OrdinalIgnoreCase));
                if (g == null)
                {
                    bool isExp = true;
                    if (savedExpandedStates.TryGetValue(catName, out bool wasExp))
                    {
                        isExp = wasExp;
                    }
                    g = new LogCategoryGroup { categoryName = catName, isExpanded = isExp };
                    newGroups.Add(g);
                }
                return g;
            }

            HashSet<UnityEngine.Object> scannedTargets = new HashSet<UnityEngine.Object>();
            HashSet<string> scannedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 3. シーン内の全コンポーネント（非アクティブを含む）を検出
            MonoBehaviour[] allComponents = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var comp in allComponents)
            {
                if (comp == null || comp == managerInstance) continue;

                // シーンに属している実在オブジェクトに限定（Project内のプレハブアセット等を除外）
                if (!comp.gameObject.scene.IsValid()) continue;

                var loggableAttr = comp.GetType().GetCustomAttribute<AppLoggableAttribute>(true);
                bool isLoggableInterface = comp is IAppLoggable;

                // AppLogger 未対応クラスは除外
                if (loggableAttr == null && !isLoggableInterface)
                {
                    continue;
                }

                string typeName = comp.GetType().Name;
                string catName = !string.IsNullOrEmpty(loggableAttr?.CategoryName)
                    ? loggableAttr.CategoryName
                    : (ResolveCategoryName(typeName) ?? "Other Loggables");

                LogCategoryGroup group = GetOrCreateNewGroup(catName);

                if (comp is IAppLoggable loggableComp)
                {
                    int countBefore = group.entries.Count;
                    loggableComp.RegisterLogTriggers(group, scannedLabels);
                    scannedTargets.Add(comp);

                    for (int i = countBefore; i < group.entries.Count; i++)
                    {
                        var e = group.entries[i];
                        if (e != null && !string.IsNullOrEmpty(e.label))
                        {
                            scannedLabels.Add(e.label);
                        }
                    }
                }
                else if (!scannedTargets.Contains(comp))
                {
                    string label = $"[{typeName}] {comp.gameObject.name}";
                    if (!scannedLabels.Contains(label))
                    {
                        group.entries.Add(new LogInstanceEntry
                        {
                            label = label,
                            tag = typeName,
                            target = comp,
                            enableInfo = true,
                            enableWarning = true,
                            enableError = true
                        });
                        scannedLabels.Add(label);
                    }
                    scannedTargets.Add(comp);
                }
            }

            // 4. 有効な ScriptableObject を検出（[AppLoggable] または IAppLoggable）
            UnityEngine.Object[] allScriptableObjects = Resources.FindObjectsOfTypeAll(typeof(ScriptableObject));
            foreach (var obj in allScriptableObjects)
            {
                if (obj is ScriptableObject so && so != null)
                {
                    if (so.hideFlags.HasFlag(HideFlags.HideAndDontSave) || so.hideFlags.HasFlag(HideFlags.DontSave))
                    {
                        continue;
                    }

                    var loggableAttr = so.GetType().GetCustomAttribute<AppLoggableAttribute>(true);
                    bool isLoggableInterface = so is IAppLoggable;

                    if (loggableAttr == null && !isLoggableInterface)
                    {
                        continue;
                    }

                    string typeName = so.GetType().Name;
                    string catName = !string.IsNullOrEmpty(loggableAttr?.CategoryName)
                        ? loggableAttr.CategoryName
                        : (ResolveCategoryName(typeName) ?? "Other Loggables");

                    LogCategoryGroup group = GetOrCreateNewGroup(catName);

                    if (so is IAppLoggable loggableSo)
                    {
                        int countBefore = group.entries.Count;
                        loggableSo.RegisterLogTriggers(group, scannedLabels);
                        scannedTargets.Add(so);

                        for (int i = countBefore; i < group.entries.Count; i++)
                        {
                            var e = group.entries[i];
                            if (e != null && !string.IsNullOrEmpty(e.label))
                            {
                                scannedLabels.Add(e.label);
                            }
                        }
                    }
                    else if (!scannedTargets.Contains(so))
                    {
                        string label = $"[{typeName}] {so.name}";
                        if (!scannedLabels.Contains(label))
                        {
                            group.entries.Add(new LogInstanceEntry
                            {
                                label = label,
                                tag = typeName,
                                target = so,
                                enableInfo = true,
                                enableWarning = true,
                                enableError = true
                            });
                            scannedLabels.Add(label);
                        }
                        scannedTargets.Add(so);
                    }
                }
            }

            // 5. 既存設定の復元（同一ターゲット・タグ、または同一ラベルのエントリの設定を引き継ぐ）
            foreach (var group in newGroups)
            {
                if (group.entries == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry == null) continue;

                    EntryState state = default;
                    bool found = false;

                    if (entry.target != null && !string.IsNullOrEmpty(entry.tag))
                    {
                        try
                        {
                            string idKey = $"{entry.target.GetInstanceID()}:{entry.tag}";
                            if (stateByInstanceTag.TryGetValue(idKey, out state))
                            {
                                found = true;
                            }
                            else
                            {
                                string nameKey = $"{entry.target.name}:{entry.target.GetType().Name}:{entry.tag}";
                                if (stateByNameTypeTag.TryGetValue(nameKey, out state))
                                {
                                    found = true;
                                }
                            }
                        }
                        catch (Exception) { }
                    }

                    if (!found && !string.IsNullOrEmpty(entry.label))
                    {
                        if (stateByLabel.TryGetValue(entry.label, out state))
                        {
                            found = true;
                        }
                    }

                    if (found)
                    {
                        entry.enableInfo = state.info;
                        entry.enableWarning = state.warn;
                        entry.enableError = state.err;
                    }
                    else
                    {
                        entry.enableInfo = true;
                        entry.enableWarning = true;
                        entry.enableError = true;
                    }
                }
            }

            // 6. 空のグループを削除
            newGroups.RemoveAll(g => g.entries == null || g.entries.Count == 0);

            return newGroups;
        }

        public static string ResolveCategoryName(string typeName)
        {
            if (typeName.StartsWith("HCD", StringComparison.OrdinalIgnoreCase)) return "HCD (Haptic Collision)";
            if (typeName.StartsWith("DPC", StringComparison.OrdinalIgnoreCase) || typeName.Contains("Dummy")) return "DPC (Dummy Point Cloud)";
            if (typeName.StartsWith("Rs", StringComparison.OrdinalIgnoreCase) || typeName.Contains("RealSense")) return "RealSense";
            if (typeName.StartsWith("PCD", StringComparison.OrdinalIgnoreCase) || typeName.Contains("Occlusion")) return "PCD (Occlusion)";
            if (typeName.StartsWith("EXP", StringComparison.OrdinalIgnoreCase) || typeName.Contains("Experiment")) return "Experiment";
            if (typeName.StartsWith("HAP", StringComparison.OrdinalIgnoreCase) || typeName.Contains("Haptic")) return "Haptics";
            if (typeName.Contains("Controller") || typeName.Contains("Manager")) return "Core / Utilities";
            return null;
        }
    }
}
