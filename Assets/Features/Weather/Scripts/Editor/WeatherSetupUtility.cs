
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Features.Weather;
using Features.PhysicalResponse;

namespace Features.Weather.Editor
{
    /// <summary>
    /// Weather（天候演出）システムの各種マテリアル・ライト・プロセッサを自動セットアップし、
    /// Inspector 上に完全にシリアライズ結線された GameObject および Prefab を生成するエディタユーティリティ。
    /// </summary>
    public static class WeatherSetupUtility
    {
        private const string RainMatPath = "Assets/Features/Weather/Materials/M_Weather_Rain.mat";
        private const string CloudMatPath = "Assets/Features/Weather/Materials/M_Weather_Cloud.mat";
        private const string LightningMatPath = "Assets/Features/Weather/Materials/M_Weather_Lightning.mat";
        private const string PrefabDir = "Assets/Features/Weather/Prefabs";
        private const string PrefabPath = "Assets/Features/Weather/Prefabs/WeatherSystem.prefab";

        [MenuItem("GameObject/Weather/Create Weather System in Scene", false, 10)]
        public static GameObject CreateWeatherSystemInScene()
        {
            var root = BuildWeatherHierarchy(null);
            Undo.RegisterCreatedObjectUndo(root, "Create Weather System");
            Selection.activeGameObject = root;
            Debug.Log("[WeatherSetupUtility] シーン内にシリアライズ済みの Weather システムを作成しました。");
            return root;
        }

        [MenuItem("GameObject/Weather/Apply PCD Layer to Weather", false, 15)]
        public static void ApplyPcdLayerToWeather()
        {
            var weatherManager = Object.FindFirstObjectByType<WeatherManager>();
            if (weatherManager == null)
            {
                EditorUtility.DisplayDialog("Weather Setup", "シーン内に WeatherManager が見つかりませんでした。", "OK");
                return;
            }

            int pcdLayer = LayerMask.NameToLayer("PCD");
            if (pcdLayer < 0)
            {
                EditorUtility.DisplayDialog("Weather Setup", "プロジェクトに 'PCD' レイヤーが定義されていません。", "OK");
                return;
            }

            WeatherManager.SetLayerRecursively(weatherManager.gameObject, pcdLayer);
            EditorUtility.SetDirty(weatherManager.gameObject);
            Debug.Log($"[WeatherSetupUtility] {weatherManager.gameObject.name} およびすべての子オブジェクトのレイヤーを 'PCD' (Layer {pcdLayer}) に設定しました。");
            EditorUtility.DisplayDialog("Weather Setup", $"{weatherManager.gameObject.name} 階層全体のレイヤーを 'PCD' (Layer {pcdLayer}) に設定しました。\nPCDRendererFeature によるオクルージョン描画に正常に接続されます。", "OK");
        }

        [MenuItem("GameObject/Weather/Remove Weather from PR_VirtualObjectManager", false, 16)]
        public static void RemoveWeatherFromVirtualObjectManager()
        {
            var vom = Object.FindFirstObjectByType<PR_VirtualObjectManager>();
            if (vom == null)
            {
                EditorUtility.DisplayDialog("Weather Setup", "シーン内に PR_VirtualObjectManager が見つかりませんでした。", "OK");
                return;
            }

            var weatherManager = Object.FindFirstObjectByType<WeatherManager>();
            if (weatherManager == null)
            {
                EditorUtility.DisplayDialog("Weather Setup", "シーン内に WeatherManager が見つかりませんでした。", "OK");
                return;
            }

            GameObject weatherGo = weatherManager.gameObject;

            var list = new List<GameObject>();
            bool removed = false;
            if (vom.virtualObjects != null)
            {
                foreach (var obj in vom.virtualObjects)
                {
                    if (obj == weatherGo || (obj != null && obj.GetComponentInChildren<WeatherManager>() != null))
                    {
                        removed = true;
                    }
                    else if (obj != null)
                    {
                        list.Add(obj);
                    }
                }
            }

            if (removed)
            {
                vom.virtualObjects = list.ToArray();
                EditorUtility.SetDirty(vom);

                // vom の子階層にある場合はルートへ戻す
                if (weatherGo.transform.parent == vom.transform)
                {
                    weatherGo.transform.SetParent(null, true);
                }

                Debug.Log("[WeatherSetupUtility] PR_VirtualObjectManager から Weather を除外しました。");
                EditorUtility.DisplayDialog("Weather Setup", "PR_VirtualObjectManager から Weather を正常に除外しました。\nWeather は通常の独立エフェクトとして動作し、PR_BoneDetector や HCD に影響を与えません。", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Weather Setup", "PR_VirtualObjectManager に Weather は登録されていませんでした。", "OK");
            }
        }

        [MenuItem("Weather/Generate WeatherSystem Prefab", false, 20)]
        public static void GenerateWeatherPrefab()
        {
            if (!Directory.Exists(PrefabDir))
            {
                Directory.CreateDirectory(PrefabDir);
            }

            var tempGo = BuildWeatherHierarchy(null);
            try
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(tempGo, PrefabPath, InteractionMode.UserAction);
                Debug.Log($"[WeatherSetupUtility] Prefab を正常に生成・保存しました: {PrefabPath}");
            }
            finally
            {
                Object.DestroyImmediate(tempGo);
            }
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 既存または新規の GameObject 上に天候コンポーネント階層を構築し、
        /// マテリアル・ライト・子プロセッサを完全にシリアライズ結線します。
        /// </summary>
        public static GameObject BuildWeatherHierarchy(GameObject targetRoot)
        {
            GameObject root = targetRoot;
            if (root == null)
            {
                root = new GameObject("Weather");
            }

            // 1. マテリアルのロード
            var rainMat = AssetDatabase.LoadAssetAtPath<Material>(RainMatPath);
            var cloudMat = AssetDatabase.LoadAssetAtPath<Material>(CloudMatPath);
            var lightningMat = AssetDatabase.LoadAssetAtPath<Material>(LightningMatPath);

            // 2. WeatherManager の設定
            var manager = GetOrAddComponent<WeatherManager>(root);
            var keyController = GetOrAddComponent<WeatherKeyController>(root);
            var logTriggers = GetOrAddComponent<WeatherLogTriggers>(root);
            var foxHandler = GetOrAddComponent<WeatherFoxReactionHandler>(root);

            // 3. RainProcessor の作成と結線
            var rainChild = GetOrCreateChild(root.transform, "RainProcessor");
            var rainProc = GetOrAddComponent<WeatherRainProcessor>(rainChild);
            if (rainMat != null)
            {
                rainProc.ParticleMaterial = rainMat;
            }
            manager.RainProcessor = rainProc;

            // 4. CloudProcessor の作成と結線
            var cloudChild = GetOrCreateChild(root.transform, "CloudProcessor");
            var cloudProc = GetOrAddComponent<WeatherCloudProcessor>(cloudChild);
            if (cloudMat != null)
            {
                cloudProc.CloudMaterial = cloudMat;
            }
            manager.CloudProcessor = cloudProc;

            // 5. LightningProcessor の作成と結線
            var lightningChild = GetOrCreateChild(root.transform, "LightningProcessor");
            var lightningProc = GetOrAddComponent<WeatherLightningProcessor>(lightningChild);
            if (lightningMat != null)
            {
                lightningProc.LightningMaterial = lightningMat;
            }
            var boltPoolChild = GetOrCreateChild(lightningChild.transform, "LightningBoltPool");
            var boltPool = GetOrAddComponent<WeatherLightningBoltPool>(boltPoolChild);
            lightningProc.BoltPool = boltPool;
            manager.LightningProcessor = lightningProc;

            // 5. LightingProcessor と BoltPointLight の作成・結線
            var lightingChild = GetOrCreateChild(root.transform, "LightingProcessor");
            var lightingProc = GetOrAddComponent<WeatherLightingProcessor>(lightingChild);

            var boltLightChild = GetOrCreateChild(lightingChild.transform, "BoltPointLight");
            var boltLight = GetOrAddComponent<Light>(boltLightChild);
            boltLight.type = LightType.Point;
            boltLight.color = new Color(0.85f, 0.9f, 1.0f);
            boltLight.range = 7.0f;
            boltLight.intensity = 0f;
            boltLight.shadows = LightShadows.None;
            boltLight.enabled = false;
            lightingProc.BoltPointLight = boltLight;

            // シーン内の Directional Light を探索して設定
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional)
                {
                    lightingProc.SunLight = l;
                    break;
                }
            }
            manager.LightingProcessor = lightingProc;

            // 6. AudioSynthesizer の作成と結線
            var audioChild = GetOrCreateChild(root.transform, "AudioSynthesizer");
            var audioSynth = GetOrAddComponent<WeatherAudioSynthesizer>(audioChild);
            var audioSource = GetOrAddComponent<AudioSource>(audioChild);
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            manager.AudioSynthesizer = audioSynth;

            // 7. InputBridge の作成と結線
            var inputChild = GetOrCreateChild(root.transform, "InputBridge");
            var inputBridge = GetOrAddComponent<WeatherHcdInputBridge>(inputChild);
            manager.InputBridge = inputBridge;

            // 8. KeyController の結線
            manager.KeyController = keyController;

            // 9. Manager Settings 側へのマテリアル・ライトの同期
            if (rainMat != null) manager.Rain.material = rainMat;
            if (lightningMat != null) manager.Lightning.material = lightningMat;
            if (boltLight != null) manager.Lighting.boltPointLight = boltLight;
            if (lightingProc.SunLight != null) manager.Lighting.sunLight = lightingProc.SunLight;

            manager.ApplyAllSettings();

            // 10. PCD レイヤーの適用 (PCDRenderer 接続用)
            int pcdLayer = LayerMask.NameToLayer("PCD");
            if (pcdLayer >= 0)
            {
                WeatherManager.SetLayerRecursively(root, pcdLayer);
            }

            // シリアライズ変更を反映
            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(rainProc);
            EditorUtility.SetDirty(lightningProc);
            EditorUtility.SetDirty(lightingProc);

            return root;
        }

        private static GameObject GetOrCreateChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null) return child.gameObject;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static T GetOrAddComponent<T>(GameObject go) where T : Component
        {
            var comp = go.GetComponent<T>();
            if (comp == null)
            {
                comp = go.AddComponent<T>();
            }
            return comp;
        }
    }
}
#endif
