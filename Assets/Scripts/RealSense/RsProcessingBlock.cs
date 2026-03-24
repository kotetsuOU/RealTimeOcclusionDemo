using System;
using System.Collections.Generic;
using UnityEngine;
using Intel.RealSense;
using System.Linq;

/// <summary>
/// Intel RealSenseの各フレームに対するフィルタリングやデータ変換を行う処理ブロックの基本インターフェース
/// </summary>
public interface IProcessingBlock
{
    Frame Process(Frame frame, FrameSource frameSource);
}

/// <summary>
/// UnityのScriptableObjectとして管理される、Intel RealSenseフレーム処理ブロックの抽象基底クラス
/// 各種フィルタや点群生成などの処理を部品化してパイプラインに組み込むために使用される
/// </summary>
[Serializable]
public abstract class RsProcessingBlock : ScriptableObject, IProcessingBlock
{
    [Tooltip("この処理ブロックを有効にするかどうか")]
    public bool enabled = true;

    /// <summary>
    /// ブロックの有効状態の取得・設定。
    /// エディタ上で変更された場合にObjectEnabledの状態を同期する。
    /// </summary>
    public bool Enabled
    {
        get
        {
            return enabled;
        }

        set
        {
            enabled = value;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetObjectEnabled(this, enabled);
#endif
        }
    }

    /// <summary>
    /// 受け取ったフレームに対して固有の処理（フィルタリング等）を施して返す
    /// </summary>
    public abstract Frame Process(Frame frame, FrameSource frameSource);

    /// <summary>
    /// ScriptableObjectとしてアセットに追加・リセットされた際、
    /// 名前が重複しないようにユニークな名称を割り当てる
    /// </summary>
    public virtual void Reset()
    {
        this.name = GetType().Name;

#if UNITY_EDITOR
        var p = UnityEditor.AssetDatabase.GetAssetPath(this);
        var names = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p).Where(a => a).Select(a => a.name).ToList();
        names.Remove(GetType().Name);
        this.name = UnityEditor.ObjectNames.GetUniqueName(names.ToArray(), GetType().Name);
        UnityEditor.AssetDatabase.SaveAssets();
#endif
    }
}