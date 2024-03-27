// Modified from https://github.com/omarvision/Softbody-Physics-Spring-Joint-

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Unity.VisualScripting;
using UnityEngine;

public class BoneSphere : MonoBehaviour
{
    [Header("Bones")]
    public GameObject root = null;
    public GameObject[] nodes = null;

    public int[,] connections = null;

    [Serializable]
    public class EndPoints { public GameObject[] components = null; }

    [Header("Spring Joint Settings")]
    [Tooltip("Strength of spring")]
    public float Spring = 100f;

    [Tooltip("Higher the value the faster the spring oscillation stops")]
    public float Damper = 0.2f;

    [Header("Other Settings")]
    public Softbody.ColliderShape Shape = Softbody.ColliderShape.Sphere;
    public float RootColliderSize = 0.005f;
    public float RootRigidbodyMass = 10f;
    public float ColliderSize = 0.002f;
    public float RigidbodyMass = 1f;
    public float ConnectionDistance = 0.005f;
    public float Jitter = 0.00001f;


    private Rigidbody Root;

    //   0  1  2  3    4  5  6  7  8  9 10 11   12 13 14 15 16 17 18 19 20 21 22 23   24 25 26 27 28 29 30 31 32 33 34 35   36 37 38 39 40 41 42 43   44 45 46 47
    //  a1  2  3  4   b1  2  3  4  5  6  7  8   c1  2  3  4  5  6  7  8  9 10 11 12   c1  2  3  4  5  6  7  8  9 10 11 12   b1  2  3  4  5  6  7  8   f1  2  3  4
    //private void Awake()
    //{
    //    connections = new int[48, 5] {
    //        {  1,  3,  4,  5, 11 },
    //        {  2,  5,  6,  7,  0 },
    //        {  3,  7,  8,  9,  0 },
    //        {  9, 10, 11,  0,  0 },

    //        {  5, 11, 12, 13, 23 },
    //        {  6, 13, 14,  0,  0 },
    //        {  7, 14, 15, 16,  0 },
    //        {  8, 16, 17,  0,  0 },
    //        {  9, 17, 18, 19,  0 },
    //        { 10, 19, 20,  0,  0 },
    //        { 11, 20, 21, 22,  0 },
    //        { 22, 23,  0,  0,  0 }
    //    };
    //}

    private void Start()
    {
        float scale = transform.localScale[0];
        float scale2 = Mathf.Pow(scale, 2f);
        float scale3 = Mathf.Pow(scale, 3f);
        Softbody.Init(Shape, ColliderSize*scale, RigidbodyMass*scale3, Spring, Damper,
                      RigidbodyConstraints.FreezeRotation, scale);

        Softbody.AddCollider(ref root, Softbody.ColliderShape.Sphere,
                             RootColliderSize*scale, RootRigidbodyMass*scale3);
        for (int i = 0; i < nodes.Length; ++i)
        {
            Root = Softbody.AddCollider(ref nodes[i]);
            Softbody.AddSpring(ref nodes[i], ref root);
        }

        for (int i = 0; i < nodes.Length; ++i)
        {
            for (int j = i + 1; j < nodes.Length; ++j)
            {
                if (Vector3.Distance(nodes[i].transform.position, nodes[j].transform.position) < ConnectionDistance * scale)
                    Softbody.AddSpring(ref nodes[i], ref nodes[j]);
            }
        }
    }

    private void Update()
    {
        float scale = transform.localScale[0];
        Root.AddForce(scale * Jitter * UnityEngine.Random.insideUnitSphere * Time.deltaTime, ForceMode.Impulse);
    }
}