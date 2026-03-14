using UnityEngine;

[RequireComponent(typeof(Camera))]
public class ViewpointTracker : MonoBehaviour
{
    [Header("Tracking Settings")]
    [Tooltip("アイトラッキングシステムから受け取るユーザーの視点オフセット座標")]
    public Vector3 eyeOffsetPosition; 
    
    [Tooltip("トラッカーの移動量とUnity空間の移動量のスケール調整")]
    public float movementScale = 1.0f;

    [Header("Viewing Frustum Settings")]
    [Tooltip("注視点（ディスプレイの面など）を向かせる場合は設定します")]
    public Transform displayCenter;

    private Camera targetCamera;
    private Vector3 initialPosition;

    void Start()
    {
        targetCamera = GetComponent<Camera>();
        initialPosition = transform.localPosition;
    }

    void Update()
    {
        // 1. 外部システム（RealSenseやアイトラッキングAPI）から最新の視点位置を取得
        UpdateTrackingData();

        // 2. カメラの位置を更新（初期位置 ＋ 視点の移動量）
        transform.localPosition = initialPosition + (eyeOffsetPosition * movementScale);

        // 3. パララックスバリアなどディスプレイ面を見る場合、必要に応じて向きを補正
        if (displayCenter != null)
        {
            transform.LookAt(displayCenter);
        }
    }

    private void UpdateTrackingData()
    {
        // TODO: ここにアイトラッキングデバイス（RealSenseなど）から
        // 最新の座標を取得し、eyeOffsetPositionを更新する処理を記述します。
        // 例: eyeOffsetPosition = RealSenseTrackingAPI.GetEyePosition();
    }
}