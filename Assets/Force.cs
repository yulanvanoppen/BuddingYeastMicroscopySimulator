using UnityEngine;

public class Force : MonoBehaviour
{
    private Rigidbody rb;
    public float Gravity = 1f;

    void Start() => rb = GetComponent<Rigidbody>();

    void Update()
    {
        Vector2 horizontalDirection = new Vector2(transform.position.x, transform.position.z).normalized;

        // force cells toward the XZ-plane and gently cluster around the origin
        Vector3 directedForce = new Vector3(-horizontalDirection.x / 100f,
                                            -transform.position.y * 10f,
                                            -horizontalDirection.y / 100f);

        rb.AddForce(Gravity * directedForce, ForceMode.Force);
    }
}
