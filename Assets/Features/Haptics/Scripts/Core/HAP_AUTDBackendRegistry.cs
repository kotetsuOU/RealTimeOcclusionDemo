using System;
using Core.Logging;
using Features.Haptics.Debug;

#nullable enable

namespace Features.Haptics.Core
{
    /// <summary>
    /// 利用可能な AUTD3 バックエンド（Legacy / Current）のファクトリを一元管理するレジストリ。
    /// 各バックエンドは [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    /// にて自己登録を行います。
    /// </summary>
    public static class HAP_AUTDBackendRegistry
    {
        private static Func<IHAP_AUTDBackend>? _factory;
        private static string _registeredName = "None";

        /// <summary>
        /// バックエンドファクトリを登録します。
        /// </summary>
        public static void RegisterBackend(string name, Func<IHAP_AUTDBackend> factory)
        {
            _registeredName = name;
            _factory = factory;
            AppLogger.Log(null, HAP_LogTriggers.TagLinkService, $"Registered AUTD backend: {name}");
        }

        /// <summary>
        /// 登録されているバックエンドのインスタンスを生成します。
        /// 未登録の場合は null を返します。
        /// </summary>
        public static IHAP_AUTDBackend? CreateBackend()
        {
            if (_factory == null)
            {
                AppLogger.LogWarning(null, HAP_LogTriggers.TagLinkService, "No AUTD backend is currently registered.");
                return null;
            }

            return _factory();
        }

        /// <summary>登録されているバックエンドが存在するかどうか。</summary>
        public static bool HasBackend => _factory != null;

        /// <summary>登録されているバックエンド名。</summary>
        public static string RegisteredName => _registeredName;
    }
}
