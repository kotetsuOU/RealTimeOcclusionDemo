#if UNITY_EDITOR
using System.IO;
using System.Collections;
using UnityEditor;
using UnityEngine;
using SICESI;
using Core.Logging;

[InitializeOnLoad]
public class SICESI_ReCaptureTrigger : AssetPostprocessor
{
    private const string TriggerFile = ".start_capture";
    private const string DoneFile = ".capture_finished";

    static SICESI_ReCaptureTrigger()
    {
        EditorApplication.update += CheckTrigger;
    }

    private static void CheckTrigger()
    {
        if (!Application.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(TriggerFile))
        {
            AppLogger.Log(SICESI_StereoEvaluationController.TagCore, "[SICESI_ReCapture] トリガーを検出しました。シーンを保存し、Play モードに移行します...");
            try { File.Delete(TriggerFile); } catch { }
            if (File.Exists(DoneFile)) { try { File.Delete(DoneFile); } catch { } }

            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.isPlaying = true;
        }
    }

    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        CheckTrigger();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            var runner = new GameObject("[SICESI_ReCaptureRunner]").AddComponent<ReCaptureRunnerMono>();
            Object.DontDestroyOnLoad(runner.gameObject);
        }
    }

    private class ReCaptureRunnerMono : MonoBehaviour
    {
        private IEnumerator Start()
        {
            yield return null;
            yield return null;

            var controller = Object.FindAnyObjectByType<SICESI_StereoEvaluationController>();
            if (controller == null)
            {
                AppLogger.LogError(SICESI_StereoEvaluationController.TagCore, "[SICESI_ReCapture] SICESI_StereoEvaluationController がシーン内に見つかりません。");
                EditorApplication.isPlaying = false;
                yield break;
            }

            controller.outputDirectory = @"C:\Users\hongo\Documents\tsutsumi\Estimation\SICESI_Dataset\RawTest";
            controller.casesParentFolder = "";
            controller.conditionName = "1";
            controller.sweepDensities = new float[] { 0.25f };

            string jsonPath = Path.Combine(controller.outputDirectory, "1", "scene_transforms.json");
            controller.LoadAndApplySceneTransformsJson(jsonPath);

            var collector = controller.GetComponent<SICESI_SectorMaskCollector>();
            if (collector == null) collector = controller.gameObject.AddComponent<SICESI_SectorMaskCollector>();

            AppLogger.Log(SICESI_StereoEvaluationController.TagMaskSweep, "[SICESI_ReCapture] 8セクター二値マスクスイープ (配置1, 密度0.25) を実行します...");
            collector.RunSector8MaskSweep();

            while (collector.isCollecting)
            {
                yield return new WaitForSeconds(0.5f);
            }

            yield return new WaitForSeconds(1.0f);
            AppLogger.Log(SICESI_StereoEvaluationController.TagMaskSweep, "[SICESI_ReCapture] スイープ撮影が完了しました。.capture_finished を書き込みます。");
            File.WriteAllText(DoneFile, "Done at " + System.DateTime.Now);

            yield return new WaitForSeconds(0.5f);
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
