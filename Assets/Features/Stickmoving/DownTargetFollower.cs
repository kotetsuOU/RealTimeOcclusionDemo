using UnityEngine;

public class DownTargetFollower : MonoBehaviour
{
    [SerializeField] private CylinderMover stickMover;
    [SerializeField] private MultiAUTD3Controller autdController;
    [SerializeField] private Transform palmReference;  // 鏡映の中心（円軌道の中心と同じ）

    [Tooltip("手の厚み。手のひら表面の座標からこの分だけY方向に上げて、手の甲側の座標にする")]
    [SerializeField] private float handThickness = 0.03f;

    [Tooltip("ハーフミラーの鏡映を補正するため、X軸方向を反転する")]
    [SerializeField] private bool mirrorX = true;

    [Header("条件設定(実験コンダクター使用時は上書きされる)")]
    [SerializeField] private bool hapticEnabled = true;

    [Tooltip("見た目に対する触覚の位相遅れ(1周期に対する割合。0.5で半周期=180°)")]
    [Range(0f, 1f)] [SerializeField] private float phaseLag = 0f;

    [Tooltip("出力強度(0-255)。負の値ならMultiAUTD3ControllerのdownIntensityを使う")]
    [SerializeField] private int intensityOverride = -1;

    private bool wasActive = false;

    public void Configure(bool enableHaptic, float phaseLagCycles, int intensity)
    {
        hapticEnabled = enableHaptic;
        phaseLag = Mathf.Repeat(phaseLagCycles, 1f);
        intensityOverride = intensity;
    }

    /// <summary>上側の先端座標から、下側で触覚焦点を出す座標を求める</summary>
    public Vector3 ComputeFocusPoint(Vector3 tip)
    {
        float x = tip.x;
        if (mirrorX && palmReference != null)
        {
            float centerX = palmReference.position.x;
            x = 2f * centerX - tip.x;  // centerXを軸にXを反転
        }

        return new Vector3(x, tip.y - handThickness, tip.z);
    }

    public void StopOutput()
    {
        if (wasActive && autdController != null) autdController.ClearExternalFocus();
        wasActive = false;
    }

    void Update()
    {
        if (stickMover == null || autdController == null) return;

        bool isActive = hapticEnabled && stickMover.IsMoving && stickMover.IsVisible;

        if (isActive)
        {
            Vector3 tip = phaseLag > 0f
                ? stickMover.GetTipPositionWithPhaseLag(phaseLag)
                : stickMover.TipPosition;
            Vector3 dorsalPoint = ComputeFocusPoint(tip);

            transform.position = dorsalPoint;
            int? intensity = intensityOverride >= 0 ? intensityOverride : (int?)null;
            autdController.SendFocusAtWorld(dorsalPoint, MultiAUTD3Controller.OutputSide.DownOnly, intensity);
        }

        if (wasActive && !isActive)
        {
            autdController.ClearExternalFocus();
        }

        wasActive = isActive;
    }
}
