using UnityEngine;

public class PCV_InputHandler : MonoBehaviour
{
    [SerializeField] private PCV_Controller controller;

    private void Awake()
    {
        if (controller == null)
        {
            controller = GetComponent<PCV_Controller>();
        }
    }

    private void Update()
    {
        if (controller == null || !UnityEngine.Application.isPlaying) return;

        if (Input.GetMouseButtonDown(0))
        {
            controller.HandleInteraction();
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            controller.StartNeighborFiltering();
        }
    }
}