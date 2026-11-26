#if UNITY_EDITOR
#nullable enable
using System.Linq;
using UnityEditor;
using UnityEngine;
using Core.Logging;
using Features.Haptics.Debug;
using Features.Haptics.Processors;

namespace Features.Haptics.Editor
{
    /// <summary>
    /// 単焦点デバッグ照射における Scene ビュー上のハンドル操作、ターゲット生成・自動配置、
    /// および実空間正解位置からの差分オフセット算出を担当するエディタアクションクラス。
    /// </summary>
    public static class HAP_DebugFocusSceneActions
    {
        private const string DebugTargetObjectName = "AUTD_DebugFocusTarget";

        /// <summary>
        /// Scene ビュー上に単焦点の位置操作ハンドル (PositionHandle) を描画し、ドラッグによる座標編集を反映します。
        /// </summary>
        public static void DrawSceneMoveHandle(HAP_EditorContext ctx)
        {
            var cfg = ctx.Controller.debugFocusConfig;
            if (cfg == null || !cfg.showFocusGizmo) return;

            if (cfg.targetTransform != null)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 movedPos = Handles.PositionHandle(cfg.targetTransform.position, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(cfg.targetTransform, "Move AUTD Debug Focus Target");
                    cfg.targetTransform.position = movedPos;
                    EditorUtility.SetDirty(cfg.targetTransform);
                }
            }
            else if (ctx.DbgFocusPositionProp != null)
            {
                ctx.UpdateAll();
                Vector3 rawPos = ctx.DbgFocusPositionProp.vector3Value;

                EditorGUI.BeginChangeCheck();
                Vector3 movedPos = Handles.PositionHandle(rawPos, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    ctx.DbgFocusPositionProp.vector3Value = movedPos;
                    ctx.ApplyAll();
                    if (Application.isPlaying && HAP_DebugFocusPlayStatePersistence.PreservePlayChanges)
                    {
                        HAP_DebugFocusPlayStatePersistence.SavePlayState(ctx.Controller);
                    }
                }
            }
        }

        /// <summary>
        /// デバッグターゲット用 GameObject を選択または新規生成して紐づけます。
        /// </summary>
        public static void CreateOrSelectTargetObject(HAP_EditorContext ctx)
        {
            var cfg = ctx.Controller.debugFocusConfig;
            if (cfg.targetTransform != null)
            {
                Selection.activeTransform = cfg.targetTransform;
                return;
            }

            var existing = GameObject.Find(DebugTargetObjectName);
            if (existing == null)
            {
                existing = new GameObject(DebugTargetObjectName);
                Undo.RegisterCreatedObjectUndo(existing, "Create AUTD Debug Focus Target");
                existing.transform.position = cfg.focusPosition;
            }

            ctx.DbgTargetTransformProp!.objectReferenceValue = existing.transform;
            ctx.ApplyAll();
            Selection.activeGameObject = existing;
        }

        /// <summary>
        /// 有効な AUTD3Device 群の平均照射方向前方 20cm に単焦点位置を配置します。
        /// </summary>
        public static void PlaceAtArrayFront(HAP_EditorContext ctx)
        {
            var devices = Object.FindObjectsByType<AUTD3Device>(FindObjectsSortMode.None)
                .Where(d => d != null && d.gameObject.activeInHierarchy)
                .OrderBy(d => d.ID)
                .ToArray();

            Vector3 frontPos = HAP_DebugFocusProcessor.ComputeDefaultArrayFrontPosition(devices, 0.20f);
            SetFocusWorldPosition(ctx, frontPos);
        }

        /// <summary>
        /// 現在アクティブなオブジェクト／HCD ターゲット位置に単焦点位置を移動します。
        /// </summary>
        public static void PlaceAtActiveTarget(HAP_EditorContext ctx)
        {
            Vector3? resolvedPos = TryResolveActiveTargetPosition(ctx.Controller);
            if (resolvedPos.HasValue)
            {
                SetFocusWorldPosition(ctx, resolvedPos.Value);
            }
            else
            {
                AppLogger.LogWarning(ctx.Controller, HAP_LogTriggers.TagCalibration,
                    "[DebugFocus] アクティブターゲットの位置を自動特定できませんでした。手動で位置を設定してください。");
            }
        }

        /// <summary>
        /// 実空間の正解位置と現在照射座標の差分から、TransformLoader.offset を自動計算・更新します。
        /// </summary>
        public static void ApplyDifferenceCalibration(HAP_EditorContext ctx)
        {
            var ctrl = ctx.Controller;
            var cfg = ctrl.debugFocusConfig;
            if (cfg.referenceTrueTarget == null || ctrl.transformLoader == null) return;

            Vector3 truePos = cfg.referenceTrueTarget.position;
            Vector3 newOffset = HAP_DebugFocusProcessor.CalculateCalibratedOffset(cfg, ctrl.transformLoader.offset, truePos);

            if (ctx.TlOffsetProp != null)
            {
                ctx.TlOffsetProp.vector3Value = newOffset;
            }
            else
            {
                Undo.RecordObject(ctrl.transformLoader, "Calibrate AUTD Offset");
                ctrl.transformLoader.offset = newOffset;
                EditorUtility.SetDirty(ctrl.transformLoader);
            }

            // オフセット適用時は基準位置を正解位置へ揃えて二重シフトを防止する
            if (cfg.applyCalibrationOffset)
            {
                SetFocusWorldPosition(ctx, truePos);
            }

            AppLogger.Log(ctrl, HAP_LogTriggers.TagCalibration,
                $"[DebugFocus] Calibrated Offset: trueTarget={truePos:F4}, newOffset={newOffset:F4}");
        }

        private static Vector3? TryResolveActiveTargetPosition(HAP_AUTDHapticsController ctrl)
        {
            var objCtrl = ctrl.objectHapticsController;
            if (objCtrl != null) return objCtrl.transform.position;

            if (ctrl.hcdPipeline != null && ctrl.hcdPipeline.distanceProcessor != null)
            {
                var dp = ctrl.hcdPipeline.distanceProcessor;
                if (dp.targetObject != null) return dp.targetObject.position;
                if (dp.targetSkinnedMeshes != null && dp.targetSkinnedMeshes.Length > 0 && dp.targetSkinnedMeshes[0] != null)
                {
                    return dp.targetSkinnedMeshes[0].bounds.center;
                }
                if (dp.targetMeshFilters != null && dp.targetMeshFilters.Length > 0 && dp.targetMeshFilters[0] != null)
                {
                    return dp.targetMeshFilters[0].transform.position;
                }
            }

            return null;
        }

        private static void SetFocusWorldPosition(HAP_EditorContext ctx, Vector3 worldPos)
        {
            var cfg = ctx.Controller.debugFocusConfig;
            if (cfg.targetTransform != null)
            {
                Undo.RecordObject(cfg.targetTransform, "Set AUTD Debug Focus Position");
                cfg.targetTransform.position = worldPos;
                EditorUtility.SetDirty(cfg.targetTransform);
            }
            else if (ctx.DbgFocusPositionProp != null)
            {
                ctx.DbgFocusPositionProp.vector3Value = worldPos;
            }
        }
    }
}
#endif

