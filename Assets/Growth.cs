using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Growth : MonoBehaviour
{
    public float Multiplier = 1f;
    public float Variability = .5f;

    public float time = .25f;
    public float rate;
    private BoneSphere boneSphere;

    private void Awake()
    {
        boneSphere = gameObject.GetComponent<BoneSphere>();
    }

    private void Start()
    {
        time = 40f * transform.localScale.x;
        Multiplier *= 1f + Variability * Random.Range(-1f, 1f);
    }

    private void Update()
    {
        time += Time.deltaTime;
        rate = .0001f + .2f * Mathf.Exp(time/10f) * Mathf.Pow(Mathf.Exp(time/10f) + 1, -2f);
        Grow(Multiplier * rate * Time.deltaTime);
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

    public void ResetBud(float scale)
    {
        Scale(scale);
        time = 0f;
    }
}
