using System.Collections;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

public class Force : MonoBehaviour
{
    private Rigidbody rb;
    public float Gravity = 100f;

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    // Update is called once per frame
    void Update()
    {
        Vector3 centerDirection = -transform.position;
        centerDirection.y *= 100f;
        rb.AddForce(Gravity * centerDirection.normalized, ForceMode.Acceleration);
    }
}
