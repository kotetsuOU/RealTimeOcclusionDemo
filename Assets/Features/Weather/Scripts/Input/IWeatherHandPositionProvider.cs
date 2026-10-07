using UnityEngine;

namespace Features.Weather
{
    /// <summary>
    /// 天候操作（雨トグル・落雷）に使用する手またはジェスチャー追跡座標を提供するプロバイダー。
    /// HCD点群クラスタ、VRコントローラー、Ultraleap、あるいはデバッグ用追跡オブジェクトを統一的に扱います。
    /// </summary>
    public interface IWeatherHandPositionProvider
    {
        /// <summary>
        /// 現在手が有効に追跡されているか。
        /// </summary>
        bool IsTracked { get; }

        /// <summary>
        /// 現在の手のワールド座標を取得します。
        /// </summary>
        bool TryGetHandPosition(out Vector3 worldPosition);
    }
}
