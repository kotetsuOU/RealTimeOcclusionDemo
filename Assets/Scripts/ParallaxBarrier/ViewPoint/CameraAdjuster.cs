using UnityEngine;

public class CameraAdjuster : MonoBehaviour
{
    public Transform displayTransform;

    [Header ("Debug")]
    public bool isHalfMirrorEnabled = true;

    public Vector3 defaultPosition = new Vector3(0.3f, 0.85f, 0.15f);

    private void LateUpdate()
    {
        if (displayTransform == null)
        {
            return;
        }

        Camera cam = GetComponent<Camera>();
        if (cam == null)
        {
            return;
        }

        Vector3 displayCenter = displayTransform.position;
        Vector3 displayRight = Vector3.right * displayTransform.localScale.x / 2;
        Vector3 displayUp = Vector3.forward * displayTransform.localScale.z / 2;

        Vector3 bl = displayCenter - displayRight - displayUp;
        Vector3 br = displayCenter + displayRight - displayUp;
        Vector3 tl = displayCenter - displayRight + displayUp;

        Matrix4x4 cameraTransform = cam.worldToCameraMatrix;
        bl = cameraTransform.MultiplyPoint(bl);
        br = cameraTransform.MultiplyPoint(br);
        tl = cameraTransform.MultiplyPoint(tl);

        float nearPlane = 0.1f;
        float right = br.x * (nearPlane / -br.z);
        float left = bl.x * (nearPlane / -bl.z);
        float top = tl.y * (nearPlane / -tl.z);
        float bottom = bl.y * (nearPlane / -bl.z);

        Matrix4x4 p;
        if (isHalfMirrorEnabled)
        {
            p = Matrix4x4.Frustum(right, left, bottom, top, nearPlane, 1);
        }
        else
        {
            p = Matrix4x4.Frustum(left, right, bottom, top, nearPlane, 1);
        }
        cam.projectionMatrix = p;
    }

    public void MoveToDefaultPosition()
    {
        this.transform.position = defaultPosition;

        UnityEngine.Debug.Log($"Moved to default position: {defaultPosition}");
    }
}