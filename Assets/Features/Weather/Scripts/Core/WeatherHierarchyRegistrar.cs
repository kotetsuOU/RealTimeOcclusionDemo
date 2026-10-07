using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 天候演出システムにおける子プロセッサの検索・自動生成・配線、カメラ参照の解決、
    /// および PCD レイヤー (Layer 3) の再帰的適用を担当する Pure C# レジストラクラス。
    /// </summary>
    public static class WeatherHierarchyRegistrar
    {
        /// <summary>
        /// 視点カメラ (Viewer) が未設定の場合に探索・設定します。
        /// </summary>
        public static void EnsureViewer(Transform root, ref Transform viewer)
        {
            if (viewer != null) return;

            if (Camera.main != null)
            {
                viewer = Camera.main.transform;
            }
            else
            {
                var cam = Object.FindFirstObjectByType<Camera>();
                if (cam != null) viewer = cam.transform;
            }
        }

        /// <summary>
        /// 各種天候サブプロセッサが存在するか確認し、なければ子 GameObject を生成して自動配線します。
        /// </summary>
        public static void EnsureProcessors(
            Transform root,
            ref WeatherRainProcessor rainProcessor,
            ref WeatherCloudProcessor cloudProcessor,
            ref WeatherLightningProcessor lightningProcessor,
            ref WeatherLightingProcessor lightingProcessor,
            ref WeatherAudioSynthesizer audioSynthesizer,
            ref WeatherHcdInputBridge inputBridge,
            ref WeatherKeyController keyController)
        {
            GetOrAddProcessor(root, ref rainProcessor, "RainProcessor");
            GetOrAddProcessor(root, ref cloudProcessor, "CloudProcessor");
            GetOrAddProcessor(root, ref lightningProcessor, "LightningProcessor");
            GetOrAddProcessor(root, ref lightingProcessor, "LightingProcessor");
            GetOrAddProcessor(root, ref audioSynthesizer, "AudioSynthesizer");

            if (inputBridge == null) inputBridge = root.GetComponentInChildren<WeatherHcdInputBridge>();
            if (keyController == null) keyController = root.GetComponentInChildren<WeatherKeyController>();
        }

        private static void GetOrAddProcessor<T>(Transform root, ref T processor, string childName) where T : Component
        {
            if (processor != null) return;
            processor = root.GetComponentInChildren<T>();
            if (processor != null) return;

            var go = new GameObject(childName);
            go.transform.SetParent(root, false);
            processor = go.AddComponent<T>();
        }

        /// <summary>
        /// 指定オブジェクト配下全体に PCD レイヤー (仮想オブジェクト用 Layer 3) を再帰的に適用します。
        /// </summary>
        public static void ApplyPcdLayer(GameObject root)
        {
            if (root == null) return;
            int pcdLayer = LayerMask.NameToLayer("PCD");
            if (pcdLayer >= 0)
            {
                SetLayerRecursively(root, pcdLayer);
            }
        }

        /// <summary>
        /// GameObject とその全子孫のレイヤーを再帰的に設定します。
        /// </summary>
        public static void SetLayerRecursively(GameObject obj, int newLayer)
        {
            if (obj == null) return;
            obj.layer = newLayer;
            foreach (Transform child in obj.transform)
            {
                if (child != null) SetLayerRecursively(child.gameObject, newLayer);
            }
        }
    }
}
