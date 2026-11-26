using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 個々の AUTD デバイスのローカル座標・回転と ID を保存する構造体。
/// </summary>
[Serializable]
public struct HAP_AUTDTransformSnapshot
{
    public int id;
    public Vector3 localPosition;
    public Quaternion localRotation;
}

/// <summary>
/// 複数の AUTD デバイスの配置データ（位置・回転）を JSON 形式で保持・交換するための設定クラス。
/// </summary>
[Serializable]
public class HAP_AUTDTransformConfig
{
    public List<HAP_AUTDTransformSnapshot> snapshots = new List<HAP_AUTDTransformSnapshot>();
}

