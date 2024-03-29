using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Budding : MonoBehaviour
{
    public float MeanTime = 100f;
    public float Variability = .2f;

    public float BudSize = 0.5f;

    private float counter = 0f;
    private float cooldown;

    // Start is called before the first frame update
    void Start()
    {
        cooldown = MeanTime * (1 + Variability * Random.Range(-1, 1));
    }

    // Update is called once per frame
    void Update()
    {
        counter += Time.deltaTime;

        if (counter > cooldown) {
            counter = 0f;
            cooldown = MeanTime * (1 + Variability * Random.Range(-1, 1));
            MakeBud();
        }
    }

    void MakeBud()
    {
        Vector3 offset = Random.insideUnitCircle.normalized;
        GameObject bud = Instantiate(gameObject, transform.position + offset, Quaternion.identity);
        bud.transform.localScale = BudSize * Vector3.one;
    }
}
