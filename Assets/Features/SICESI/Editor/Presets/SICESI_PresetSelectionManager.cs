using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// SICE SI 配置プリセット一覧における各プリセットの選択状態 (ON/OFF) および
    /// フォルダー階層の開閉 (Foldout) 状態を管理・永続化するマネージャークラス。
    /// </summary>
    public static class SICESI_PresetSelectionManager
    {
        private const string PrefKeyDisabledPresets = "SICESI_DisabledPresetPaths";
        private const string PrefKeyFoldedFolders = "SICESI_FoldedFolders";

        private static HashSet<string> _disabledPaths = null;
        private static HashSet<string> _foldedFolders = null;

        private static void EnsureLoaded()
        {
            if (_disabledPaths == null)
            {
                _disabledPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string raw = EditorPrefs.GetString(PrefKeyDisabledPresets, "");
                if (!string.IsNullOrEmpty(raw))
                {
                    string[] items = raw.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var item in items)
                    {
                        _disabledPaths.Add(item.Trim());
                    }
                }
            }

            if (_foldedFolders == null)
            {
                _foldedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string raw = EditorPrefs.GetString(PrefKeyFoldedFolders, "");
                if (!string.IsNullOrEmpty(raw))
                {
                    string[] items = raw.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var item in items)
                    {
                        _foldedFolders.Add(item.Trim());
                    }
                }
            }
        }

        private static void SaveDisabledPaths()
        {
            if (_disabledPaths == null) return;
            string joined = string.Join(";", _disabledPaths);
            EditorPrefs.SetString(PrefKeyDisabledPresets, joined);
        }

        private static void SaveFoldedFolders()
        {
            if (_foldedFolders == null) return;
            string joined = string.Join(";", _foldedFolders);
            EditorPrefs.SetString(PrefKeyFoldedFolders, joined);
        }

        /// <summary>
        /// 指定されたプリセットが選択中 (有効) かどうかを取得します。デフォルトは true。
        /// </summary>
        public static bool IsSelected(string jsonPath)
        {
            if (string.IsNullOrEmpty(jsonPath)) return false;
            EnsureLoaded();
            return !_disabledPaths.Contains(jsonPath);
        }

        /// <summary>
        /// 指定されたプリセットの選択状態を設定します。
        /// </summary>
        public static void SetSelected(string jsonPath, bool isSelected)
        {
            if (string.IsNullOrEmpty(jsonPath)) return;
            EnsureLoaded();
            if (isSelected)
            {
                if (_disabledPaths.Remove(jsonPath)) SaveDisabledPaths();
            }
            else
            {
                if (_disabledPaths.Add(jsonPath)) SaveDisabledPaths();
            }
        }

        /// <summary>
        /// フォルダーが開いている (展開中) かどうかを取得します。デフォルトは true。
        /// </summary>
        public static bool IsFolderExpanded(string folderKey)
        {
            if (string.IsNullOrEmpty(folderKey)) return true;
            EnsureLoaded();
            return !_foldedFolders.Contains(folderKey);
        }

        /// <summary>
        /// フォルダーの開閉状態を設定します。
        /// </summary>
        public static void SetFolderExpanded(string folderKey, bool isExpanded)
        {
            if (string.IsNullOrEmpty(folderKey)) return;
            EnsureLoaded();
            if (isExpanded)
            {
                if (_foldedFolders.Remove(folderKey)) SaveFoldedFolders();
            }
            else
            {
                if (_foldedFolders.Add(folderKey)) SaveFoldedFolders();
            }
        }

        /// <summary>
        /// 指定リスト内の全プリセットを選択状態にします。
        /// </summary>
        public static void SelectAll(IEnumerable<SICESI_PresetPoseManager.PresetEntry> entries)
        {
            if (entries == null) return;
            EnsureLoaded();
            bool changed = false;
            foreach (var e in entries)
            {
                if (e != null && !string.IsNullOrEmpty(e.jsonPath))
                {
                    if (_disabledPaths.Remove(e.jsonPath)) changed = true;
                }
            }
            if (changed) SaveDisabledPaths();
        }

        /// <summary>
        /// 指定リスト内の全プリセットを選択解除にします。
        /// </summary>
        public static void DeselectAll(IEnumerable<SICESI_PresetPoseManager.PresetEntry> entries)
        {
            if (entries == null) return;
            EnsureLoaded();
            bool changed = false;
            foreach (var e in entries)
            {
                if (e != null && !string.IsNullOrEmpty(e.jsonPath))
                {
                    if (_disabledPaths.Add(e.jsonPath)) changed = true;
                }
            }
            if (changed) SaveDisabledPaths();
        }

        /// <summary>
        /// 選択されているプリセットのみを抽出して取得します。
        /// </summary>
        public static List<SICESI_PresetPoseManager.PresetEntry> GetSelectedEntries(IEnumerable<SICESI_PresetPoseManager.PresetEntry> entries)
        {
            var result = new List<SICESI_PresetPoseManager.PresetEntry>();
            if (entries == null) return result;
            foreach (var e in entries)
            {
                if (e != null && IsSelected(e.jsonPath))
                {
                    result.Add(e);
                }
            }
            return result;
        }
    }
}
