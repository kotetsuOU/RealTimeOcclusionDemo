using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using static PCDRendererFeature;
using Core.Logging;

namespace Features.PhysicalResponse
{
    /// <summary>
    /// バーチャルオブジェクトのアニメーション制御・手動移動・視点(カメラ)LookAt追従に専念するコントローラー。
    /// PCD設定系キー操作（M/1/2/3/4/T/O/P/L/K/J/C）は PR_PCDKeyController に委譲します。
    /// アクティブなモデルの管理は PR_VirtualObjectManager に委譲します。
    /// </summary>
    [MovedFrom(true, null, null, "AnimationController")]
    [AppLoggable("PR (PhysicalResponse)")]
    public class PR_AnimationController : MonoBehaviour, IAppLoggable
    {
        public const string TagAnimController = "PR_AnimationController";

        public void RegisterLogTriggers(LogCategoryGroup group, HashSet<string> existingLabels)
        {
            const string label = "[PR_AnimationController] Animation State";
            if (!existingLabels.Contains(label))
            {
                group.entries.Add(new LogInstanceEntry { label = label, tag = TagAnimController, target = this, enabled = true });
                existingLabels.Add(label);
            }
        }

        [Header("Virtual Object Manager Integration")]
        [Tooltip("バーチャルオブジェクトの管理・切り替えを一元管理するマネージャ")]
        public PR_VirtualObjectManager virtualObjectManager;

        [Header("Control Targets")]
        [Tooltip("アニメーションの再生/一時停止を切り替えるAnimator (PR_VirtualObjectManagerから自動取得されます)")]
        private Animator targetAnimator;

        [Tooltip("旧仕様のオブジェクト配列（PR_VirtualObjectManagerが未設定の場合のフォールバック用）")]
        public GameObject[] toggleObjects;

        private int currentActiveIndex = 0;

        [Tooltip("キーボード操作で移動させる対象のオブジェクト (PR_VirtualObjectManagerから自動取得されます)")]
        private Transform targetTransform;

        [Tooltip("カメラキャプチャ用スクリプト (ViewPointのカメラ映像保存用)")]
        public CameraCapture cameraCapture;

        [Tooltip("移動速度")]
        public float moveSpeed = 1.0f;

        [Header("Look At Settings")]
        [Tooltip("視点(カメラ)に自動で追従して向きを変えるか (Fキーで切替)")]
        public bool lookAtCamera = true;
        [Tooltip("向きを変える速度")]
        public float lookAtSpeed = 5.0f;

        private void Awake()
        {
            EnsureVirtualObjectManager();
        }

        private void Start()
        {
            EnsureVirtualObjectManager();
            UpdateActiveTargetReferences();
        }

        private void OnEnable()
        {
            EnsureVirtualObjectManager();
            if (virtualObjectManager != null)
            {
                virtualObjectManager.OnActiveObjectChanged -= HandleActiveObjectChanged;
                virtualObjectManager.OnActiveObjectChanged += HandleActiveObjectChanged;
            }
        }

        private void OnDisable()
        {
            if (virtualObjectManager != null)
            {
                virtualObjectManager.OnActiveObjectChanged -= HandleActiveObjectChanged;
            }
        }

        private void EnsureVirtualObjectManager()
        {
            if (virtualObjectManager == null)
            {
                virtualObjectManager = PR_VirtualObjectManager.Instance != null
                    ? PR_VirtualObjectManager.Instance
                    : FindFirstObjectByType<PR_VirtualObjectManager>();
            }

            if (virtualObjectManager != null && (virtualObjectManager.virtualObjects == null || virtualObjectManager.virtualObjects.Length == 0))
            {
                if (toggleObjects != null && toggleObjects.Length > 0)
                {
                    virtualObjectManager.virtualObjects = toggleObjects;
                    virtualObjectManager.InitializeObjects();
                }
            }
        }

        private void HandleActiveObjectChanged(GameObject newActive)
        {
            if (newActive != null)
            {
                targetTransform = newActive.transform;
                targetAnimator  = newActive.GetComponent<Animator>();
            }
        }

        public void UpdateActiveTargetReferences()
        {
            EnsureVirtualObjectManager();

            if (virtualObjectManager != null && virtualObjectManager.ActiveObject != null)
            {
                targetTransform = virtualObjectManager.ActiveTransform;
                targetAnimator  = virtualObjectManager.ActiveAnimator;
                return;
            }

            // フォールバック: PR_VirtualObjectManager がない場合の旧処理
            if (toggleObjects != null && toggleObjects.Length > 0 && currentActiveIndex >= 0 && currentActiveIndex < toggleObjects.Length)
            {
                GameObject activeObj = toggleObjects[currentActiveIndex];
                if (activeObj != null)
                {
                    targetTransform = activeObj.transform;
                    targetAnimator  = activeObj.GetComponent<Animator>();
                }
            }
        }

        private void Update()
        {
            // ---------------------------------------------------
            // 1. ゲーム終了 (Escapeキー)
            // ---------------------------------------------------
            if (Input.GetKeyDown(KeyCode.Escape))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                UnityEngine.Application.Quit();
#endif
            }

            // ---------------------------------------------------
            // 2. 撮影 (Enter / Returnキー) - デバッグ画像 & カメラ映像
            // ---------------------------------------------------
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                string methodPrefix = "";

                if (PCDRendererFeature.Instance != null && PCDRendererFeature.Instance.settings != null)
                {
                    var s = PCDRendererFeature.Instance.settings;
                    s.recordOcclusionDebugMap   = true;
                    s.recordPixelTagMap         = true;
                    s.recordIntegratedDepthMap  = true;
                    s.recordNeighborhoodMap     = true;
                    s.recordNeighborCountMap    = true;
                    AppLogger.Log(this, "[PR_AnimationController] オクルージョン関連DebugMapの出力をリクエストしました");

                    bool isTag    = s.enableTagBasedOptimization;
                    bool isDensity= s.enableTypeAwareDensity;
                    bool isFade   = s.enableSoftOcclusionFade;
                    bool isHole   = s.holeFillingMethod != PCD_HoleFillingMethod.None;
                    if      ( isTag &&  isDensity &&  isFade &&  isHole) methodPrefix = "Proposal";
                    else if (!isTag && !isDensity && !isFade && !isHole) methodPrefix = "Traditional";
                    else methodPrefix = $"Ablation_T{(isTag?"1":"0")}_D{(isDensity?"1":"0")}_F{(isFade?"1":"0")}_H{(isHole?"1":"0")}";
                }

                CameraCapture cc = cameraCapture ?? FindFirstObjectByType<CameraCapture>();
                if (cc != null)
                {
                    cc.Capture(methodPrefix);
                }
                else
                {
                    AppLogger.LogWarning(this, "[PR_AnimationController] CameraCaptureが設定・発見されなかったため、カメラ映像の保存はスキップされました。");
                }
            }

            // ---------------------------------------------------
            // 3. オブジェクト切り替え (Tabキー)
            // ---------------------------------------------------
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                if (virtualObjectManager != null)
                {
                    if (!virtualObjectManager.allowTabSwitch)
                    {
                        virtualObjectManager.SwitchNext();
                    }
                }
                else if (toggleObjects != null && toggleObjects.Length > 0)
                {
                    if (toggleObjects[currentActiveIndex] != null) toggleObjects[currentActiveIndex].SetActive(false);
                    currentActiveIndex = (currentActiveIndex + 1) % toggleObjects.Length;
                    if (toggleObjects[currentActiveIndex] != null) toggleObjects[currentActiveIndex].SetActive(true);
                    UpdateActiveTargetReferences();
                    AppLogger.Log(this, $"[PR_AnimationController] オブジェクトのActiveを {toggleObjects[currentActiveIndex]?.name} ({currentActiveIndex}番目) に切り替えました。");
                }
            }

            // ---------------------------------------------------
            // 4. アニメーション一時停止 / 再開 (Spaceキー)
            // ---------------------------------------------------
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (targetAnimator != null)
                {
                    targetAnimator.speed = (targetAnimator.speed > 0f) ? 0f : 1f;
                    AppLogger.Log(this, $"[PR_AnimationController] アニメーション: {(targetAnimator.speed > 0f ? "再生" : "停止")}");
                }
                else
                {
                    AppLogger.LogWarning(this, "[PR_AnimationController] 現在アクティブなオブジェクトにAnimatorがアタッチされていません。");
                }
            }

            // ---------------------------------------------------
            // 5. 対象オブジェクトの移動 (W/A/S/D / Q/E / 方向キー)
            // ---------------------------------------------------
            if (targetTransform != null)
            {
                Vector3 move = Vector3.zero;
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))    move += Vector3.forward;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))  move += Vector3.back;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))  move += Vector3.left;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += Vector3.right;
                if (Input.GetKey(KeyCode.E)) move += Vector3.up;
                if (Input.GetKey(KeyCode.Q)) move += Vector3.down;

                if (move != Vector3.zero)
                {
                    targetTransform.Translate(move.normalized * (moveSpeed * Time.deltaTime), Space.World);
                }

                // 6. 視点(カメラ)への向き追従 (Fキーで切り替え)
                if (Input.GetKeyDown(KeyCode.F))
                {
                    lookAtCamera = !lookAtCamera;
                    AppLogger.Log(this, $"[PR_AnimationController] 視点追従: {(lookAtCamera ? "ON" : "OFF")}");
                }

                if (lookAtCamera && Camera.main != null)
                {
                    Vector3 dir = Camera.main.transform.position - targetTransform.position;
                    dir.y = 0;
                    if (dir != Vector3.zero)
                    {
                        Quaternion targetRot = Quaternion.LookRotation(dir);
                        targetTransform.rotation = Quaternion.Slerp(targetTransform.rotation, targetRot, lookAtSpeed * Time.deltaTime);
                    }
                }
            }
        }
    }
}
