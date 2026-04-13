using UnityEngine;
using System.IO;
using System;

public class KeyboardController : MonoBehaviour
{
    [Header("Control Targets")]
    [Tooltip("アニメーションの再生/一時停止を切り替えるAnimator (例: キツネ等)")]
    public Animator targetAnimator;

    [Tooltip("キーボード操作で移動させる対象のオブジェクト (例: キツネ等)")]
    public Transform targetTransform;

    [Tooltip("カメラキャプチャ用スクリプト (ViewPointのカメラ映像保存用)")]
    public CameraCapture cameraCapture;

    [Tooltip("マテリアル切り替え用コントローラー")]
    public RsMaterialController materialController;

    [Tooltip("移動速度")]
    public float moveSpeed = 1.0f;

    void Update()
    {
        // ----------------------------------------------------
        // 1. QuitGame の統合 (Escapeキー)
        // ----------------------------------------------------
        if (Input.GetKeyDown(KeyCode.Escape))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            UnityEngine.Application.Quit();
#endif
        }

        // ----------------------------------------------------
        // 2. 撮影 (Enter / Returnキー)
        // デバッグ画像と現在のViewPointカメラ映像を同時保存！
        // ----------------------------------------------------
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            string methodPrefix = "";

            // ① オクルージョンマップの書き出しフラグをオン
            if (PCDRendererFeature.Instance != null)
            {
                PCDRendererFeature.Instance.recordOcclusionDebugMap = true;
                Debug.Log("[KeyboardController] オクルージョンマップの出力をリクエストしました");

                bool isTag = PCDRendererFeature.Instance.enableTagBasedOptimization;
                bool isDensity = PCDRendererFeature.Instance.enableTypeAwareDensity;
                bool isFade = PCDRendererFeature.Instance.enableSoftOcclusionFade;
                bool isHoleFill = PCDRendererFeature.Instance.enableJointBilateralHoleFilling;

                if (isTag && isDensity && isFade && isHoleFill) methodPrefix = "Proposal";
                else if (!isTag && !isDensity && !isFade && !isHoleFill) methodPrefix = "Traditional";
                else methodPrefix = $"Ablation_T{(isTag?"1":"0")}_D{(isDensity?"1":"0")}_F{(isFade?"1":"0")}_H{(isHoleFill?"1":"0")}";
            }

            // ② 同時にCameraCaptureのCapture()を実行してViewPointカメラ映像を保存
            if (cameraCapture != null)
            {
                cameraCapture.Capture(methodPrefix);
            }
            else
            {
                // アタッチし忘れていた場合のフォールバック（シーン内から検索）
                CameraCapture cc = FindFirstObjectByType<CameraCapture>();
                if (cc != null)
                {
                    cc.Capture(methodPrefix);
                }
                else
                {
                    Debug.LogWarning("[KeyboardController] CameraCaptureが設定・発見されなかったため、カメラ映像の保存はスキップされました。");
                }
            }
        }

        // ----------------------------------------------------
        // 3. アニメーションの一時停止 / 再開 (Spaceキー)
        // ----------------------------------------------------
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (targetAnimator != null)
            {
                // Animatorの再生速度を0と1でスイッチする
                targetAnimator.speed = (targetAnimator.speed > 0f) ? 0f : 1f;
                Debug.Log($"[KeyController] アニメーション: {(targetAnimator.speed > 0f ? "再生" : "停止")}");
            }
            else
            {
                Debug.LogWarning("[KeyController] Inspectorで targetAnimator が設定されていません。");
            }
        }

        // ----------------------------------------------------
        // 4. 手法 (提案手法 / 従来手法) の瞬時切り替え (Mキー) - Ablation Study
        // ----------------------------------------------------
        if (Input.GetKeyDown(KeyCode.M))
        {
            if (PCDRendererFeature.Instance != null)
            {
                // 全ての提案機能のON/OFFを一括でトグルする
                bool isAnyOn = PCDRendererFeature.Instance.enableTagBasedOptimization || 
                               PCDRendererFeature.Instance.enableTypeAwareDensity || 
                               PCDRendererFeature.Instance.enableSoftOcclusionFade || 
                               PCDRendererFeature.Instance.enableJointBilateralHoleFilling;

                bool toggleTo = !isAnyOn; // 1つでもONならすべてOFFにする

                PCDRendererFeature.Instance.enableTagBasedOptimization = toggleTo;
                PCDRendererFeature.Instance.enableTypeAwareDensity = toggleTo;
                PCDRendererFeature.Instance.enableSoftOcclusionFade = toggleTo;
                PCDRendererFeature.Instance.enableJointBilateralHoleFilling = toggleTo;

                string methodStr = toggleTo ? "提案手法 (全てON)" : "従来手法 (全てOFF)";
                Debug.Log($"[KeyController] 手法切り替え: {methodStr}");
            }
        }

        // ----------------------------------------------------
        // 5. 各提案機能(Ablation)の個別切り替え (Alpha1, 2, 3, 4)
        // ----------------------------------------------------
        if (PCDRendererFeature.Instance != null)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                PCDRendererFeature.Instance.enableTagBasedOptimization = !PCDRendererFeature.Instance.enableTagBasedOptimization;
                Debug.Log($"[KeyController] ① タグスキップ最適化: {(PCDRendererFeature.Instance.enableTagBasedOptimization ? "ON" : "OFF")}");
            }
            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                PCDRendererFeature.Instance.enableTypeAwareDensity = !PCDRendererFeature.Instance.enableTypeAwareDensity;
                Debug.Log($"[KeyController] ② 密度計算補正: {(PCDRendererFeature.Instance.enableTypeAwareDensity ? "ON" : "OFF")}");
            }
            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                PCDRendererFeature.Instance.enableSoftOcclusionFade = !PCDRendererFeature.Instance.enableSoftOcclusionFade;
                Debug.Log($"[KeyController] ③ ソフトフェード: {(PCDRendererFeature.Instance.enableSoftOcclusionFade ? "ON" : "OFF")}");
            }
            if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                PCDRendererFeature.Instance.enableJointBilateralHoleFilling = !PCDRendererFeature.Instance.enableJointBilateralHoleFilling;
                Debug.Log($"[KeyController] ④ 穴埋め(Hole Filling): {(PCDRendererFeature.Instance.enableJointBilateralHoleFilling ? "ON" : "OFF")}");
            }
        }

        // ----------------------------------------------------
        // 5. Fade Width設定切り替え (Tキー)
        // ----------------------------------------------------
        if (Input.GetKeyDown(KeyCode.T))
        {
            if (PCDRendererFeature.Instance != null)
            {
                if (PCDRendererFeature.Instance.occlusionFadeWidth > 0.05f)
                {
                    PCDRendererFeature.Instance.occlusionFadeWidth = 0.0f;
                    Debug.Log("[KeyController] FadeWidth: 0.0 (くっきりマスク)");
                }
                else
                {
                    PCDRendererFeature.Instance.occlusionFadeWidth = 0.2f;
                    Debug.Log("[KeyController] FadeWidth: 0.2 (滑らかマスク)");
                }
            }
        }

        // ----------------------------------------------------
        // 6. カラーモードの切り替え (Cキー)
        // ----------------------------------------------------
        if (Input.GetKeyDown(KeyCode.C))
        {
            if (materialController != null)
            {
                // Enumの値をローテーションさせる
                PointCloudColorMode nextMode = (PointCloudColorMode)(((int)materialController.colorMode + 1) % Enum.GetValues(typeof(PointCloudColorMode)).Length);
                materialController.ChangeColorMode(nextMode);
                Debug.Log($"[KeyController] カラーモード切り替え: {nextMode}");
            }
            else
            {
                Debug.LogWarning("[KeyController] materialControllerが設定されていません。");
            }
        }

        // ----------------------------------------------------
        // 7. 対象オブジェクト(狐など)の移動 (W,A,S,D / Q,E)
        // ----------------------------------------------------
        if (targetTransform != null)
        {
            Vector3 move = Vector3.zero;

            // X軸, Z軸移動: WASD または 十字キー
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move += Vector3.forward;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move += Vector3.back;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move += Vector3.left;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += Vector3.right;

            // Y軸移動: E (上) / Q (下)
            if (Input.GetKey(KeyCode.E)) move += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) move += Vector3.down;

            if (move != Vector3.zero)
            {
                // カメラの向き等に関係なく、ワールド空間に対して自由に移動させる
                targetTransform.Translate(move.normalized * (moveSpeed * Time.deltaTime), Space.World);
            }
        }
    }
}
