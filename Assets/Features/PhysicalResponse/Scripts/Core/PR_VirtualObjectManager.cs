using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using Core.Logging;

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
            InitializeObjects();
        }

        private void OnEnable()
        {
            Instance = this;
            InitializeObjects();
        }

        private void Update()
        {
            if (virtualObjects == null || virtualObjects.Length == 0)
            {
                InitializeObjects();
                if (virtualObjects == null || virtualObjects.Length == 0) return;
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
            // 配列が未指定または空の場合、自身の子オブジェクトから収集
            if (virtualObjects == null || virtualObjects.Length == 0)
            {
                int childCount = transform.childCount;
                if (childCount > 0)
                {
                    virtualObjects = new GameObject[childCount];
                    for (int i = 0; i < childCount; i++)
                    {
                        virtualObjects[i] = transform.GetChild(i).gameObject;
                    }
                }
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
                    currentActiveIndex = 0;
                    virtualObjects[0].SetActive(true);
                    NotifyActiveObjectChanged(virtualObjects[0]);
                }
                else if (foundActiveIndex >= 0)
                {
                    currentActiveIndex = foundActiveIndex;
                }
            }
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

            for (int i = 0; i < virtualObjects.Length; i++)
            {
                if (virtualObjects[i] != null && virtualObjects[i].activeInHierarchy && i != currentActiveIndex)
                {
                    currentActiveIndex = i;
                    GameObject newActive = virtualObjects[currentActiveIndex];
                    if (syncWithHcd && newActive != null)
                    {
                        SyncHcdTarget(newActive);
                    }
                    NotifyActiveObjectChanged(newActive);
                    break;
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
            if (hcd != null && hcd.distanceProcessor != null)
            {
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
        }
    }
}
