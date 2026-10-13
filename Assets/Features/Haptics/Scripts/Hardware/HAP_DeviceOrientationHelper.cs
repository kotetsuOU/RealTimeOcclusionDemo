using UnityEngine;

#nullable enable

namespace Features.Haptics.Hardware
{
    /// <summary>
    /// シーン上の AUTD3Device の Transform から、実機の物理照射面に整合させた
    /// 調整後回転 (Quaternion) および照射方向 (forward) を算出する純粋ヘルパークラス。
    /// プレハブ・メッシュの幾何定義に基づき、全デバイス一貫してローカル Y 軸回りに 180 度反転補正を行います。
    /// </summary>
    public static class HAP_DeviceOrientationHelper
    {
        /// <summary>
        /// AUTD3Device の Transform から、物理照射面に整合させた調整後の回転を取得します。
        /// メッシュ定義と超音波照射面の整合のため、全デバイス一律にローカル Y 軸回りに 180 度反転します。
        /// </summary>
        public static Quaternion GetAdjustedRotation(AUTD3Device device)
        {
            if (device == null) return Quaternion.identity;
            // 全デバイス一貫してローカル Y 軸回りに 180 度反転（照射面を反転）
            return device.transform.rotation * Quaternion.Euler(0f, 180f, 0f);
        }

        /// <summary>
        /// 調整後の照射方向（forward ベクトル）を取得します。
        /// Directional Grouping の法線判定などで使用します。
        /// </summary>
        public static Vector3 GetAdjustedForward(AUTD3Device device)
        {
            if (device == null) return Vector3.forward;
            return GetAdjustedRotation(device) * Vector3.forward;
        }
    }
}
