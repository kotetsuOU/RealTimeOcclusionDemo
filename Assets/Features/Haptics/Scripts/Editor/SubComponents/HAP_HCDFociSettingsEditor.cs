#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Features.Haptics.Processors;

/// <summary>
/// HAP_HCDFociSettings の個別インスペクター展開を抑制するスリムエディタ。
/// 設定の重複表示を防ぎ、HAP_AUTDHapticsController の [HCD Foci] タブで一元管理します。
/// </summary>
[CustomEditor(typeof(HAP_HCDFociSettings))]
public class HAP_HCDFociSettingsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("💡 このコンポーネントの設定は HAP_AUTDHapticsController の [HCD Foci] タブに統合されています。", MessageType.Info);
    }
}
#endif
