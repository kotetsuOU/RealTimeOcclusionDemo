using UnityEngine;

public class ElephalMotion : MonoBehaviour
{
    public float moveSpeed = 0.01f;

    void Update()
    {
        Vector3 moveDirection = Vector3.zero;

        if (Input.GetKey(KeyCode.W))
        {
            moveDirection += Vector3.forward;
        }
        if (Input.GetKey(KeyCode.S))
        {
            moveDirection += Vector3.back;
        }
        if (Input.GetKey(KeyCode.A))
        {
            moveDirection += Vector3.left;
        }
        if (Input.GetKey(KeyCode.D))
        {
            moveDirection += Vector3.right;
        }
        if (Input.GetKey(KeyCode.E))
        {
            moveDirection += Vector3.up;
        }
        if (Input.GetKey(KeyCode.C))
        {
            moveDirection += Vector3.down;
        }
        transform.position += moveDirection * moveSpeed * Time.deltaTime;
    }
}
