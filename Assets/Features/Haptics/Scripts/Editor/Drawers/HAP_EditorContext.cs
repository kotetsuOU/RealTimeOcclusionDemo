#if UNITY_EDITOR
#nullable enable
using UnityEditor;
using UnityEngine;

namespace Features.Haptics.Editor
{
    /// <summary>
    /// HAP_AUTDHapticsControllerEditor と各専任 Drawer 間で共有される SerializedProperty および連動オブジェクトのコンテキスト。
    /// ハードウェア、トランスフォーム、HCD焦点設定などの外部コンポーネントの SerializedObject も一元キャッシュし、
    /// 一つのコントローラーからシームレスに全設定を編集・保存できるようにします。
    /// </summary>
    public class HAP_EditorContext
    {
        public HAP_AUTDHapticsController Controller { get; }
        public SerializedObject SerializedObject { get; }

        public SerializedProperty ScriptProp { get; }

        // General / Source
        public SerializedProperty HardwareControllerProp { get; }
        public SerializedProperty TransformLoaderProp { get; }
        public SerializedProperty SourceModeProp { get; }
        public SerializedProperty HcdPipelineProp { get; }
        public SerializedProperty HcdFociSettingsProp { get; }
        public SerializedProperty ObjectHapticsControllersProp { get; }
        public SerializedProperty ActiveObjectControllerIndexProp { get; }

        // Acoustics
        public SerializedProperty HoloAlgorithmProp { get; }
        public SerializedProperty FocusIntensityPascalProp { get; }
        public SerializedProperty GspatRepeatCountProp { get; }
        public SerializedProperty EnableDirectionalGroupingProp { get; }
        public SerializedProperty DirectionalAngleThresholdProp { get; }

        // STM
        public SerializedProperty StmModeProp { get; }
        public SerializedProperty StmFrequencyProp { get; }
        public SerializedProperty GainStmModeProp { get; }

        // Debug & Profiling
        public SerializedProperty VisualizeDevicesProp { get; }
        public SerializedProperty EnableProfilingProp { get; }
        public SerializedProperty SynchronousSendProp { get; }
        public SerializedProperty ProfilingLogIntervalProp { get; }

        // ─── 連動オブジェクト (Sub-SerializedObjects) ─────────────────────────
        public SerializedObject? HardwareSO { get; private set; }
        public SerializedProperty? HwLinkTypeProp { get; private set; }
        public SerializedProperty? HwSoemAdapterProp { get; private set; }
        public SerializedProperty? HwTemperatureProp { get; private set; }
        public SerializedProperty? HwEnableFanProp { get; private set; }
        public SerializedProperty? HwModulationModeProp { get; private set; }
        public SerializedProperty? HwSineFrequencyProp { get; private set; }
        public SerializedProperty? HwStaticAmplitudeProp { get; private set; }
        public SerializedProperty? HwSilencerModeProp { get; private set; }
        public SerializedProperty? HwSilencerStepPhaseProp { get; private set; }
        public SerializedProperty? HwSilencerStepAmpProp { get; private set; }

        public SerializedObject? TransformLoaderSO { get; private set; }
        public SerializedProperty? TlConfigFileProp { get; private set; }
        public SerializedProperty? TlDevicePrefabProp { get; private set; }
        public SerializedProperty? TlDeviceRootProp { get; private set; }
        public SerializedProperty? TlPrefabCountProp { get; private set; }
        public SerializedProperty? TlOffsetProp { get; private set; }

        public SerializedObject? FociSettingsSO { get; private set; }
        public SerializedProperty? FociGenModeProp { get; private set; }
        public SerializedProperty? FociCentroidSourceProp { get; private set; }
        public SerializedProperty? FociEllipseSourceProp { get; private set; }
        public SerializedProperty? FociRandomSourceProp { get; private set; }

        public SerializedObject? DebugDisablerSO { get; private set; }

        public HAP_EditorContext(HAP_AUTDHapticsController controller, SerializedObject so)
        {
            Controller = controller;
            SerializedObject = so;

            ScriptProp = so.FindProperty("m_Script");

            HardwareControllerProp = so.FindProperty("hardwareController");
            TransformLoaderProp = so.FindProperty("transformLoader");
            SourceModeProp = so.FindProperty("sourceMode");
            HcdPipelineProp = so.FindProperty("hcdPipeline");
            HcdFociSettingsProp = so.FindProperty("hcdFociSettings");
            ObjectHapticsControllersProp = so.FindProperty("objectHapticsControllers");
            ActiveObjectControllerIndexProp = so.FindProperty("activeObjectControllerIndex");

            HoloAlgorithmProp = so.FindProperty("acousticConfig.holoAlgorithm");
            FocusIntensityPascalProp = so.FindProperty("acousticConfig.focusIntensityPascal");
            GspatRepeatCountProp = so.FindProperty("acousticConfig.gspatRepeatCount");
            EnableDirectionalGroupingProp = so.FindProperty("acousticConfig.enableDirectionalGrouping");
            DirectionalAngleThresholdProp = so.FindProperty("acousticConfig.directionalAngleThreshold");

            StmModeProp = so.FindProperty("stmConfig.stmMode");
            StmFrequencyProp = so.FindProperty("stmConfig.stmFrequency");
            GainStmModeProp = so.FindProperty("stmConfig.gainStmMode");

            VisualizeDevicesProp = so.FindProperty("visualizeDevices");
            EnableProfilingProp = so.FindProperty("profilingConfig.enableProfiling");
            SynchronousSendProp = so.FindProperty("profilingConfig.synchronousSend");
            ProfilingLogIntervalProp = so.FindProperty("profilingConfig.profilingLogInterval");

            RefreshLinkedObjects();
        }

        public void RefreshLinkedObjects()
        {
            // 1. HardwareController
            var hw = Controller.hardwareController;
            if (hw != null)
            {
                HardwareSO = new SerializedObject(hw);
                HwLinkTypeProp = HardwareSO.FindProperty("linkType");
                HwSoemAdapterProp = HardwareSO.FindProperty("soemAdapterName");
                HwTemperatureProp = HardwareSO.FindProperty("temperature");
                HwEnableFanProp = HardwareSO.FindProperty("enableFan");
                HwModulationModeProp = HardwareSO.FindProperty("modulationMode");
                HwSineFrequencyProp = HardwareSO.FindProperty("sineFrequency");
                HwStaticAmplitudeProp = HardwareSO.FindProperty("staticAmplitude");
                HwSilencerModeProp = HardwareSO.FindProperty("silencerMode");
                HwSilencerStepPhaseProp = HardwareSO.FindProperty("silencerStepPhase");
                HwSilencerStepAmpProp = HardwareSO.FindProperty("silencerStepAmplitude");
            }
            else
            {
                HardwareSO = null;
            }

            // 2. TransformLoader
            var tl = Controller.transformLoader;
            if (tl != null)
            {
                TransformLoaderSO = new SerializedObject(tl);
                TlConfigFileProp = TransformLoaderSO.FindProperty("configFileName");
                TlDevicePrefabProp = TransformLoaderSO.FindProperty("devicePrefab");
                TlDeviceRootProp = TransformLoaderSO.FindProperty("deviceRoot");
                TlPrefabCountProp = TransformLoaderSO.FindProperty("prefabCount");
                TlOffsetProp = TransformLoaderSO.FindProperty("offset");
            }
            else
            {
                TransformLoaderSO = null;
                TlConfigFileProp = null;
                TlDevicePrefabProp = null;
                TlDeviceRootProp = null;
                TlPrefabCountProp = null;
                TlOffsetProp = null;
            }

            // 3. HCDFociSettings
            var foci = Controller.hcdFociSettings;
            if (foci != null)
            {
                FociSettingsSO = new SerializedObject(foci);
                FociGenModeProp = FociSettingsSO.FindProperty("generationMode");
                FociCentroidSourceProp = FociSettingsSO.FindProperty("centroidSource");
                FociEllipseSourceProp = FociSettingsSO.FindProperty("ellipseSource");
                FociRandomSourceProp = FociSettingsSO.FindProperty("randomSource");
            }
            else
            {
                FociSettingsSO = null;
            }

            // 4. DebugDisabler
            var disabler = Controller.debugDisabler;
            if (disabler != null)
            {
                DebugDisablerSO = new SerializedObject(disabler);
            }
            else
            {
                DebugDisablerSO = null;
            }
        }

        public void UpdateAll()
        {
            SerializedObject.Update();
            HardwareSO?.Update();
            TransformLoaderSO?.Update();
            FociSettingsSO?.Update();
            DebugDisablerSO?.Update();
        }

        public void ApplyAll()
        {
            SerializedObject.ApplyModifiedProperties();
            HardwareSO?.ApplyModifiedProperties();
            TransformLoaderSO?.ApplyModifiedProperties();
            FociSettingsSO?.ApplyModifiedProperties();
            DebugDisablerSO?.ApplyModifiedProperties();
        }
    }
}
#endif
