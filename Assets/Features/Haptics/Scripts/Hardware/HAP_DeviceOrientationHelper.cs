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
        /// autd3-sdk (v0.14.1 / Current SDK) 用のデバイス回転を取得します。
        /// Coords.cs により座標系が変換されるため、ローカル +X/+Y に振動子を配置しローカル -Z へ照射する
        /// Unity 上の AUTD3 プレハブ定義と 1 対 1 で一致する生の Transform 回転をそのまま返します。
        /// </summary>
        public static Quaternion GetCurrentSdkRotation(AUTD3Device device)
        {
            if (device == null) return Quaternion.identity;
            return device.transform.rotation;
        }

        /// <summary>
        /// 旧 SDK (AUTD3Sharp / Legacy SDK) 用にローカル Y 軸回りに 180 度反転した回転を取得します。
        /// 旧 SDK のローカル +Z 照射基準を AUTD3 プレハブの照射面へ合わせるための補正です。
        /// </summary>
        public static Quaternion GetAdjustedRotation(AUTD3Device device)
        {
            if (device == null) return Quaternion.identity;
            // 旧 SDK ではローカル Y 軸回りに 180 度反転して照射面を合わせる
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
