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
            if (PCDRendererFeature.Instance != null)
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                
                // ① オクルージョンマップの書き出しフラグをオン
                PCDRendererFeature.Instance.recordOcclusionDebugMap = true;
                
                // ② 同時にカメラの通常のスクリーンショットも撮影
                string savePath = "Assets/HandTrackingData/OcclusionMaps";
                if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);

                string cameraImagePath = Path.Combine(savePath, $"CameraView_{timestamp}.png");
                ScreenCapture.CaptureScreenshot(cameraImagePath);

                Debug.Log($"[KeyController] 撮影完了: {timestamp}\n(OcclusionMapとCameraViewが保存されました)");
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
        // 4. 手法 (提案手法 / 従来手法) の瞬時切り替え (Mキー)
        // ----------------------------------------------------
        if (Input.GetKeyDown(KeyCode.M))
        {
            if (PCDRendererFeature.Instance != null)
            {
                PCDRendererFeature.Instance.enableJointBilateralHoleFilling = !PCDRendererFeature.Instance.enableJointBilateralHoleFilling;
                string methodStr = PCDRendererFeature.Instance.enableJointBilateralHoleFilling ? "提案手法 (Proposal)" : "従来手法 (Traditional / Hole Filling OFF)";
                Debug.Log($"[KeyController] 手法切り替え: {methodStr}");
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
                    PCDRendererFeature.Instance.occlusionFadeWidth = 0.1f;
                    Debug.Log("[KeyController] FadeWidth: 0.1 (滑らかマスク)");
                }
            }
        }

        // ----------------------------------------------------
        // 6. 対象オブジェクト(狐など)の移動 (W,A,S,D / Q,E)
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
