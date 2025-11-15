using UnityEngine;

/// <summary>
/// FocusObject が RsDeviceController によって定義された検知領域の境界に達した際、
/// 境界から内側(bounceMargin)の地点まで移動させ、静止させます。
/// このスクリプトは FocusObject の Transform を直接制御します。
/// </summary>
public class RSPC_FocusBoundsController : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("検知領域の境界(AABB)を定義する RsDeviceController")]
    [SerializeField]
    private RsDeviceController rsDeviceController;

    [Tooltip("制御対象の焦点オブジェクト（このスクリプトがアタッチされているオブジェクト）")]
    [SerializeField]
    private Transform focusTransform;

    [Header("Bounce Settings")]
    [Tooltip("境界から内側に戻る距離 (メートル)")]
    [SerializeField]
    private float bounceMargin = 0.1f;

    [Tooltip("境界から戻る際の移動速度 (メートル/秒)")]
    [SerializeField]
    private float bounceSpeed = 1.0f;

    // --- 内部状態 ---
    private Vector3 _targetPosition;
    private Vector3 _safeMin;
    private Vector3 _safeMax;
    private bool _isReturningToSafeZone = false;

    // (デバッグ用) ギズモ描画用の計算済み境界
    private Vector3 _debugGlobalMin;
    private Vector3 _debugGlobalMax;

    void Start()
    {
        if (rsDeviceController == null)
        {
            UnityEngine.Debug.LogError("RSPC_FocusBoundsController: 'Rs Device Controller' が設定されていません。", this);
            this.enabled = false;
            return;
        }
        if (focusTransform == null)
        {
            focusTransform = this.transform;
            UnityEngine.Debug.LogWarning("RSPC_FocusBoundsController: 'Focus Transform' が未設定のため、自身を対象にします。", this);
        }

        // 最初の目標位置は現在の位置
        _targetPosition = focusTransform.position;
    }

    void Update()
    {
        if (rsDeviceController == null) return;

        // 1. RsDeviceController から境界値を計算
        // ユーザー指定の計算式を適用
        float frameMargin = rsDeviceController.FrameWidth; // (frameWidth + extraLength) 
        Vector3 marginVector = Vector3.one * frameMargin;

        Vector3 globalMin = marginVector;
        Vector3 globalMax = rsDeviceController.RealSenseScanRange - marginVector;

        // (ギズモ描画用に保存)
        _debugGlobalMin = globalMin;
        _debugGlobalMax = globalMax;

        // 2. 「安全領域」を計算 (bounceMargin を考慮)
        _safeMin = globalMin + Vector3.one * bounceMargin;
        _safeMax = globalMax - Vector3.one * bounceMargin;

        // 3. 現在の位置を取得
        Vector3 currentPosition = focusTransform.position;

        // 4. もし今、安全領域に戻る途中でないなら、現在の位置が領域外かチェック
        if (!_isReturningToSafeZone)
        {
            // 現在の位置が「安全領域」の外に出ているかチェック
            bool isOutside =
                currentPosition.x < _safeMin.x || currentPosition.x > _safeMax.x ||
                currentPosition.y < _safeMin.y || currentPosition.y > _safeMax.y ||
                currentPosition.z < _safeMin.z || currentPosition.z > _safeMax.z;

            if (isOutside)
            {
                // 安全領域の外に出た場合、「跳ね返り」を開始
                // 目標位置を、安全領域内にクランプ（丸め込み）した位置に設定
                _targetPosition.x = Mathf.Clamp(currentPosition.x, _safeMin.x, _safeMax.x);
                _targetPosition.y = Mathf.Clamp(currentPosition.y, _safeMin.y, _safeMax.y);
                _targetPosition.z = Mathf.Clamp(currentPosition.z, _safeMin.z, _safeMax.z);

                _isReturningToSafeZone = true;
                UnityEngine.Debug.Log($"境界外を検出。安全領域 ({_targetPosition:F3}) に戻ります。");
            }
            else
            {
                // 領域内にいる場合、目標位置は現在の位置（＝静止）
                _targetPosition = currentPosition;
            }
        }

        // 5. 目標位置 ( _targetPosition ) に向かって移動
        Vector3 newPos = Vector3.MoveTowards(
            currentPosition,
            _targetPosition,
            bounceSpeed * Time.deltaTime
        );

        // 6. 新しい位置を適用
        focusTransform.position = newPos;

        // 7. もし目標位置に到達したら、「戻る」状態を解除
        if (_isReturningToSafeZone && newPos == _targetPosition)
        {
            _isReturningToSafeZone = false;
            UnityEngine.Debug.Log("安全領域に到達。静止します。");
        }
    }

    // (デバッグ用) 安全領域をギズモで描画
    void OnDrawGizmosSelected()
    {
        // Start() 前や Controller がない場合は実行しない
        if (rsDeviceController == null) return;

        // 安全領域
        Vector3 safeCenter = (_safeMin + _safeMax) / 2f;
        Vector3 safeSize = _safeMax - _safeMin;
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(safeCenter, safeSize);

        // 検知領域 (全体)
        Vector3 globalCenter = (_debugGlobalMin + _debugGlobalMax) / 2f;
        Vector3 globalSize = _debugGlobalMax - _debugGlobalMin;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(globalCenter, globalSize);
    }
}