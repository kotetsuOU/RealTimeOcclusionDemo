#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Features.PhysicalResponse
{
    [CustomEditor(typeof(PR_VirtualObjectManager))]
    public class PR_VirtualObjectManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var manager = (PR_VirtualObjectManager)target;

            EditorGUILayout.LabelField("Virtual Objects Management", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("virtualObjects"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("allowTabSwitch"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("syncWithHcd"));

            EditorGUILayout.Space();

            if (manager.virtualObjects != null && manager.virtualObjects.Length > 0)
            {
                EditorGUILayout.LabelField("Active Model Switching", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("◀ Previous Model", GUILayout.Height(30)))
                {
                    Undo.RecordObject(manager, "Switch Previous Model");
                    int prevIndex = (manager.CurrentActiveIndex - 1 + manager.virtualObjects.Length) % manager.virtualObjects.Length;
                    manager.SwitchTo(prevIndex);
                    EditorUtility.SetDirty(manager);
                }
                if (GUILayout.Button("Next Model ▶", GUILayout.Height(30)))
                {
                    Undo.RecordObject(manager, "Switch Next Model");
                    manager.SwitchNext();
                    EditorUtility.SetDirty(manager);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Direct Selection:", EditorStyles.miniBoldLabel);
                for (int i = 0; i < manager.virtualObjects.Length; i++)
                {
                    var obj = manager.virtualObjects[i];
                    string name = obj != null ? obj.name : $"(Empty {i})";
                    bool isActive = (i == manager.CurrentActiveIndex);

                    GUI.backgroundColor = isActive ? Color.green : Color.white;
                    if (GUILayout.Button($"{i}: {name} {(isActive ? " [Active]" : "")}"))
                    {
                        Undo.RecordObject(manager, $"Switch To {name}");
                        manager.SwitchTo(i);
                        EditorUtility.SetDirty(manager);
                    }
                    GUI.backgroundColor = Color.white;
                }
            }
            else
            {
                if (GUILayout.Button("Collect Children as Virtual Objects", GUILayout.Height(30)))
                {
                    Undo.RecordObject(manager, "Collect Children");
                    manager.InitializeObjects();
                    EditorUtility.SetDirty(manager);
                }
                EditorGUILayout.HelpBox("バーチャルオブジェクトが未登録です。[Collect Children] を押すか手動で登録してください。", MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
