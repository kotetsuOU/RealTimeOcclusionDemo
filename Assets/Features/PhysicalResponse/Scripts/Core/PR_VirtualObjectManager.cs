using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Core.Logging;
using Features.Weather;

namespace Features.PhysicalResponse
{
    /// <summary>
    /// シーン内のバーチャルオブジェクト（Fox等の各バリエーションモデル）のアクティブ状態および切り替えを一元管理するクラス。
    /// Tabキーや外部呼び出しによるモデル切り替え、現在アクティブなモデルの提供、
    /// およびHCD（触覚判定）ターゲットの自動更新を担います。
    /// </summary>
    [ExecuteAlways]
    [AppLoggable("VirtualObject")]
    [MovedFrom(true, "Features.Animation", null, "VirtualObjectManager")]
    public class PR_VirtualObjectManager : MonoBehaviour, IAppLoggable
    {
        public static PR_VirtualObjectManager Instance { get; private set; }

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            // AppLogger 管理用トリガー（必要に応じて拡張可能）
        }

        [Header("Virtual Objects Setup")]
        [Tooltip("切り替え対象のバーチャルオブジェクト配列（未設定時は自身の子階層から自動検出します）")]
        public GameObject[] virtualObjects;

        [Tooltip("Tabキー入力によるモデルの順番切り替えを有効にするか")]
        public bool allowTabSwitch = true;

        [Tooltip("現在アクティブなオブジェクトに合わせてHCDの接触判定対象を自動更新するか")]
        public bool syncWithHcd = true;

        [SerializeField] private int currentActiveIndex = 0;

        /// <summary>現在アクティブなバーチャルオブジェクト</summary>
        public GameObject ActiveObject
        {
            get
            {
                if (virtualObjects != null && currentActiveIndex >= 0 && currentActiveIndex < virtualObjects.Length)
                {
                    return virtualObjects[currentActiveIndex];
                }
                return null;
            }
        }

        /// <summary>現在アクティブなバーチャルオブジェクトのTransform</summary>
        public Transform ActiveTransform => ActiveObject != null ? ActiveObject.transform : null;

        /// <summary>現在アクティブなバーチャルオブジェクトのAnimator</summary>
        public Animator ActiveAnimator => ActiveObject != null ? ActiveObject.GetComponent<Animator>() : null;

        /// <summary>現在アクティブなインデックス</summary>
        public int CurrentActiveIndex => currentActiveIndex;

        /// <summary>アクティブオブジェクトが切り替わった際に発火するイベント</summary>
        public event Action<GameObject> OnActiveObjectChanged;

        private void Awake()
        {
            Instance = this;
            SanitizeVirtualObjects();
            InitializeObjects();
        }

        private void OnEnable()
        {
            Instance = this;
            SanitizeVirtualObjects();
            InitializeObjects();
        }

        private void Update()
        {
            if (virtualObjects == null || virtualObjects.Length == 0)
            {
                InitializeObjects();
                if (virtualObjects == null || virtualObjects.Length == 0) return;
            }

            // 0. Weather 等の非バーチャルオブジェクトが混入している場合は自動除外
            for (int i = 0; i < virtualObjects.Length; i++)
            {
                if (IsExcludedFromVirtualObjects(virtualObjects[i]))
                {
                    SanitizeVirtualObjects();
                    break;
                }
            }

            // 1. Play時のTabキー切り替え
            if (Application.isPlaying && allowTabSwitch)
            {
                if (Input.GetKeyDown(KeyCode.Tab))
                {
                    SwitchNext();
                }
            }

            // 2. Hierarchy上での手動アクティブ変更検知（エディタ非Play時および実行時）
            VerifyActiveHierarchyState();
        }

        /// <summary>
        /// オブジェクト配列の初期化および現在のアクティブ状態の整合性チェック
        /// </summary>
        public void InitializeObjects()
        {
            // 配列が未指定または空の場合、自身の子オブジェクトから収集（Weather 等は除外）
            if (virtualObjects == null || virtualObjects.Length == 0)
            {
                var children = new List<GameObject>();
                for (int i = 0; i < transform.childCount; i++)
                {
                    var child = transform.GetChild(i).gameObject;
                    if (!IsExcludedFromVirtualObjects(child))
                    {
                        children.Add(child);
                    }
                }
                virtualObjects = children.ToArray();
            }
            else
            {
                SanitizeVirtualObjects();
            }

            if (virtualObjects != null && virtualObjects.Length > 0)
            {
                // 現在activeInHierarchyなものを探索
                int activeCount = 0;
                int foundActiveIndex = -1;
                for (int i = 0; i < virtualObjects.Length; i++)
                {
                    if (virtualObjects[i] != null && virtualObjects[i].activeInHierarchy)
                    {
                        activeCount++;
                        if (foundActiveIndex < 0) foundActiveIndex = i;
                    }
                }

                // 誰もアクティブでない場合は先頭をアクティブ化
                if (activeCount == 0 && virtualObjects[0] != null)
                {
                    SwitchTo(0);
                }
                else if (foundActiveIndex >= 0)
                {
                    // 複数アクティブだった場合も含めて、見つかった1つに排他化
                    SwitchTo(foundActiveIndex);
                }
            }
        }

        /// <summary>
        /// バーチャルオブジェクト配列から天候など非バーチャルオブジェクトを自動除外し、親子関係を解除します。
        /// </summary>
        public void SanitizeVirtualObjects()
        {
            if (virtualObjects == null || virtualObjects.Length == 0) return;

            var cleanList = new List<GameObject>();
            bool modified = false;

            for (int i = 0; i < virtualObjects.Length; i++)
            {
                var obj = virtualObjects[i];
                if (obj == null) continue;

                if (IsExcludedFromVirtualObjects(obj))
                {
                    modified = true;
                    // もし自身の子階層に入っていたらルート階層へ切り離す
                    if (obj.transform.parent == transform)
                    {
                        obj.transform.SetParent(null, true);
                    }
                    continue;
                }

                cleanList.Add(obj);
            }

            if (modified)
            {
                virtualObjects = cleanList.ToArray();
                AppLogger.Log(this, "[PR_VirtualObjectManager] Weather 演出オブジェクトをバーチャルモデル管理配列から自動除外しました。");
            }
        }

        private static bool IsExcludedFromVirtualObjects(GameObject obj)
        {
            if (obj == null) return false;
            // WeatherManager を保持、または名前が Weather の場合は広域環境演出のため除外
            if (obj.GetComponentInChildren<WeatherManager>() != null || obj.name == "Weather")
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// 次のバーチャルオブジェクトへアクティブを切り替えます。
        /// </summary>
        public void SwitchNext()
        {
            if (virtualObjects == null || virtualObjects.Length == 0) return;
            int nextIndex = (currentActiveIndex + 1) % virtualObjects.Length;
            SwitchTo(nextIndex);
        }

        /// <summary>
        /// 指定インデックスのバーチャルオブジェクトへ切り替えます。
        /// </summary>
        public void SwitchTo(int index)
        {
            if (virtualObjects == null || virtualObjects.Length == 0) return;
            if (index < 0 || index >= virtualObjects.Length) return;

            // 全て非アクティブにし、対象のみアクティブ化
            for (int i = 0; i < virtualObjects.Length; i++)
            {
                if (virtualObjects[i] != null)
                {
                    virtualObjects[i].SetActive(i == index);
                }
            }

            currentActiveIndex = index;
            GameObject newActive = virtualObjects[currentActiveIndex];

            AppLogger.Log(this, $"アクティブオブジェクトを切り替えました: {newActive?.name} (Index: {currentActiveIndex})");

            // HCDターゲットの自動同期
            if (syncWithHcd && newActive != null)
            {
                SyncHcdTarget(newActive);
            }

            NotifyActiveObjectChanged(newActive);
        }

        /// <summary>
        /// Hierarchy上で手動でアクティブが切り替えられた場合の検知と同期
        /// </summary>
        private void VerifyActiveHierarchyState()
        {
            if (virtualObjects == null || virtualObjects.Length == 0) return;

            // 1. 別のオブジェクトが手動でアクティブ化された場合、それを新アクティブとして排他切り替え
            for (int i = 0; i < virtualObjects.Length; i++)
            {
                if (virtualObjects[i] != null && virtualObjects[i].activeInHierarchy && i != currentActiveIndex)
                {
                    // 手動でONにされたオブジェクトへ正式に排他切り替え（他をOFFにして発振防止）
                    SwitchTo(i);
                    return;
                }
            }

            // 2. 現在のアクティブオブジェクトが手動で非アクティブ化された場合
            if (currentActiveIndex >= 0 && currentActiveIndex < virtualObjects.Length)
            {
                GameObject currentObj = virtualObjects[currentActiveIndex];
                if (currentObj != null && !currentObj.activeInHierarchy)
                {
                    int nextActive = -1;
                    for (int i = 0; i < virtualObjects.Length; i++)
                    {
                        if (virtualObjects[i] != null && virtualObjects[i].activeInHierarchy)
                        {
                            nextActive = i;
                            break;
                        }
                    }

                    if (nextActive >= 0)
                    {
                        SwitchTo(nextActive);
                    }
                    else
                    {
                        currentActiveIndex = -1;
                        if (syncWithHcd)
                        {
                            ClearHcdTarget();
                        }
                        NotifyActiveObjectChanged(null);
                    }
                }
            }
        }

        private void NotifyActiveObjectChanged(GameObject newActive)
        {
            OnActiveObjectChanged?.Invoke(newActive);

            // PR_BoneDetector が存在すれば即座にターゲット更新
            if (PR_BoneDetector.Instance != null && newActive != null)
            {
                PR_BoneDetector.Instance.SetTarget(newActive.transform);
            }
        }

        /// <summary>
        /// HCD_Pipeline の判定ターゲットを現在のアクティブオブジェクトに更新します。
        /// </summary>
        private void SyncHcdTarget(GameObject activeObj)
        {
            var hcd = FindFirstObjectByType<HCD_Pipeline>();
            if (hcd == null || hcd.distanceProcessor == null) return;

            // IExcludeFromHcd を持つオブジェクト（天候演出など）は HCD 判定から除外
            if (activeObj.GetComponentInChildren<IExcludeFromHcd>() != null)
            {
                AppLogger.Log(this, $"[PR_VirtualObjectManager] '{activeObj.name}' は IExcludeFromHcd を実装しているため、HCDターゲットの登録をスキップしました。");
                ClearHcdTarget();
                return;
            }

            var skinnedMeshes = activeObj.GetComponentsInChildren<SkinnedMeshRenderer>();
            var meshFilters = activeObj.GetComponentsInChildren<MeshFilter>();

            hcd.distanceProcessor.targetSkinnedMeshes = skinnedMeshes;
            hcd.distanceProcessor.targetMeshFilters = meshFilters;

            if (skinnedMeshes != null && skinnedMeshes.Length > 0)
            {
                hcd.distanceProcessor.detectionMode = HCD_DistanceProcessor.DetectionMode.SkinnedMeshRenderer;
            }
            else if (meshFilters != null && meshFilters.Length > 0)
            {
                hcd.distanceProcessor.detectionMode = HCD_DistanceProcessor.DetectionMode.MeshFilter;
            }
            else
            {
                hcd.distanceProcessor.detectionMode = HCD_DistanceProcessor.DetectionMode.TransformOnly;
                hcd.distanceProcessor.targetObject = activeObj.transform;
                hcd.distanceProcessor.targetTransforms = activeObj.GetComponentsInChildren<Transform>();
            }
        }

        private void ClearHcdTarget()
        {
            var hcd = FindFirstObjectByType<HCD_Pipeline>();
            if (hcd == null || hcd.distanceProcessor == null) return;

            hcd.distanceProcessor.targetSkinnedMeshes = null;
            hcd.distanceProcessor.targetMeshFilters = null;
            hcd.distanceProcessor.targetTransforms = null;
            hcd.distanceProcessor.targetObject = null;
        }
    }
}
