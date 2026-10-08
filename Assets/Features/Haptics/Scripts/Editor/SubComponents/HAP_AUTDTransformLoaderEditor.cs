#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// HAP_AUTDTransformLoader の個別インスペクター展開を抑制するスリムエディタ。
/// 設定の重複表示を防ぎ、HAP_AUTDHapticsController の [Placement] タブで一元管理します。
/// </summary>
[CustomEditor(typeof(HAP_AUTDTransformLoader))]
public class HAP_AUTDTransformLoaderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("💡 このコンポーネントの設定は HAP_AUTDHapticsController の [Placement] タブに統合されています。", MessageType.Info);
    }
}
#endif
