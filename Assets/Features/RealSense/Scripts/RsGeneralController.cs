using UnityEngine;

/// <summary>
/// RealSense 親 GameObject にアタッチされた複数の管理コンポーネント
/// （<see cref="RsGlobalPointCloudManager"/>, <see cref="RsDeviceController"/>,
/// <see cref="RsTransformController"/>, <see cref="RsMaterialController"/>, <see cref="RsPointCloudCapturer"/>）
/// を Inspector 上で 1 つのタブ付き UI として統合管理するためのハブコンポーネント。
/// </summary>
[DisallowMultipleComponent]
public class RsGeneralController : MonoBehaviour
{
    [SerializeField]
    [Tooltip("ON にすると、同一 GameObject 上の管理対象サブコンポーネントを Inspector 上で非表示にし、このコンポーネントのタブ内で一元管理します。")]
    private bool _hideSubComponentsInInspector = true;

    public bool HideSubComponentsInInspector
    {
        get => _hideSubComponentsInInspector;
        set
        {
            _hideSubComponentsInInspector = value;
#if UNITY_EDITOR
            ApplySubComponentHideFlags();
#endif
        }
    }

    public RsGlobalPointCloudManager GlobalManager => GetComponent<RsGlobalPointCloudManager>();
    public RsDeviceController DeviceController => GetComponent<RsDeviceController>();
    public RsTransformController TransformController => GetComponent<RsTransformController>();
    public RsMaterialController MaterialController => GetComponent<RsMaterialController>();
    public RsPointCloudCapturer PointCloudCapturer => GetComponent<RsPointCloudCapturer>();

#if UNITY_EDITOR
    private void Reset()
    {
        ApplySubComponentHideFlags();
    }

    private void OnValidate()
    {
        ApplySubComponentHideFlags();
    }

    /// <summary>
    /// 管理対象サブコンポーネントの HideInInspector フラグを一括更新する
    /// </summary>
    public void ApplySubComponentHideFlags()
    {
        UpdateComponentHideFlag(GlobalManager);
        UpdateComponentHideFlag(DeviceController);
        UpdateComponentHideFlag(TransformController);
        UpdateComponentHideFlag(MaterialController);
        UpdateComponentHideFlag(PointCloudCapturer);
    }

    private void UpdateComponentHideFlag(Component comp)
    {
        if (comp == null) return;

        if (_hideSubComponentsInInspector)
        {
            if ((comp.hideFlags & HideFlags.HideInInspector) == 0)
            {
                comp.hideFlags |= HideFlags.HideInInspector;
                UnityEditor.EditorUtility.SetDirty(comp);
            }
        }
        else
        {
            if ((comp.hideFlags & HideFlags.HideInInspector) != 0)
            {
                comp.hideFlags &= ~HideFlags.HideInInspector;
                UnityEditor.EditorUtility.SetDirty(comp);
            }
        }
    }
#endif
}

