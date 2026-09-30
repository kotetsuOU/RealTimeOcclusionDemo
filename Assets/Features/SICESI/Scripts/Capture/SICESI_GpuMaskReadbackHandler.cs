using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Core.Logging;

namespace SICESI
{
    /// <summary>
    /// PCD レンダーパイプラインの NeighborCountMap (RT) から AsyncGPUReadback を実行し、
    /// ピクセルごとのセクター占有マスク・未占有マスクデータを CSV / RAW / PNG としてエクスポートするハンドラークラス。
    /// </summary>
    public class SICESI_GpuMaskReadbackHandler
    {
        private readonly HashSet<int> _activePatterns = new HashSet<int>();

        public IReadOnlyCollection<int> ActivePatterns => _activePatterns;

        public void ClearActivePatterns()
        {
            _activePatterns.Clear();
        }

        /// <summary>
        /// 現在の RT から非同期リードバックを行い、指定ディレクトリへセクターマスクデータを出力します。
        /// </summary>
        public IEnumerator ReadbackAndSaveSingleEye(string saveDir, string filePrefix)
        {
            if (PCDRendererFeature.Instance == null || PCDRendererFeature.Instance.CurrentResources == null)
            {
                yield break;
            }

            var neighborCountRt = PCDRendererFeature.Instance.CurrentResources.NeighborCountMap;
            if (neighborCountRt == null || neighborCountRt.rt == null)
            {
                yield break;
            }

            var rt = neighborCountRt.rt;
            int width = rt.width;
            int height = rt.height;

            bool readbackDone = false;
            uint[] readbackData = null;

            AsyncGPUReadback.Request(rt, 0, request =>
            {
                if (!request.hasError)
                {
                    readbackData = request.GetData<uint>().ToArray();
                }
                readbackDone = true;
            });

            float timeout = Time.realtimeSinceStartup + 2.0f;
            while (!readbackDone && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            if (readbackData != null)
            {
                for (int i = 0; i < readbackData.Length; i++)
                {
                    uint val = readbackData[i];
                    if (((val >> 12) & 1u) != 0u) // isEvaluated == 1
                    {
                        _activePatterns.Add((int)(val & 0xFFu));
                    }
                }

                PCDOcclusionDebugExporter.ExportSectorMaskData(readbackData, width, height, saveDir, filePrefix);
                AppLogger.Log("SICESI", $"SectorMask 保存完了 [{filePrefix}] (w:{width}, h:{height}): {saveDir}");
            }
        }

        /// <summary>
        /// 左右カメラを個別に有効化してレンダリングさせ、リードバックと保存を実行します。
        /// </summary>
        public IEnumerator CaptureAndExportStereoEyes(
            Camera leftCam,
            Camera rightCam,
            string targetDensityDir,
            string densityStr,
            Action<Camera, string> saveCameraViewAction)
        {
            bool leftOriginalEnabled = leftCam != null && leftCam.enabled;
            bool rightOriginalEnabled = rightCam != null && rightCam.enabled;

            try
            {
                if (leftCam != null)
                {
                    if (rightCam != null) rightCam.enabled = false;
                    leftCam.enabled = true;

                    yield return null;
                    yield return new WaitForEndOfFrame();

                    string leftDir = Path.Combine(targetDensityDir, "Left");
                    Directory.CreateDirectory(leftDir);

                    string leftImgPath = Path.Combine(leftDir, $"test_{densityStr}_left.png");
                    saveCameraViewAction?.Invoke(leftCam, leftImgPath);

                    yield return ReadbackAndSaveSingleEye(leftDir, $"{densityStr}_left");
                }

                if (rightCam != null)
                {
                    if (leftCam != null) leftCam.enabled = false;
                    rightCam.enabled = true;

                    yield return null;
                    yield return new WaitForEndOfFrame();

                    string rightDir = Path.Combine(targetDensityDir, "Right");
                    Directory.CreateDirectory(rightDir);

                    string rightImgPath = Path.Combine(rightDir, $"test_{densityStr}_right.png");
                    saveCameraViewAction?.Invoke(rightCam, rightImgPath);

                    yield return ReadbackAndSaveSingleEye(rightDir, $"{densityStr}_right");
                }
            }
            finally
            {
                if (leftCam != null) leftCam.enabled = leftOriginalEnabled;
                if (rightCam != null) rightCam.enabled = rightOriginalEnabled;
            }
        }
    }
}
