using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Budding : MonoBehaviour
{
    public float MeanTime = 100f;
    public float Variability = .2f;

    public float BudSize = .01f;


    public GameObject Prefab = null;

    private float counter = 0f;
    private float cooldown;


    // Start is called before the first frame update
    private void Start()
    {
        cooldown = MeanTime * (1 + Variability * Random.Range(-1, 1));
    }

    // Update is called once per frame
    private void Update()
    {
        counter += Time.deltaTime;

        if (counter > cooldown) {
            counter = 0f;
            cooldown = MeanTime * (1 + Variability * Random.Range(-1, 1));
            MakeBud();
        }
    }

    private void MakeBud()
    {
        Vector2 offset = transform.localScale.x * .2f * Random.insideUnitCircle.normalized;
        Vector3 offset3 = new Vector3(offset.x, 0f, offset.y);

        Debug.Log(offset3);
        Debug.Log(transform.position);

        GameObject bud = Instantiate(Prefab, transform.position + offset3, Quaternion.identity);
        bud.GetComponent<Growth>().ResetBud(BudSize);
    }
}
