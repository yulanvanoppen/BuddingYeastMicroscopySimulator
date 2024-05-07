using UnityEngine;

public class Budding : MonoBehaviour
{
    [Tooltip("Mean time to until budding")] 
    public float MeanTime = 100f;

    [Tooltip("Relative mean time variabilty")]
    public float Variability = .2f;

    [Tooltip("Initial bud size")]
    public float BudSize = .01f;

    [Tooltip("List of possible budding site transforms")]
    public Transform[] Sites = null;

    [Tooltip("Bud prefab object (equal to own prefab)")]
    public GameObject Prefab = null;

    private float counter = 0f;
    private float cooldown;

    private void Start()
    {
        cooldown = MeanTime * (1 + Variability * Random.Range(-1, 1));
    }

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
        Transform randomSite = Sites[Random.Range(0, Sites.Length)];

        GameObject bud = Instantiate(Prefab, randomSite.position, Quaternion.identity);
        bud.GetComponent<Growth>().ResetBud(BudSize);
    }
}
