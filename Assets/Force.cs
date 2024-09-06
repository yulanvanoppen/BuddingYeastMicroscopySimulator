using UnityEngine;

public class Force : MonoBehaviour
{
    public GameObject Root = null;
    public float Gravity = 1f;

    private Rigidbody rb;

    void Start()
    {
        if (Root == null)
            rb = GetComponent<Rigidbody>();
        else
            rb = Root.GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        Vector2 horizontalDirection = new Vector2(rb.transform.position.x, rb.transform.position.z).normalized;

        // force cells toward the XZ-plane and gently cluster around the origin
        Vector3 directedForce = new Vector3(-horizontalDirection.x / 100f,
                                            -rb.transform.position.y * 10f,
                                            -horizontalDirection.y / 100f);

        rb.AddForce(Gravity * directedForce, ForceMode.Force);
    }
}
