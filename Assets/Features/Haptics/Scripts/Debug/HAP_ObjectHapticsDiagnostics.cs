using UnityEngine;
using System.Text;
using System.Collections.Generic;
using Core.Logging;

#nullable enable

namespace Features.Haptics.Debug
{
    /// <summary>
    /// HAP_BaseObjectHapticsController 派生クラス（FoxBody, FoxFoot 等）の
    /// HCD衝突判定・クラスタ追跡・各部位の近接接触状態およびAUTD状態の診断レポート生成とログ出力を担当するヘルパークラス。
    /// コントローラー本体の神クラス化を防ぐため、診断文字列生成ロジックを分離しています。
    /// </summary>
    public static class HAP_ObjectHapticsDiagnostics
    {
        /// <summary>
        /// 指定されたオブジェクト触覚コントローラーの現在状態（AUTD, HCD, 点群, メッシュ, 各部位接触距離）を
        /// 整形された診断テキストとして組み立てて返します。
        /// </summary>
        public static string BuildReport(HAP_BaseObjectHapticsController controller)
        {
            if (controller == null) return "Controller is NULL";

            var autdController = controller.autdController;
            HCD_Pipeline? pipeline = autdController != null ? autdController.hcdPipeline : null;
            if (pipeline == null) pipeline = HCD_Pipeline.Instance ?? Object.FindAnyObjectByType<HCD_Pipeline>();

            string hcdModeStr = "None";
            string meshDetailStr = "None";
            string boundsDetailStr = "";
            int clusterCount = 0;
            int activeClusterCount = 0;

            if (pipeline != null && pipeline.distanceProcessor != null)
            {
                var dp = pipeline.distanceProcessor;
                hcdModeStr = dp.detectionMode.ToString();
                if (dp.targetSkinnedMeshes != null && dp.targetSkinnedMeshes.Length > 0)
                {
                    var names = new List<string>();
                    foreach (var m in dp.targetSkinnedMeshes) if (m != null) names.Add(m.name);
                    meshDetailStr = $"{names.Count} mesh(es) [{string.Join(", ", names)}]";
                }
                else if (dp.targetMeshFilters != null && dp.targetMeshFilters.Length > 0)
                {
                    var names = new List<string>();
                    foreach (var m in dp.targetMeshFilters) if (m != null) names.Add(m.name);
                    meshDetailStr = $"{names.Count} mesh(es) [{string.Join(", ", names)}]";
                }
                else
                {
                    meshDetailStr = "0 meshes (未設定/空)";
                }

                var b = dp.MeshBounds;
                boundsDetailStr = $"  [MeshDetail] Verts={dp.BakedVerticesCount}, Tris={dp.BakedTrianglesCount}, BoundsCenter={b.center:F3}, BoundsSize={b.size:F3}, DistanceMode={dp.distanceMode}, SurfThresh={dp.surfaceDistanceThreshold:F3}m, BackThresh={dp.backfaceDistanceThreshold:F3}m";

                var clusters = pipeline.GetTrackedClusters();
                if (clusters != null)
                {
                    clusterCount = clusters.Count;
                    foreach (var c in clusters)
                    {
                        if (c.IsAlive && c.Force > 0.01f) activeClusterCount++;
                    }
                }
            }

            int globalPointCount = RsGlobalPointCloudManager.Instance != null ? RsGlobalPointCloudManager.Instance.CurrentTotalCount : -1;

            string autdState = autdController != null
                ? $"SourceMode={autdController.sourceMode}, Connected={autdController.hardwareController?.IsConnected}, Bypass={autdController.bypassHaptics}, Intensity={autdController.focusIntensityPascal}Pa"
                : "AUTDController is NULL";

            var sb = new StringBuilder();
            sb.AppendLine($"[{controller.GetType().Name}] === 触覚診断レポート (Frame: {Time.frameCount}) ===");
            sb.AppendLine($"  [AUTD] {autdState}");
            sb.AppendLine($"  [PointCloud] RealSense GlobalPoints = {globalPointCount}");
            sb.AppendLine($"  [HCD] DetectionMode={hcdModeStr}, TargetMeshes={meshDetailStr}, TrackedClusters={clusterCount} (Active: {activeClusterCount})");
            if (!string.IsNullOrEmpty(boundsDetailStr))
            {
                sb.AppendLine(boundsDetailStr);
            }
            sb.AppendLine($"  [Settings] onlyTargetHandContact={controller.onlyTargetHandContact}, threshold={controller.handContactThreshold:F3}m, disableWhenInAir={controller.disableWhenInAir}");

            var clustersList = pipeline?.GetTrackedClusters();

            foreach (var info in controller.TargetInfos)
            {
                if (info.Transform == null) continue;

                Vector3 pos = info.Transform.position + info.Offset;
                bool active = controller.IsTargetActive(info.Transform, info.IsEnabled, info.IsTail);

                float closestDist = float.MaxValue;
                if (clustersList != null)
                {
                    foreach (var c in clustersList)
                    {
                        if (c.IsAlive)
                        {
                            float d = Vector3.Distance(c.Centroid, pos);
                            if (d < closestDist) closestDist = d;
                        }
                    }
                }

                string distStr = closestDist < 10f ? $"{closestDist:F3}m" : "N/A";
                string reason = "";
                if (!info.IsEnabled) reason = " (Toggle OFF)";
                else if (!active)
                {
                    if (controller.onlyTargetHandContact && closestDist > controller.handContactThreshold)
                    {
                        reason = $" (手の距離 {distStr} > 閾値 {controller.handContactThreshold:F3}m)";
                    }
                    else if (!info.IsTail && controller.disableWhenInAir)
                    {
                        reason = " (空中判定により除外)";
                    }
                }

                sb.AppendLine($"  • 部位 '{info.Name}': Active={active}, Pos={pos:F2}, 手との最近接距離={distStr}{reason}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 診断レポートを構築し、AppLogger 経由でコンソールへ出力します。
        /// </summary>
        public static void LogDiagnostics(HAP_BaseObjectHapticsController controller, string logTag = "HAP_ObjectHaptics")
        {
            if (controller == null) return;
            string report = BuildReport(controller);
            AppLogger.Log(controller, logTag, report);
        }
    }
}
