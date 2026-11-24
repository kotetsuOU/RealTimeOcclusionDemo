using System.Collections.Generic;
using UnityEngine;

namespace RealSense.DummyPointCloud
{
    /// <summary>
    /// ダミー点群生成の設定値を <see cref="RsDummyChangeTracker"/> 等へ共通提供するためのインターフェース。
    /// </summary>
    public interface IRsDummyPointCloudSource
    {
        List<GameObject> TargetObjects { get; }
        bool IncludeChildren { get; }
        PointDensityUnit DensityUnit { get; }
        float DensityValue { get; }
        int MaxPointLimit { get; }
        PointColorMode ColorMode { get; }
        Color SolidColor { get; }
        bool UseCameraPerspective { get; }
        Transform SimulatedCameraTransform { get; }
        int UpdateFPS { get; }
        Transform OwnerTransform { get; }
    }
}

