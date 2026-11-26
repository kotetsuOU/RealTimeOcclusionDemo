using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Debug用に、特定のAUTD3Deviceの出力をいかなる場合でも強制的に停止（Null出力）させる機能を提供します。
/// HAP_AUTDController と同じ GameObject にアタッチして使用します。
/// </summary>
[RequireComponent(typeof(HAP_AUTDHapticsController))]
public class HAP_AUTDDebugDisabler : MonoBehaviour
{
    [HideInInspector]
    public List<bool> disabledDevices = new List<bool>();

    /// <summary>
    /// 指定されたデバイスIDが無効化されているかどうかを判定します。
    /// </summary>
    public bool IsDisabled(int deviceID)
    {
        if (deviceID >= 0 && deviceID < disabledDevices.Count)
        {
            return disabledDevices[deviceID];
        }
        return false;
    }
}
