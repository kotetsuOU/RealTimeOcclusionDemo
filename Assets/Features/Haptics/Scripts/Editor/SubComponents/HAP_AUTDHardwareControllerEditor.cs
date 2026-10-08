#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// HAP_AUTDHardwareController の個別インスペクター展開を抑制するスリムエディタ。
/// 設定の重複表示を防ぎ、HAP_AUTDHapticsController の [Hardware] タブで一元管理します。
/// </summary>
[CustomEditor(typeof(HAP_AUTDHardwareController))]
public class HAP_AUTDHardwareControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("💡 このコンポーネントの設定は HAP_AUTDHapticsController の [Hardware] タブに統合されています。", MessageType.Info);
    }
}
#endif
