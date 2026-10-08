using UnityEngine;

/// <summary>
/// 単一焦点の位置と目標音圧を表す SDK 非依存の値型。
/// 焦点生成層はこの型のみを扱い、AUTD3 SDK 固有型への変換は送信直前の Backend 側で行う。
/// </summary>
public readonly struct HAP_FocusPoint
{
    /// <summary>焦点位置（オフセット適用済みの、SDK へそのまま渡す座標）。</summary>
    public readonly Vector3 Position;

    /// <summary>目標音圧（Pa）。</summary>
    public readonly float Pascal;

    public HAP_FocusPoint(Vector3 position, float pascal)
    {
        Position = position;
        Pascal = pascal;
    }
}
