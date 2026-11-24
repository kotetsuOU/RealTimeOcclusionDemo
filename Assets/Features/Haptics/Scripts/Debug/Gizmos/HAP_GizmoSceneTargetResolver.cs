#nullable enable
using System.Reflection;
using UnityEngine;

namespace Features.Haptics.Debug
{
    /// <summary>
    /// Gizmo 描画時に使用するシーン上の AUTD デバイス群およびアクティブ仮想オブジェクトを、
    /// 一定間隔キャッシュとリフレクションキャッシュを用いて低負荷に解決するヘルパークラス。
    /// </summary>
    public static class HAP_GizmoSceneTargetResolver
    {
        private const double CacheRefreshIntervalSeconds = 1.0;

        private static AUTD3Device[]? _cachedDevices;
        private static double _lastDevicesScanTime = -10.0;

        private static HCD_Pipeline? _cachedHcdPipeline;
        private static double _lastHcdScanTime = -10.0;

        private static MonoBehaviour? _cachedVirtualObjectManager;
        private static PropertyInfo? _cachedActiveObjectProp;
        private static MonoBehaviour? _cachedAnimationController;
        private static FieldInfo? _cachedToggleObjectsField;
        private static FieldInfo? _cachedActiveIndexField;
        private static double _lastFallbackScanTime = -10.0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCaches()
        {
            _cachedDevices = null;
            _lastDevicesScanTime = -10.0;
            _cachedHcdPipeline = null;
            _lastHcdScanTime = -10.0;
            _cachedVirtualObjectManager = null;
            _cachedActiveObjectProp = null;
            _cachedAnimationController = null;
            _cachedToggleObjectsField = null;
            _cachedActiveIndexField = null;
            _lastFallbackScanTime = -10.0;
        }

        private static double GetTimestamp()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorApplication.timeSinceStartup;
#else
            return Time.realtimeSinceStartupAsDouble;
#endif
        }

        /// <summary>
        /// シーン内の <see cref="AUTD3Device"/> 一覧をキャッシュ付きで取得します。
        /// </summary>
        public static AUTD3Device[] GetCachedDevices()
        {
            double now = GetTimestamp();
            bool needsRefresh = _cachedDevices == null || now - _lastDevicesScanTime >= CacheRefreshIntervalSeconds;
            if (!needsRefresh)
            {
                for (int i = 0; i < _cachedDevices!.Length; i++)
                {
                    if (_cachedDevices[i] == null)
                    {
                        needsRefresh = true;
                        break;
                    }
                }
            }

            if (needsRefresh)
            {
                _lastDevicesScanTime = now;
                _cachedDevices = Object.FindObjectsByType<AUTD3Device>(FindObjectsSortMode.None);
            }

            return _cachedDevices ?? System.Array.Empty<AUTD3Device>();
        }

        /// <summary>
        /// 指向性グルーピング可視化の対象となるアクティブな仮想オブジェクトを解決します。
        /// </summary>
        public static GameObject? ResolveActiveVirtualObject(HCD_Pipeline? hcdPipeline)
        {
            if (hcdPipeline == null)
            {
                double now = GetTimestamp();
                if (_cachedHcdPipeline == null && now - _lastHcdScanTime >= CacheRefreshIntervalSeconds)
                {
                    _lastHcdScanTime = now;
                    _cachedHcdPipeline = Object.FindFirstObjectByType<HCD_Pipeline>(FindObjectsInactive.Include);
                }
                hcdPipeline = _cachedHcdPipeline;
            }

            if (hcdPipeline != null && hcdPipeline.distanceProcessor != null)
            {
                var dp = hcdPipeline.distanceProcessor;
                if (dp.detectionMode == HCD_DistanceProcessor.DetectionMode.TransformOnly && dp.targetObject != null)
                {
                    return dp.targetObject.gameObject;
                }
                if (dp.targetSkinnedMeshes != null && dp.targetSkinnedMeshes.Length > 0 && dp.targetSkinnedMeshes[0] != null)
                {
                    return dp.targetSkinnedMeshes[0].transform.root != null
                        ? dp.targetSkinnedMeshes[0].transform.root.gameObject
                        : dp.targetSkinnedMeshes[0].gameObject;
                }
                if (dp.targetMeshFilters != null && dp.targetMeshFilters.Length > 0 && dp.targetMeshFilters[0] != null)
                {
                    return dp.targetMeshFilters[0].transform.root != null
                        ? dp.targetMeshFilters[0].transform.root.gameObject
                        : dp.targetMeshFilters[0].gameObject;
                }
            }

            return ResolveFallbackActiveObject();
        }

        private static GameObject? ResolveFallbackActiveObject()
        {
            double now = GetTimestamp();
            if (_cachedVirtualObjectManager == null && _cachedAnimationController == null &&
                now - _lastFallbackScanTime >= CacheRefreshIntervalSeconds)
            {
                _lastFallbackScanTime = now;
                var monoBehaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var mc in monoBehaviours)
                {
                    if (mc == null) continue;
                    var type = mc.GetType();
                    string typeName = type.Name;
                    if (typeName == "PR_VirtualObjectManager" && _cachedVirtualObjectManager == null)
                    {
                        _cachedVirtualObjectManager = mc;
                        _cachedActiveObjectProp = type.GetProperty("ActiveObject");
                    }
                    else if (typeName == "PR_AnimationController" && _cachedAnimationController == null)
                    {
                        _cachedAnimationController = mc;
                        _cachedToggleObjectsField = type.GetField("toggleObjects");
                        _cachedActiveIndexField = type.GetField("currentActiveIndex", BindingFlags.NonPublic | BindingFlags.Instance);
                    }
                }
            }

            if (_cachedVirtualObjectManager != null && _cachedActiveObjectProp != null)
            {
                if (_cachedActiveObjectProp.GetValue(_cachedVirtualObjectManager) is GameObject activeObj)
                {
                    return activeObj;
                }
            }

            if (_cachedAnimationController != null && _cachedToggleObjectsField != null && _cachedActiveIndexField != null)
            {
                var toggleObjects = _cachedToggleObjectsField.GetValue(_cachedAnimationController) as GameObject[];
                var activeIndexObj = _cachedActiveIndexField.GetValue(_cachedAnimationController);
                if (toggleObjects != null && activeIndexObj is int activeIndex &&
                    activeIndex >= 0 && activeIndex < toggleObjects.Length)
                {
                    return toggleObjects[activeIndex];
                }
            }

            return null;
        }
    }
}

