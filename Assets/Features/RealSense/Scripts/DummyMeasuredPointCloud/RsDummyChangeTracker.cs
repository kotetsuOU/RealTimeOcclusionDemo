using System.Collections.Generic;
using UnityEngine;

namespace RealSense.DummyPointCloud
{
    /// <summary>
    /// ダミー点群生成に必要なパラメータ、ターゲットオブジェクト、Transform、および Renderer の
    /// 変更状態（Dirty / Moved / Swapped）を追跡・検知するクラス。
    /// </summary>
    public class RsDummyChangeTracker
    {
        private struct TargetObjectSnapshot
        {
            public GameObject gameObject;
            public bool activeInHierarchy;
        }

        private struct TransformSnapshot
        {
            public Transform transform;
            public Renderer renderer;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public bool isRendererActiveAndEnabled;
        }

        private readonly List<TargetObjectSnapshot> _cachedTargetSnapshots = new List<TargetObjectSnapshot>();
        private readonly List<TransformSnapshot> _trackedTransforms = new List<TransformSnapshot>();

        // パラメータ変更検知用キャッシュ
        private PointDensityUnit _cachedDensityUnit;
        private float _cachedDensityValue = -1f;
        private int _cachedMaxPointLimit = -1;
        private PointColorMode _cachedColorMode;
        private Color _cachedSolidColor;
        private bool _cachedIncludeChildren;
        private bool _cachedUseCameraPerspective;
        private Transform _cachedSimulatedCameraTransform;

        // ノイズ設定変更検知用キャッシュ
        private bool _cachedNoiseEnabled;
        private float _cachedNoiseAmountMm = -1f;
        private float _cachedNoiseRatio = -1f;
        private NoiseDistributionType _cachedNoiseType;
        private bool _cachedOutliersEnabled;
        private float _cachedOutlierRatio = -1f;
        private float _cachedOutlierDistanceMm = -1f;
        private NoiseUpdateMode _cachedNoiseUpdateMode;

        private bool _isDirty = true;
        private float _lastDynamicUpdateTime = 0f;

        /// <summary>
        /// 外部から強制的にダーティフラグを立てます。
        /// </summary>
        public void SetDirty()
        {
            _isDirty = true;
        }

        /// <summary>
        /// 現在のフレームで点群の再サンプリング・更新が必要かどうかを総合判定します。
        /// </summary>
        public bool CheckShouldUpdate(
            RsDummyPointCloudProvider provider,
            RsPointCloudNoiseSettings noiseSettings,
            out bool isDynamicNoise,
            out bool requiresCacheInvalidation)
        {
            requiresCacheInvalidation = false;
            isDynamicNoise = noiseSettings != null &&
                             (noiseSettings.enableNoise || noiseSettings.enableOutliers) &&
                             noiseSettings.updateMode == NoiseUpdateMode.Dynamic;

            if (_isDirty)
            {
                requiresCacheInvalidation = true;
                return true;
            }

            if (HasSettingsChanged(provider, noiseSettings) || HasTargetObjectsChanged(provider.targetObjects))
            {
                requiresCacheInvalidation = true;
                return true;
            }

            Transform camXform = provider.simulatedCameraTransform != null ? provider.simulatedCameraTransform : provider.transform;
            if (HasTransformsOrRenderersChanged(provider.targetObjects, provider.includeChildren, camXform))
            {
                return true;
            }

            if (isDynamicNoise)
            {
                float interval = 1.0f / Mathf.Max(1, provider.updateFPS);
                if (Time.time - _lastDynamicUpdateTime >= interval)
                {
                    _lastDynamicUpdateTime = Time.time;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 設定値やターゲット数の変化を検知します。
        /// </summary>
        private bool HasSettingsChanged(RsDummyPointCloudProvider provider, RsPointCloudNoiseSettings noiseSettings)
        {
            if (_cachedDensityUnit != provider.densityUnit) return true;
            if (!Mathf.Approximately(_cachedDensityValue, provider.densityValue)) return true;
            if (_cachedMaxPointLimit != provider.maxPointLimit) return true;
            if (_cachedColorMode != provider.colorMode) return true;
            if (_cachedSolidColor != provider.solidColor) return true;
            if (_cachedIncludeChildren != provider.includeChildren) return true;
            if (_cachedUseCameraPerspective != provider.useCameraPerspective) return true;
            if (_cachedSimulatedCameraTransform != provider.simulatedCameraTransform) return true;

            if (noiseSettings != null)
            {
                if (_cachedNoiseEnabled != noiseSettings.enableNoise) return true;
                if (!Mathf.Approximately(_cachedNoiseAmountMm, noiseSettings.noiseAmountMm)) return true;
                if (!Mathf.Approximately(_cachedNoiseRatio, noiseSettings.noiseRatio)) return true;
                if (_cachedNoiseType != noiseSettings.noiseType) return true;
                if (_cachedOutliersEnabled != noiseSettings.enableOutliers) return true;
                if (!Mathf.Approximately(_cachedOutlierRatio, noiseSettings.outlierRatio)) return true;
                if (!Mathf.Approximately(_cachedOutlierDistanceMm, noiseSettings.outlierDistanceMm)) return true;
                if (_cachedNoiseUpdateMode != noiseSettings.updateMode) return true;
            }

            return false;
        }

        /// <summary>
        /// targetObjects リストの要素参照や activeInHierarchy 状態が切り替わったかどうかを検知します。
        /// </summary>
        public bool HasTargetObjectsChanged(List<GameObject> targetObjects)
        {
            int currentCount = targetObjects != null ? targetObjects.Count : 0;
            if (currentCount != _cachedTargetSnapshots.Count) return true;

            if (targetObjects != null)
            {
                for (int i = 0; i < currentCount; i++)
                {
                    var curObj = targetObjects[i];
                    var cached = _cachedTargetSnapshots[i];

                    if (curObj != cached.gameObject) return true;
                    if (curObj != null)
                    {
                        if (curObj.activeInHierarchy != cached.activeInHierarchy) return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 追跡中オブジェクトの Transform 変化、または Renderer の有効無効・増減を検知します。
        /// </summary>
        private bool HasTransformsOrRenderersChanged(List<GameObject> targetObjects, bool includeChildren, Transform camXform)
        {
            if (camXform != null && camXform.hasChanged)
            {
                camXform.hasChanged = false;
                return true;
            }

            int activeRendererCount = 0;
            if (targetObjects != null)
            {
                for (int i = 0; i < targetObjects.Count; i++)
                {
                    var obj = targetObjects[i];
                    if (obj == null || !obj.activeInHierarchy) continue;
                    var renderers = includeChildren ? obj.GetComponentsInChildren<Renderer>() : obj.GetComponents<Renderer>();
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        var rend = renderers[r];
                        if (rend != null && RsMeshPointCloudSampler.IsRendererActiveForSampling(rend, obj))
                        {
                            activeRendererCount++;
                        }
                    }
                }
            }

            if (activeRendererCount != _trackedTransforms.Count)
            {
                return true;
            }

            for (int i = 0; i < _trackedTransforms.Count; i++)
            {
                var snap = _trackedTransforms[i];
                if (snap.transform == null || snap.renderer == null) return true;
                if (!snap.renderer.gameObject.activeInHierarchy || snap.renderer.enabled != snap.isRendererActiveAndEnabled) return true;
                if (snap.transform.hasChanged ||
                    snap.transform.position != snap.position ||
                    snap.transform.rotation != snap.rotation ||
                    snap.transform.lossyScale != snap.scale)
                {
                    snap.transform.hasChanged = false;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// サンプリング完了後、最新の状態スナップショットを記録します。
        /// </summary>
        public void UpdateSnapshots(RsDummyPointCloudProvider provider, RsPointCloudNoiseSettings noiseSettings)
        {
            _isDirty = false;

            _cachedDensityUnit = provider.densityUnit;
            _cachedDensityValue = provider.densityValue;
            _cachedMaxPointLimit = provider.maxPointLimit;
            _cachedColorMode = provider.colorMode;
            _cachedSolidColor = provider.solidColor;
            _cachedIncludeChildren = provider.includeChildren;
            _cachedUseCameraPerspective = provider.useCameraPerspective;
            _cachedSimulatedCameraTransform = provider.simulatedCameraTransform;

            if (noiseSettings != null)
            {
                _cachedNoiseEnabled = noiseSettings.enableNoise;
                _cachedNoiseAmountMm = noiseSettings.noiseAmountMm;
                _cachedNoiseRatio = noiseSettings.noiseRatio;
                _cachedNoiseType = noiseSettings.noiseType;
                _cachedOutliersEnabled = noiseSettings.enableOutliers;
                _cachedOutlierRatio = noiseSettings.outlierRatio;
                _cachedOutlierDistanceMm = noiseSettings.outlierDistanceMm;
                _cachedNoiseUpdateMode = noiseSettings.updateMode;
            }

            _cachedTargetSnapshots.Clear();
            if (provider.targetObjects != null)
            {
                for (int i = 0; i < provider.targetObjects.Count; i++)
                {
                    var obj = provider.targetObjects[i];
                    _cachedTargetSnapshots.Add(new TargetObjectSnapshot
                    {
                        gameObject = obj,
                        activeInHierarchy = obj != null && obj.activeInHierarchy
                    });
                }
            }

            _trackedTransforms.Clear();
            if (provider.targetObjects != null)
            {
                foreach (var obj in provider.targetObjects)
                {
                    if (obj == null || !obj.activeInHierarchy) continue;
                    var renderers = provider.includeChildren ? obj.GetComponentsInChildren<Renderer>() : obj.GetComponents<Renderer>();
                    foreach (var r in renderers)
                    {
                        if (r == null || !RsMeshPointCloudSampler.IsRendererActiveForSampling(r, obj)) continue;
                        r.transform.hasChanged = false;
                        _trackedTransforms.Add(new TransformSnapshot
                        {
                            transform = r.transform,
                            renderer = r,
                            position = r.transform.position,
                            rotation = r.transform.rotation,
                            scale = r.transform.lossyScale,
                            isRendererActiveAndEnabled = r.enabled && r.gameObject.activeInHierarchy
                        });
                    }
                }
            }
        }
    }
}
