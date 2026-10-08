using System.Collections.Generic;
using UnityEngine;
using Core.Logging;

namespace Features.Haptics.Debug
{
    /// <summary>
    /// Haptics (HAP) モジュールの AppLogManager 連動ログトリガー定義および登録を担当するヘルパーコンポーネント。
    /// HAP_AUTDHapticsController コア本体および HAP_BaseObjectHapticsController 各クラスから
    /// AppLogManager 登録処理や定期診断ログ処理を分離し、神クラス化を防止します。
    /// </summary>
    [AppLoggable("Haptics")]
    [DisallowMultipleComponent]
    public class HAP_LogTriggers : MonoBehaviour, IAppLoggable
    {
        public const string TagController = "HAP_Controller";
        public const string TagLinkService = "HAP_LinkService";
        public const string TagModulationService = "HAP_ModulationService";
        public const string TagTransformLoader = "HAP_TransformLoader";
        public const string TagCalibration = "HAP_Calibration";
        public const string TagPerformanceProfiler = "HAP_PerformanceProfiler";
        public const string TagSDKSetup = "HAP_SDKSetup";
        public const string TagObjectHaptics = "HAP_ObjectHaptics";
        public const string TagObjectPeriodic = "HAP_ObjectHaptics_Periodic";

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            var controller = GetComponent<HAP_AUTDHapticsController>() ?? FindFirstObjectByType<HAP_AUTDHapticsController>();
            Object targetObj = controller != null ? (Object)controller : this;

            AddSubTriggerIfNotExists(group, targetObj, "[HAP_Controller] Main Controller & Dispatcher", TagController, existingLabels);
            AddSubTriggerIfNotExists(group, targetObj, "[HAP_LinkService] AUTD3 Link Connection", TagLinkService, existingLabels);
            AddSubTriggerIfNotExists(group, targetObj, "[HAP_ModulationService] Modulation & Silencer Control", TagModulationService, existingLabels);
            AddSubTriggerIfNotExists(group, targetObj, "[HAP_TransformLoader] Transform & Snapshot Loader", TagTransformLoader, existingLabels);
            AddSubTriggerIfNotExists(group, targetObj, "[HAP_Calibration] Device Alignment Calibration", TagCalibration, existingLabels);
            AddSubTriggerIfNotExists(group, targetObj, "[HAP_PerformanceProfiler] Performance Profiler Log", TagPerformanceProfiler, existingLabels);
            AddSubTriggerIfNotExists(group, targetObj, "[HAP_SDKSetup] AUTD3 SDK Symbol & Build Setup", TagSDKSetup, existingLabels);

            // シーン内のオブジェクト触覚コントローラー（FoxBody, FoxFoot 等）を検出し、サブトリガーとして一元登録
            var objectControllers = FindObjectsByType<HAP_BaseObjectHapticsController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var objCtrl in objectControllers)
            {
                if (objCtrl == null) continue;
                string typeName = objCtrl.GetType().Name;
                AddSubTriggerIfNotExists(group, objCtrl, $"[{typeName}] 診断レポート (手動/イベント)", TagObjectHaptics, existingLabels, enabled: true);
                AddSubTriggerIfNotExists(group, objCtrl, $"[{typeName}] 定期自動ログ (5秒間隔)", TagObjectPeriodic, existingLabels, enabled: false);
            }
        }

        private void Update()
        {
            // 5秒間隔 (約300フレーム) で、AppLogManager 上で有効化されているオブジェクト触覚コントローラーの定期診断ログを出力
            if (Time.frameCount % 300 == 0)
            {
                var objectControllers = FindObjectsByType<HAP_BaseObjectHapticsController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var objCtrl in objectControllers)
                {
                    if (objCtrl != null && objCtrl.enabled && objCtrl.gameObject.activeInHierarchy)
                    {
                        if (AppLogger.IsEnabled(objCtrl, TagObjectPeriodic))
                        {
                            objCtrl.LogDiagnostics();
                        }
                    }
                }
            }
        }

        private void AddSubTriggerIfNotExists(LogCategoryGroup group, Object targetObj, string label, string tag, HashSet<string> existingLabels, bool enabled = true)
        {
            if (!existingLabels.Contains(label))
            {
                group.entries.Add(new LogInstanceEntry
                {
                    label = label,
                    tag = tag,
                    target = targetObj,
                    enabled = enabled
                });
                existingLabels.Add(label);
            }
        }
    }
}
