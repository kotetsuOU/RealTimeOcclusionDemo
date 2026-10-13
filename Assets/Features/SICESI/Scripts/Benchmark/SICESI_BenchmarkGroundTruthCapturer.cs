using System.Collections;
using UnityEngine;

namespace SICESI
{
    /// <summary>
    /// SICE SI ベンチマークにおける最終出力 Ground Truth (GT) 画像
    /// （手メッシュ黒化＋点群オフ）のステレオ撮影および状態復元を担当するクラス。
    /// （※ 仮想物体の Unlit 白化状態はベンチマークスイープ全体を通じて維持されます）
    /// </summary>
    public static class SICESI_BenchmarkGroundTruthCapturer
    {
        /// <summary>
        /// 最終出力 Ground Truth の左右ステレオ画像を撮影し、終了後に手メッシュと点群の元の状態を確実に復元します。
        /// </summary>
        public static IEnumerator CaptureFinalOutputGroundTruth(
            SICESI_StereoEvaluationController controller,
            string targetGtDir)
        {
            if (controller == null) yield break;

            var swapper = controller.MaterialSwapper;
            var gtBackup = swapper.SetGroundTruthState(
                controller.groundTruthObject,
                controller.groundTruthCaptureLayer,
                controller.renderGroundTruthAsBlack);

            try
            {
                // 点群を非表示にして真の幾何学的オクルージョン（手メッシュによる遮蔽）を撮影
                if (controller.pointCloudObject != null)
                {
                    controller.pointCloudObject.SetActive(false);
                }

                for (int i = 0; i < 4; i++) yield return null;
                yield return new WaitForEndOfFrame();

                SICESI_ScreenCaptureUtil.CaptureStereoViews(
                    controller.leftEyeCamera,
                    controller.rightEyeCamera,
                    targetGtDir,
                    "gt",
                    controller.applySRGBConversion);

                yield return new WaitForSeconds(0.15f);
            }
            finally
            {
                swapper.RestoreGroundTruthState(gtBackup);
                if (controller.pointCloudObject != null)
                {
                    controller.pointCloudObject.SetActive(true);
                }
            }
        }
    }
}
