// Modified from https://github.com/omarvision/Softbody-Physics-Spring-Joint-

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BoneSphere : MonoBehaviour
{
    [Header("Bones")]
    public GameObject Root = null;
    public GameObject[] Nodes = null;
    public GameObject[] Pivots = null;

    [Header("Spring Joint Settings")]
    [Tooltip("Strength of spring")]
    public float Spring = 100f;

    [Tooltip("Higher the value the faster the spring oscillation stops")]
    public float Damper = 0.2f;

    [Header("Other Settings")]
    public Softbody.ColliderShape Shape = Softbody.ColliderShape.Sphere;
    public float RootColliderSize = 0.005f;
    public float RootRigidbodyMass = 1f;
    public float RootRigidbodyDrag = 10f;
    public float ColliderSize = 0.002f;
    public float RigidbodyMass = 1f;
    public float ConnectionDistance = 0.005f;
    public float Jitter = 0.00001f;

    private Rigidbody root;

    private float scale;

    private Rigidbody[] rigidBodies = null;
    private SpringJoint[] springJoints = null;
    private List<Collider> colliders = new List<Collider>();

    private void Awake()
    {
        Softbody.Init(Shape, ColliderSize, RigidbodyMass, Spring, Damper,
                      RigidbodyConstraints.FreezeRotation);

        root = Softbody.AddCollider(ref Root, colliders.ToArray(), Softbody.ColliderShape.Sphere,
                                    RootColliderSize, RootRigidbodyMass, RootRigidbodyDrag);
        colliders.Add(root.GetComponent<Collider>());
    }

    private void Start()
    {
        List<Rigidbody> rbList = new List<Rigidbody>();
        List<GameObject> goList = new List<GameObject>();
        List<SpringJoint> sjList = new List<SpringJoint>();
        GameObject pivot;
        Rigidbody rbPivot;
        Physics.IgnoreLayerCollision(0, 0, true);
        for (int i = 0; i < Nodes.Length; ++i)
        {
            rbList.Add(Softbody.AddCollider(ref Nodes[i], colliders.ToArray()));
            colliders.Add(rbList.Last().GetComponent<Collider>());

            pivot = new GameObject("pivot"+i.ToString());
            pivot.transform.position = Nodes[i].transform.position;
            pivot.transform.parent = this.transform;
            rbPivot = pivot.AddComponent<Rigidbody>();
            rbPivot.useGravity = false;
            pivot.AddComponent<FixedJoint>().connectedBody = root;
            goList.Add(pivot);

            sjList.Add(Softbody.AddSpring(ref Nodes[i], ref pivot));
        }
        Physics.IgnoreLayerCollision(0, 0, false);

        //scale = transform.localScale.x;
        //for (int i = 0; i < Nodes.Length; ++i)
        //{
        //    for (int j = i + 1; j < Nodes.Length; ++j)
        //    {
        //        if (Vector3.Distance(Nodes[i].transform.position, Nodes[j].transform.position) < ConnectionDistance * scale)
        //            sjList.Add(Softbody.AddSpring(ref Nodes[i], ref Nodes[j]));
        //    }
        //}

        //rigidBodies = rbList.ToArray();
        //springJoints = sjList.ToArray();

        //Debug.Log(rigidBodies);
        //Debug.Log(springJoints);
    }

    private void Update()
    {


        //Scale();
    }

    public void RootJitter()
    {
        root.AddForce(scale * Jitter * UnityEngine.Random.insideUnitSphere * Time.deltaTime, ForceMode.Impulse);
    }

    public void Scale()
    {
        scale = transform.localScale.x;

        root.mass = RootRigidbodyMass * Mathf.Pow(scale, 3f);
        //foreach (Rigidbody rb in rigidBodies)
        //    rb.mass = RigidbodyMass * Mathf.Pow(scale, 3f);

        //foreach (SpringJoint sj in springJoints)
        //{
        //    sj.spring = Spring * Mathf.Pow(scale, 3f);
        //    sj.massScale = scale;
        //    sj.connectedMassScale = scale;
        //}
    }
}