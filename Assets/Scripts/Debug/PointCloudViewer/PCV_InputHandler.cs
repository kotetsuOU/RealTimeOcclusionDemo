using UnityEngine;

public class PCV_InputHandler : MonoBehaviour
{
    [SerializeField] private PointCloudViewer viewer;

    private void Awake()
    {
        if (viewer == null)
        {
            viewer = GetComponent<PointCloudViewer>();
        }
    }

    private void Update()
    {
        if (viewer == null || !UnityEngine.Application.isPlaying) return;

        if (Input.GetMouseButtonDown(0))
        {
            viewer.HandleInteraction();
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            viewer.StartNoiseFiltering();
        }
    }
}