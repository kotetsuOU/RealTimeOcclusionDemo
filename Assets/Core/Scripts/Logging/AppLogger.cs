using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Core.Logging
{
    /// <summary>
    /// アプリケーションログの重要度レベル
    /// </summary>
    public enum AppLogLevel
    {
        Info = 0,     // 通常ログ (AppLogger.Log)
        Warning = 1,  // 警告ログ (AppLogger.LogWarning)
        Error = 2     // エラーログ (AppLogger.LogError)
    }

    /// <summary>
    /// AppLoggerログ管理対象コンポーネントであることを明示する属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public class AppLoggableAttribute : Attribute
    {
        public string CategoryName { get; }
        public AppLoggableAttribute(string categoryName = null)
        {
            CategoryName = categoryName;
        }
    }

    /// <summary>
    /// コンポーネントが自前で複数のサブログトリガーをAppLogManagerへ登録するためのインターフェース
    /// </summary>
    public interface IAppLoggable
    {
        void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels);
    }

    /// <summary>
    /// バックグラウンドスレッド等から Unity API を介さずにログプレフィックス（GameObject名等）を提供するためのインターフェース
    /// </summary>
    public interface ILogContextProvider
    {
        string LogPrefix { get; }
    }

    /// <summary>
    /// アプリケーション全体のモジュール別統一ログ制御API。
    /// 対象コンポーネントの Inspector を一切汚さずに、AppLogManager から一括＆個別制御が可能です。
    /// バックグラウンドスレッド（マルチスレッド）からのログ出力にもスレッドセーフに対応しています。
    /// </summary>
    public static class AppLogger
    {
        private static int s_mainThreadId = -1;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitMainThreadEditor()
        {
            RegisterMainThread();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitMainThreadRuntime()
        {
            RegisterMainThread();
        }

        public static void RegisterMainThread()
        {
            s_mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        public static bool IsMainThread
        {
            get
            {
                if (s_mainThreadId == -1)
                {
                    s_mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                }
                return System.Threading.Thread.CurrentThread.ManagedThreadId == s_mainThreadId;
            }
        }

        public static bool IsEnabled(Object context, string subTag = null)
        {
            return IsEnabled(context, AppLogLevel.Info, subTag);
        }

        private static AppLogManager GetAppLogManager()
        {
            if (AppLogManager.Instance != null) return AppLogManager.Instance;
            if (IsMainThread)
            {
                var mgr = Object.FindFirstObjectByType<AppLogManager>();
                if (mgr != null) return mgr;
            }
            return null;
        }

        public static bool IsEnabled(Object context, AppLogLevel level, string subTag = null)
        {
            var mgr = GetAppLogManager();
            if (mgr != null)
            {
                return mgr.IsLogEnabled(context, level, subTag);
            }
            return true;
        }

        public static bool IsEnabled(string nameTag, AppLogLevel level = AppLogLevel.Info)
        {
            var mgr = GetAppLogManager();
            if (mgr != null)
            {
                return mgr.IsLogEnabled(nameTag, level);
            }
            return true;
        }

        public static void Log(Object context, string message)
        {
            if (!IsEnabled(context, AppLogLevel.Info)) return;
            DoDebugLog(AppLogLevel.Info, GetContextPrefix(context), message, context);
        }

        public static void Log(Object context, string arg2, string arg3)
        {
            ResolveMessageAndSubTag(arg2, arg3, out string message, out string subTag);
            if (!IsEnabled(context, AppLogLevel.Info, subTag)) return;
            string prefix = GetContextPrefix(context);
            if (!string.IsNullOrEmpty(subTag)) prefix = $"{prefix} > {subTag}";
            DoDebugLog(AppLogLevel.Info, prefix, message, context);
        }

        public static void Log(string nameTag, string message, Object context = null)
        {
            if (!IsEnabled(nameTag, AppLogLevel.Info)) return;
            DoDebugLog(AppLogLevel.Info, nameTag, message, context);
        }

        public static void LogWarning(Object context, string message)
        {
            if (!IsEnabled(context, AppLogLevel.Warning)) return;
            DoDebugLog(AppLogLevel.Warning, GetContextPrefix(context), message, context);
        }

        public static void LogWarning(Object context, string arg2, string arg3)
        {
            ResolveMessageAndSubTag(arg2, arg3, out string message, out string subTag);
            if (!IsEnabled(context, AppLogLevel.Warning, subTag)) return;
            string prefix = GetContextPrefix(context);
            if (!string.IsNullOrEmpty(subTag)) prefix = $"{prefix} > {subTag}";
            DoDebugLog(AppLogLevel.Warning, prefix, message, context);
        }

        public static void LogWarning(string nameTag, string message, Object context = null)
        {
            if (!IsEnabled(nameTag, AppLogLevel.Warning)) return;
            DoDebugLog(AppLogLevel.Warning, nameTag, message, context);
        }

        public static void LogError(Object context, string message)
        {
            if (!IsEnabled(context, AppLogLevel.Error)) return;
            DoDebugLog(AppLogLevel.Error, GetContextPrefix(context), message, context);
        }

        public static void LogError(Object context, string arg2, string arg3)
        {
            ResolveMessageAndSubTag(arg2, arg3, out string message, out string subTag);
            if (!IsEnabled(context, AppLogLevel.Error, subTag)) return;
            string prefix = GetContextPrefix(context);
            if (!string.IsNullOrEmpty(subTag)) prefix = $"{prefix} > {subTag}";
            DoDebugLog(AppLogLevel.Error, prefix, message, context);
        }

        private static void ResolveMessageAndSubTag(string arg2, string arg3, out string message, out string subTag)
        {
            if (AppLogManager.Instance != null && AppLogManager.Instance.IsTagRegistered(arg3))
            {
                message = arg2;
                subTag = arg3;
            }
            else if (!string.IsNullOrEmpty(arg3) && (arg3.StartsWith("SRD_") || arg3.StartsWith("PCD_") || arg3.StartsWith("HCD_") || arg3.StartsWith("URP_") || arg3.StartsWith("EXP_") || arg3.StartsWith("Rs")))
            {
                message = arg2;
                subTag = arg3;
            }
            else
            {
                subTag = arg2;
                message = arg3;
            }
        }

        public static void LogError(string nameTag, string message, Object context = null)
        {
            if (!IsEnabled(nameTag, AppLogLevel.Error)) return;
            DoDebugLog(AppLogLevel.Error, nameTag, message, context);
        }

        private static void DoDebugLog(AppLogLevel level, string prefix, string message, Object context)
        {
            string formattedMsg = !string.IsNullOrEmpty(prefix) ? $"[{prefix}] {message}" : message;
            Object targetContext = (IsMainThread && !ReferenceEquals(context, null)) ? context : null;

            switch (level)
            {
                case AppLogLevel.Info:
                    UnityEngine.Debug.Log(formattedMsg, targetContext);
                    break;
                case AppLogLevel.Warning:
                    UnityEngine.Debug.LogWarning(formattedMsg, targetContext);
                    break;
                case AppLogLevel.Error:
                    UnityEngine.Debug.LogError(formattedMsg, targetContext);
                    break;
            }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> s_contextPrefixCache
            = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();

        /// <summary>
        /// コンポーネントや GameObject のプレフィックス表示名（例: "RsDevice: RealSense Camera"）をキャッシュに事前登録します。
        /// バックグラウンドスレッドからのログ出力時にも GameObject 名を解決できるようにします。
        /// </summary>
        public static void RegisterContext(Object context)
        {
            if (ReferenceEquals(context, null)) return;
            if (!IsMainThread) return;

            try
            {
                int key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(context);
                string prefix = ResolvePrefixFromUnityObject(context);
                if (!string.IsNullOrEmpty(prefix))
                {
                    s_contextPrefixCache[key] = prefix;
                }
            }
            catch (Exception)
            {
                // オブジェクトアクセス失敗時は無視
            }
        }

        private static string ResolvePrefixFromUnityObject(Object context)
        {
            if (context is Component comp && comp != null)
            {
                return $"{comp.GetType().Name}: {comp.gameObject.name}";
            }
            if (context is GameObject go && go != null)
            {
                return go.name;
            }
            return context.GetType().Name;
        }

        private static string GetContextPrefix(Object context)
        {
            if (ReferenceEquals(context, null)) return "Log";

            // 1. ILogContextProvider 実装オブジェクトであれば、スレッド問わず直接スレッドセーフにプレフィックスを取得
            if (context is ILogContextProvider provider)
            {
                try
                {
                    string p = provider.LogPrefix;
                    if (!string.IsNullOrEmpty(p)) return p;
                }
                catch (Exception) { }
            }

            int key = 0;
            try
            {
                // RuntimeHelpers.GetHashCode は Unity API を介さないため、ワーカースレッドでも例外なく安全に動作
                key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(context);
            }
            catch (Exception) { }

            try
            {
                if (IsMainThread)
                {
                    string prefix = ResolvePrefixFromUnityObject(context);
                    if (key != 0 && !string.IsNullOrEmpty(prefix))
                    {
                        s_contextPrefixCache[key] = prefix;
                    }
                    return prefix;
                }
            }
            catch (Exception)
            {
                // Unity オブジェクトアクセス失敗時のフォールバック
            }

            // 2. バックグラウンドスレッド時: キャッシュから GameObject 名を含むプレフィックスを取得
            if (key != 0 && s_contextPrefixCache.TryGetValue(key, out string cachedPrefix))
            {
                return cachedPrefix;
            }

            try
            {
                return context.GetType().Name;
            }
            catch (Exception)
            {
                return "Log";
            }
        }
    }
}

