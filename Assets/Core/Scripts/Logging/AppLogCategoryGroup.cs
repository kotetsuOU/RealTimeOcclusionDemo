using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.Logging
{
    /// <summary>
    /// 各コンポーネントまたはサブトリガー単位のログ設定（Info, Warning, Error）を保持するエントリー。
    /// </summary>
    [Serializable]
    public class LogInstanceEntry
    {
        public string label;
        public string tag;
        public UnityEngine.Object target;

        public bool enableInfo = true;
        public bool enableWarning = true;
        public bool enableError = true;

        public bool IsEnabled(AppLogLevel level)
        {
            switch (level)
            {
                case AppLogLevel.Info: return enableInfo;
                case AppLogLevel.Warning: return enableWarning;
                case AppLogLevel.Error: return enableError;
                default: return true;
            }
        }

        public void SetAll(bool enable)
        {
            enableInfo = enable;
            enableWarning = enable;
            enableError = enable;
        }

        public bool enabled
        {
            get => enableInfo && enableWarning && enableError;
            set => SetAll(value);
        }
    }

    /// <summary>
    /// モジュール別（HCD, PCD, RealSense 等）にエントリーをグループ化するモデル。
    /// </summary>
    [Serializable]
    public class LogCategoryGroup
    {
        public string categoryName;
        public bool isExpanded = true;
        public List<LogInstanceEntry> entries = new List<LogInstanceEntry>();
    }
}
