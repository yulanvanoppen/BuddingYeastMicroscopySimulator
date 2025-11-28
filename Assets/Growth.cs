using UnityEngine;

public class Growth : MonoBehaviour
{
    [Tooltip("Growth rate multiplier")]
    public float Multiplier = 1f;

    [Tooltip("Relative multiplier variabilty")]
    public float Variability = .5f;

    private float time = .25f;
    private float rate;
    //private BoneSphere boneSphere;

    private void Awake()
    {
        //boneSphere = gameObject.GetComponent<BoneSphere>();
    }

    private void Start()
    {
        // prevent initial mother cells to keep growing
        time = 40f * transform.localScale.x;

        Multiplier *= 1f + Variability * Random.Range(-1f, 1f);
    }

    private void FixedUpdate()
    {
        time += Time.fixedDeltaTime;

        // saturate growth rate after +- 40 seconds
        rate = .0001f + .2f * Mathf.Exp(time/10f) * Mathf.Pow(Mathf.Exp(time/10f) + 1, -2f);

        Grow(Multiplier * rate * Time.fixedDeltaTime);
    }

    private void Grow(float dscale)
    {
        Scale(gameObject.transform.localScale.x + dscale);
    }

    public void Scale(float scale)
    {
        gameObject.transform.localScale = scale * Vector3.one;

        // additional scaling step for soft mesh behavior
        // if (boneSphere != null && boneSphere.enabled) boneSphere.Scale();
    }

    public void ResetBud(float scale)
    {
        Scale(scale);
        time = 0f;
    }
}
