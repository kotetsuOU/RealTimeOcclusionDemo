using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#nullable enable

/// <summary>
/// 複数の AUTD デバイスIDをグループとして管理・選択するためのデータ構造体/クラス。
/// Inspector でのチェックボックス操作および個別デバイス指定に対応します。
/// </summary>
[Serializable]
public class HAP_AUTDDeviceGroup
{
    [SerializeField]
    private List<int> selectedDeviceIDs = new List<int>();

    /// <summary>
    /// 選択されている AUTD デバイス ID のリストを取得します。
    /// </summary>
    public List<int> SelectedDeviceIDs
    {
        get => selectedDeviceIDs;
        set => selectedDeviceIDs = value ?? new List<int>();
    }

    public HAP_AUTDDeviceGroup()
    {
        selectedDeviceIDs = new List<int>();
    }

    public HAP_AUTDDeviceGroup(IEnumerable<int> initialDeviceIDs)
    {
        selectedDeviceIDs = initialDeviceIDs != null ? new List<int>(initialDeviceIDs) : new List<int>();
    }

    /// <summary>
    /// 単一デバイスIDで初期化（下位互換性用）
    /// </summary>
    public HAP_AUTDDeviceGroup(int singleDeviceID)
    {
        selectedDeviceIDs = new List<int>();
        if (singleDeviceID >= 0)
        {
            selectedDeviceIDs.Add(singleDeviceID);
        }
    }

    /// <summary>
    /// 指定されたデバイス ID がこのグループに含まれるか判定します。
    /// </summary>
    public bool ContainsDevice(int deviceID)
    {
        return selectedDeviceIDs.Contains(deviceID);
    }

    /// <summary>
    /// 指定されたデバイス ID の選択状態を切り替えます。
    /// </summary>
    public void SetDeviceSelected(int deviceID, bool selected)
    {
        if (selected)
        {
            if (!selectedDeviceIDs.Contains(deviceID))
            {
                selectedDeviceIDs.Add(deviceID);
                selectedDeviceIDs.Sort();
            }
        }
        else
        {
            selectedDeviceIDs.Remove(deviceID);
        }
    }

    /// <summary>
    /// 全ての選択をクリアします。
    /// </summary>
    public void Clear()
    {
        selectedDeviceIDs.Clear();
    }

    /// <summary>
    /// 何かしらのデバイスが1つ以上選択されているか。
    /// </summary>
    public bool HasAnyDevice => selectedDeviceIDs.Count > 0;
}
