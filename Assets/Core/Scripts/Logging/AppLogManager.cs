using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.Logging
{
    /// <summary>
    /// シーン内の各コンポーネントインスタンスおよび個別ログトリガーのON/OFFを
    /// モジュール別（HCD, RealSense, PCD, Experiment等）のグループ階層で一元集中管理する MonoBehaviour マネージャー。
    /// 高速検索は AppLogLookupEngine、スキャン同期は AppLogSceneScanner、一括操作は AppLogGroupModifier に責務委譲します。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class AppLogManager : MonoBehaviour
    {
        public static AppLogManager Instance { get; private set; }

        [Header("Log Profiles")]
        [Tooltip("保存されたログ設定プロファイル一覧（通常用、バグ調査用など）")]
        public List<LogProfile> profiles = new List<LogProfile>();

        [Tooltip("現在アクティブなプロファイルのインデックス")]
        public int activeProfileIndex = 0;

        [Header("Category Groups")]
        [Tooltip("モジュール機能ごとにグループ化されたコンポーネントターゲット")]
        public List<LogCategoryGroup> categoryGroups = new List<LogCategoryGroup>();

        private readonly AppLogLookupEngine _lookupEngine = new AppLogLookupEngine();

        private void Awake()
        {
            AppLogger.RegisterMainThread();
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

            EnsureDefaultProfiles();
            if (profiles.Count > 0 && activeProfileIndex >= 0 && activeProfileIndex < profiles.Count)
            {
                AppLogProfileService.ApplyStates(categoryGroups, profiles[activeProfileIndex]);
            }
            BuildLookup();
        }

        private void OnEnable()
        {
            AppLogger.RegisterMainThread();
            if (Instance == null) Instance = this;
            EnsureDefaultProfiles();
            BuildLookup();
        }

        private void OnValidate()
        {
            BuildLookup();
        }

        public void BuildLookup()
        {
            _lookupEngine.BuildLookup(categoryGroups);
        }

        public bool IsLogEnabled(UnityEngine.Object targetObject, string subTag = null)
        {
            return _lookupEngine.IsLogEnabled(targetObject, AppLogLevel.Info, subTag);
        }

        public bool IsLogEnabled(UnityEngine.Object targetObject, AppLogLevel level, string subTag = null)
        {
            return _lookupEngine.IsLogEnabled(targetObject, level, subTag);
        }

        public bool IsTagRegistered(string tag)
        {
            return _lookupEngine.IsTagRegistered(tag);
        }

        public bool IsLogEnabled(string nameTag)
        {
            return _lookupEngine.IsLogEnabled(nameTag, AppLogLevel.Info);
        }

        public bool IsLogEnabled(string nameTag, AppLogLevel level)
        {
            return _lookupEngine.IsLogEnabled(nameTag, level);
        }

        /// <summary>
        /// シーン内（非アクティブを含む実在要素）および有効な AppLogger 対応要素をスキャンし、最新リストで更新します。
        /// </summary>
        public void ScanSceneComponents()
        {
            categoryGroups = AppLogSceneScanner.Scan(categoryGroups, this);
            BuildLookup();
        }

        public void SetAllLevelEnabled(AppLogLevel level, bool enable)
        {
            AppLogGroupModifier.SetAllLevelEnabled(categoryGroups, level, enable);
            BuildLookup();
        }

        public void SetGroupEnabled(string categoryName, bool enable, AppLogLevel? targetLevel = null)
        {
            AppLogGroupModifier.SetGroupEnabled(categoryGroups, categoryName, enable, targetLevel);
            BuildLookup();
        }

        #region Profile Operations

        public void EnsureDefaultProfiles()
        {
            if (profiles == null) profiles = new List<LogProfile>();
            if (profiles.Count == 0)
            {
                var defaultProfile = new LogProfile { profileName = "通常用" };
                AppLogProfileService.CaptureStates(categoryGroups, defaultProfile);
                profiles.Add(defaultProfile);
                activeProfileIndex = 0;
            }
        }

        public void SwitchProfile(int newIndex)
        {
            EnsureDefaultProfiles();
            if (newIndex < 0 || newIndex >= profiles.Count) return;

            // 切り替え前に現在プロファイルを最新状態に保存
            SaveCurrentProfile();

            activeProfileIndex = newIndex;
            AppLogProfileService.ApplyStates(categoryGroups, profiles[activeProfileIndex]);
            BuildLookup();
        }

        public void SaveCurrentProfile()
        {
            EnsureDefaultProfiles();
            if (activeProfileIndex >= 0 && activeProfileIndex < profiles.Count)
            {
                AppLogProfileService.CaptureStates(categoryGroups, profiles[activeProfileIndex]);
            }
        }

        public void AddNewProfile(string profileName)
        {
            EnsureDefaultProfiles();
            SaveCurrentProfile();

            var newProfile = new LogProfile { profileName = profileName };
            AppLogProfileService.CaptureStates(categoryGroups, newProfile);
            profiles.Add(newProfile);
            activeProfileIndex = profiles.Count - 1;
        }

        public void DuplicateProfile(int index)
        {
            EnsureDefaultProfiles();
            if (index < 0 || index >= profiles.Count) return;
            SaveCurrentProfile();

            var source = profiles[index];
            var duplicated = new LogProfile
            {
                profileName = $"{source.profileName} (コピー)"
            };
            AppLogProfileService.CaptureStates(categoryGroups, duplicated);
            profiles.Add(duplicated);
            activeProfileIndex = profiles.Count - 1;
        }

        public void RemoveProfile(int index)
        {
            if (profiles == null || profiles.Count <= 1) return;
            if (index < 0 || index >= profiles.Count) return;

            profiles.RemoveAt(index);
            if (activeProfileIndex >= profiles.Count)
            {
                activeProfileIndex = profiles.Count - 1;
            }
            AppLogProfileService.ApplyStates(categoryGroups, profiles[activeProfileIndex]);
            BuildLookup();
        }

        #endregion
    }
}
