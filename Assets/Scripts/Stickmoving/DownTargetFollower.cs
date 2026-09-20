using UnityEngine;

public class DownTargetFollower : MonoBehaviour
{
    [SerializeField] private StickMover stickMover;
    [SerializeField] private MultiAUTD3Controller autdController;
    [SerializeField] private Transform palmReference;  // 鏡映の中心（円軌道の中心と同じ）

    [Tooltip("手の厚み。手のひら表面の座標からこの分だけY方向に上げて、手の甲側の座標にする")]
    [SerializeField] private float handThickness = 0.03f;

    [Tooltip("ハーフミラーの鏡映を補正するため、X軸方向を反転する")]
    [SerializeField] private bool mirrorX = true;

    private bool wasMoving = false;

    void Update()
    {
        if (stickMover == null || autdController == null) return;

        bool isMoving = stickMover.IsMoving;

        if (isMoving)
        {
            Vector3 tip = stickMover.TipPosition;

            float x = tip.x;
            if (mirrorX && palmReference != null)
            {
                float centerX = palmReference.position.x;
                x = 2f * centerX - tip.x;  // centerXを軸にXを反転
            }

            Vector3 dorsalPoint = new Vector3(x, tip.y + handThickness, tip.z);

            transform.position = dorsalPoint;
            autdController.SendFocusAtWorld(dorsalPoint, MultiAUTD3Controller.OutputSide.DownOnly);
        }

        if (wasMoving && !isMoving)
        {
            autdController.ClearExternalFocus();
        }

        wasMoving = isMoving;
    }
}