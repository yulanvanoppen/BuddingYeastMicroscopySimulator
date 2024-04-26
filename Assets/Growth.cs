using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Growth : MonoBehaviour
{
    public float GrowthRate = .01f;
    public float Variability = .5f;

    private float time = .25f;
    private BoneSphere boneSphere;

    private void Awake()
    {
        boneSphere = gameObject.GetComponent<BoneSphere>();
    }

    private void Start()
    {
        GrowthRate *= 1f + Variability * Random.Range(-1f, 1f);
    }

    private void Update()
    {
        time += Time.deltaTime;
        Grow(.5f * GrowthRate * Time.deltaTime / Mathf.Sqrt(time));
    }

    private void Grow(float dscale)
    {
        Scale(gameObject.transform.localScale.x + dscale);
    }

    public void Scale(float scale)
    {
        gameObject.transform.localScale = scale * Vector3.one;
        if (boneSphere != null && boneSphere.enabled) boneSphere.Scale();
    }
}
