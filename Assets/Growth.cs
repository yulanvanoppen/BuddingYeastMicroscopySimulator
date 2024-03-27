using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Growth : MonoBehaviour
{
    public float GrowthRate = .01f;
    public float Variability = .5f;

    // Start is called before the first frame update
    void Start()
    {
        GrowthRate *= 1f + Variability * Random.Range(-1f, 1f);
    }

    // Update is called once per frame
    void Update()
    {
        gameObject.transform.localScale += GrowthRate * Time.deltaTime * Vector3.one;
    }
}
