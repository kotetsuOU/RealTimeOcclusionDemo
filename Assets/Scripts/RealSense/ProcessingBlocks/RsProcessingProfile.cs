using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 直列に実行する複数のRsProcessingBlock（フィルタ等）をまとめて管理するプロファイル。
/// ScriptableObjectとして保存され、RsProcessingPipeにセットされる。
/// </summary>
[CreateAssetMenu]
public class RsProcessingProfile : ScriptableObject, IEnumerable<RsProcessingBlock>
{
    // [HideInInspector]
    [SerializeField]
    [Tooltip("パイプラインで実行する順序に登録された処理ブロックのリスト")]
    public List<RsProcessingBlock> _processingBlocks;

    public IEnumerator<RsProcessingBlock> GetEnumerator()
    {
        return _processingBlocks.GetEnumerator() as IEnumerator<RsProcessingBlock>;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _processingBlocks.GetEnumerator();
    }


#if UNITY_EDITOR
    /// <summary>
    /// エディタ上でコンポーネントがリセットされたり作成された場合の処理。
    /// サブアセットとして保存されている各ブロックを検索し、自動的にリストに追加する。
    /// </summary>
    void Reset()
    {

        var obj = new UnityEditor.SerializedObject(this);
        obj.Update();

        var blocks = obj.FindProperty("_processingBlocks");
        blocks.ClearArray();

        var p = UnityEditor.AssetDatabase.GetAssetPath(this);
        var bl = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p);
        foreach (var a in bl)
        {
            if (a == this)
                continue;
            // Debug.Log(a, a);
            // DestroyImmediate(a, true);
            int i = blocks.arraySize++;
            var e = blocks.GetArrayElementAtIndex(i);
            e.objectReferenceValue = a;
        }

        obj.ApplyModifiedProperties();
        // UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();

    }
#endif
}
