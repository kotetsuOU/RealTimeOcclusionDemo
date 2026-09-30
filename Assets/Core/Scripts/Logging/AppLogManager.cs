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

        [Header("Global Control")]
        [Tooltip("全体的なログ出力の有効/無効トグル")]
        public bool globalEnableLogging = true;

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

            BuildLookup();
        }

        private void OnEnable()
        {
            AppLogger.RegisterMainThread();
            if (Instance == null) Instance = this;
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
            return _lookupEngine.IsLogEnabled(targetObject, AppLogLevel.Info, subTag, globalEnableLogging);
        }

        public bool IsLogEnabled(UnityEngine.Object targetObject, AppLogLevel level, string subTag = null)
        {
            return _lookupEngine.IsLogEnabled(targetObject, level, subTag, globalEnableLogging);
        }

        public bool IsTagRegistered(string tag)
        {
            return _lookupEngine.IsTagRegistered(tag);
        }

        public bool IsLogEnabled(string nameTag)
        {
            return _lookupEngine.IsLogEnabled(nameTag, AppLogLevel.Info, globalEnableLogging);
        }

        public bool IsLogEnabled(string nameTag, AppLogLevel level)
        {
            return _lookupEngine.IsLogEnabled(nameTag, level, globalEnableLogging);
        }

        /// <summary>
        /// シーン内（非アクティブを含む実在要素）および有効な AppLogger 対応要素をスキャンし、最新リストで更新します。
        /// </summary>
        public void ScanSceneComponents()
        {
            categoryGroups = AppLogSceneScanner.Scan(categoryGroups, this);
            BuildLookup();
        }

        public void SetAllEnabled(bool enable, AppLogLevel? targetLevel = null)
        {
            AppLogGroupModifier.SetAllEnabled(categoryGroups, enable, targetLevel);
            BuildLookup();
        }

        public void SetGroupEnabled(string categoryName, bool enable, AppLogLevel? targetLevel = null)
        {
            AppLogGroupModifier.SetGroupEnabled(categoryGroups, categoryName, enable, targetLevel);
            BuildLookup();
        }
    }
}
