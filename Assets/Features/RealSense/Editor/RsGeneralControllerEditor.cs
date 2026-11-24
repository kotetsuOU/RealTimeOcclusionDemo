using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RsGeneralController))]
public class RsGeneralControllerEditor : Editor
{
    private enum Tab
    {
        Global = 0,
        Device = 1,
        Transform = 2,
        Material = 3,
        Capture = 4
    }

    private static readonly string[] TabLabels = { "Global", "Device", "Transform", "Material", "Capture" };

    private Tab _currentTab = Tab.Global;
    private readonly RsGlobalTabDrawer _globalTabDrawer = new RsGlobalTabDrawer();
    private readonly RsTransformTabDrawer _transformTabDrawer = new RsTransformTabDrawer();

    private void OnEnable()
    {
        var controller = (RsGeneralController)target;
        controller.ApplySubComponentHideFlags();
    }

    public override void OnInspectorGUI()
    {
        var controller = (RsGeneralController)target;

        DrawHeaderBar(controller);
        EditorGUILayout.Space(6f);

        int nextTab = GUILayout.Toolbar((int)_currentTab, TabLabels, GUILayout.Height(26f));
        if (nextTab != (int)_currentTab)
        {
            _currentTab = (Tab)nextTab;
            GUI.FocusControl(null);
        }

        EditorGUILayout.Space(6f);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            DrawSelectedTab(controller);
        }
    }

    private void OnSceneGUI()
    {
        var controller = (RsGeneralController)target;
        _globalTabDrawer.DrawSceneGizmos(controller.GlobalManager);
    }

    private static void DrawHeaderBar(RsGeneralController controller)
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("RealSense General Controller", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            bool hide = GUILayout.Toggle(controller.HideSubComponentsInInspector, "Hide Sub-Components", EditorStyles.miniButton, GUILayout.Width(140f));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(controller, "Toggle Hide Sub-Components");
                controller.HideSubComponentsInInspector = hide;
                EditorUtility.SetDirty(controller);
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
        }
    }

    private void DrawSelectedTab(RsGeneralController controller)
    {
        switch (_currentTab)
        {
            case Tab.Global:
                if (EnsureComponentExists(controller.GlobalManager, controller, "RsGlobalPointCloudManager"))
                    _globalTabDrawer.DrawInspector(controller.GlobalManager);
                break;

            case Tab.Device:
                if (EnsureComponentExists(controller.DeviceController, controller, "RsDeviceController"))
                    DrawDefaultSerializedProperties(controller.DeviceController);
                break;

            case Tab.Transform:
                if (EnsureComponentExists(controller.TransformController, controller, "RsTransformController"))
                    _transformTabDrawer.DrawInspector(controller.TransformController);
                break;

            case Tab.Material:
                if (EnsureComponentExists(controller.MaterialController, controller, "RsMaterialController"))
                    DrawMaterialTab(controller.MaterialController);
                break;

            case Tab.Capture:
                if (EnsureComponentExists(controller.PointCloudCapturer, controller, "RsPointCloudCapturer"))
                    DrawDefaultSerializedProperties(controller.PointCloudCapturer);
                break;
        }
    }

    private static bool EnsureComponentExists<T>(T component, RsGeneralController controller, string displayName) where T : Component
    {
        if (component != null)
            return true;

        EditorGUILayout.HelpBox($"{displayName} がこの GameObject にアタッチされていません。", MessageType.Warning);
        if (GUILayout.Button($"Add {displayName}", GUILayout.Height(24f)))
        {
            var added = Undo.AddComponent<T>(controller.gameObject);
            if (added != null)
            {
                controller.ApplySubComponentHideFlags();
            }
        }
        return false;
    }

    private static void DrawMaterialTab(RsMaterialController materialController)
    {
        EditorGUI.BeginChangeCheck();
        DrawDefaultSerializedProperties(materialController);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(materialController, "Change Material Settings");
            materialController.ApplyMaterial();
            materialController.ApplyColorMode();
            EditorUtility.SetDirty(materialController);
        }
    }

    private static void DrawDefaultSerializedProperties(Component component)
    {
        var so = new SerializedObject(component);
        so.Update();
        SerializedProperty prop = so.GetIterator();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (prop.name == "m_Script") continue;
            EditorGUILayout.PropertyField(prop, true);
        }
        so.ApplyModifiedProperties();
    }
}
