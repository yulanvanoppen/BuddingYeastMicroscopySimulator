using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class Jitter : MonoBehaviour
{
    public float Magnitude = 1f;
    public GameObject Root = null;

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
        rb.AddForce(transform.localScale.x * Magnitude * Time.fixedDeltaTime
                         * UnityEngine.Random.insideUnitSphere, 
                        ForceMode.Impulse);
    }
}
