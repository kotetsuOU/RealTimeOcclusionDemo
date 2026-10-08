#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Features.HapticsCollision.Editor
{
    /// <summary>
    /// HCD_PipelineEditor と各専任 Drawer 間で共有される SerializedProperty および状態コンテキスト。
    /// プロパティの事前キャッシュを一元管理し、パフォーマンス向上とタイポ防止を図ります。
    /// </summary>
    public class HCD_EditorContext
    {
        public HCD_Pipeline Pipeline { get; }
        public SerializedObject SerializedObject { get; }

        public SerializedProperty ScriptProp { get; }

        // Distance Processor
        public SerializedProperty DpProp { get; }
        public SerializedProperty DetModeProp { get; }
        public SerializedProperty DistModeProp { get; }
        public SerializedProperty ViewCamProp { get; }
        public SerializedProperty MeshSurfProp { get; }
        public SerializedProperty MeshBackProp { get; }
        public SerializedProperty VisSurfProp { get; }
        public SerializedProperty VisBackProp { get; }
        public SerializedProperty OccSurfProp { get; }
        public SerializedProperty OccBackProp { get; }
        public SerializedProperty TargetObjProp { get; }
        public SerializedProperty TargetTransformsProp { get; }
        public SerializedProperty TargetSkinnedProp { get; }
        public SerializedProperty TargetMeshFilterProp { get; }
        public SerializedProperty FootPlaneTargetProp { get; }
        public SerializedProperty FootPlaneBoundsMinProp { get; }
        public SerializedProperty FootPlaneBoundsMaxProp { get; }
        public SerializedProperty FootPlaneRefYProp { get; }
        public SerializedProperty FootPlaneDepthThresholdProp { get; }
        public SerializedProperty FootPlaneUpperMarginProp { get; }
        public SerializedProperty FootPlaneNormalProp { get; }
        public SerializedProperty DistComputeShaderProp { get; }

        // Spatial Clustering Processor
        public SerializedProperty ScpProp { get; }
        public SerializedProperty MaxClustersProp { get; }
        public SerializedProperty CellSizeProp { get; }
        public SerializedProperty AggModeProp { get; }
        public SerializedProperty PosSourceProp { get; }
        public SerializedProperty DistPowerProp { get; }
        public SerializedProperty PrecisionModeProp { get; }
        public SerializedProperty ClusterComputeShaderProp { get; }

        // Cluster Tracker
        public SerializedProperty CtProp { get; }
        public SerializedProperty MatchRadiusProp { get; }
        public SerializedProperty MaxMissingFramesProp { get; }
        public SerializedProperty ForceMaxCountProp { get; }
        public SerializedProperty ForceMinCountProp { get; }
        public SerializedProperty ForceSmoothingFactorProp { get; }
        public SerializedProperty VelocitySmoothingFactorProp { get; }

        public HCD_EditorContext(HCD_Pipeline pipeline, SerializedObject so)
        {
            Pipeline = pipeline;
            SerializedObject = so;

            ScriptProp = so.FindProperty("m_Script");

            // 1. Distance Processor
            DpProp = so.FindProperty("distanceProcessor");
            if (DpProp != null)
            {
                DetModeProp = DpProp.FindPropertyRelative("detectionMode");
                DistModeProp = DpProp.FindPropertyRelative("distanceMode");
                ViewCamProp = DpProp.FindPropertyRelative("viewCamera");

                MeshSurfProp = DpProp.FindPropertyRelative("meshSurfaceDistanceThreshold");
                MeshBackProp = DpProp.FindPropertyRelative("meshBackfaceDistanceThreshold");

                VisSurfProp = DpProp.FindPropertyRelative("visibleSurfaceDistanceThreshold");
                VisBackProp = DpProp.FindPropertyRelative("visibleBackfaceDistanceThreshold");
                OccSurfProp = DpProp.FindPropertyRelative("occludedSurfaceDistanceThreshold");
                OccBackProp = DpProp.FindPropertyRelative("occludedBackfaceDistanceThreshold");

                TargetObjProp = DpProp.FindPropertyRelative("targetObject");
                TargetTransformsProp = DpProp.FindPropertyRelative("targetTransforms");
                TargetSkinnedProp = DpProp.FindPropertyRelative("targetSkinnedMeshes");
                TargetMeshFilterProp = DpProp.FindPropertyRelative("targetMeshFilters");

                FootPlaneTargetProp = DpProp.FindPropertyRelative("footPlaneTarget");
                FootPlaneBoundsMinProp = DpProp.FindPropertyRelative("footPlaneBoundsMin");
                FootPlaneBoundsMaxProp = DpProp.FindPropertyRelative("footPlaneBoundsMax");
                FootPlaneRefYProp = DpProp.FindPropertyRelative("footPlaneRefY");
                FootPlaneDepthThresholdProp = DpProp.FindPropertyRelative("footPlaneDepthThreshold");
                FootPlaneUpperMarginProp = DpProp.FindPropertyRelative("footPlaneUpperMargin");
                FootPlaneNormalProp = DpProp.FindPropertyRelative("footPlaneNormal");

                DistComputeShaderProp = DpProp.FindPropertyRelative("collisionComputeShader");
            }

            // 2. Spatial Clustering
            ScpProp = so.FindProperty("clusteringProcessor");
            if (ScpProp != null)
            {
                MaxClustersProp = ScpProp.FindPropertyRelative("maxClusters");
                CellSizeProp = ScpProp.FindPropertyRelative("cellSize");
                AggModeProp = ScpProp.FindPropertyRelative("aggregationMode");
                PosSourceProp = ScpProp.FindPropertyRelative("positionSource");
                DistPowerProp = ScpProp.FindPropertyRelative("distanceWeightPower");
                PrecisionModeProp = ScpProp.FindPropertyRelative("precisionMode");
                ClusterComputeShaderProp = ScpProp.FindPropertyRelative("clusteringComputeShader");
            }

            // 3. Cluster Tracker
            CtProp = so.FindProperty("clusterTracker");
            if (CtProp != null)
            {
                MatchRadiusProp = CtProp.FindPropertyRelative("matchRadius");
                MaxMissingFramesProp = CtProp.FindPropertyRelative("maxMissingFrames");
                ForceMaxCountProp = CtProp.FindPropertyRelative("forceMaxCount");
                ForceMinCountProp = CtProp.FindPropertyRelative("forceMinCount");
                ForceSmoothingFactorProp = CtProp.FindPropertyRelative("forceSmoothingFactor");
                VelocitySmoothingFactorProp = CtProp.FindPropertyRelative("velocitySmoothingFactor");
            }
        }
    }
}
#endif
