using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class Jitter : MonoBehaviour
{
    private float Magnitude = 1f;
    private Rigidbody rb;
    private BoneSphere boneSphere;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        boneSphere = GetComponent<BoneSphere>();
    }

    void Update()
    {
        if (boneSphere != null && boneSphere.enabled)
            boneSphere.RootJitter();
        else
            rb.AddForce(transform.localScale.x * Magnitude * Time.deltaTime
                         * UnityEngine.Random.insideUnitSphere, 
                        ForceMode.Impulse);

    }
}
