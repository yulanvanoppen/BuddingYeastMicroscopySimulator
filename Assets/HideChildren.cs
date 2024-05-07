using UnityEngine;

public class HideChildren : MonoBehaviour
{
    void Start()
    {
        foreach (Transform child in transform)
            child.gameObject.GetComponent<Renderer>().enabled = false;
    }
}
