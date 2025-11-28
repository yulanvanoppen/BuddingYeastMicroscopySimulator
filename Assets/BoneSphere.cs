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
    public float Spring = 1000f;

    [Tooltip("Higher the value the faster the spring oscillation stops")]
    public float Damper = 1000f;

    [Header("Other Settings")]
    public Softbody.ColliderShape Shape = Softbody.ColliderShape.Sphere;
    public float RootColliderSize = 0.015f;
    public float RootRigidbodyMass = 10f;
    public float RootRigidbodyDrag = 10f;
    public float ColliderSize = 0.006f;
    public float RigidbodyMass = 1f;
    public float ConnectionDistance = 0.1f;

    private Rigidbody root;

    private float scale;

    //private Rigidbody[] rigidBodies = null;
    //private SpringJoint[] springJoints = null;
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
        scale = transform.localScale.x;

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

            pivot = new GameObject("pivot" + i.ToString());
            pivot.transform.position = Nodes[i].transform.position;
            pivot.transform.parent = this.transform;
            rbPivot = pivot.AddComponent<Rigidbody>();
            rbPivot.useGravity = false;

            FixedJoint joint = pivot.AddComponent<FixedJoint>();
            joint.connectedBody = root;
            joint.connectedAnchor = Vector3.zero;
            joint.autoConfigureConnectedAnchor = false;

            goList.Add(pivot);

            sjList.Add(Softbody.AddSpring(ref Nodes[i], ref pivot));
        }
        Physics.IgnoreLayerCollision(0, 0, false);

        Pivots = goList.ToArray();
        //rigidBodies = rbList.ToArray();
        //springJoints = sjList.ToArray();
    }

    private void FixedUpdate()
    {
        Scale();
    }

    public void Scale()
    {
        float newScale = transform.localScale.x;
        if (newScale != scale)
        {
            float factor = newScale / scale;

            FixedJoint joint;

            for (int idx = 0; idx < Nodes.Length; ++idx)
            {
                joint = Pivots[idx].GetComponent<FixedJoint>();

                joint.anchor *= factor;

                // 2. Scale the connected anchor
                // The connectedAnchor is defined relative to the connected body's local space.
                // It should be scaled by the local scale of the connected body if that body 
                // has a non-uniform scale, but for simplicity and standard prefab scaling, 
                // we often scale it by the current object's local scale as well.
                if (joint.connectedBody != null)
                {
                    // For general scaling, use the scale of the object holding the joint:
                    joint.connectedAnchor *= factor;
                }
                if (colliders[idx] is SphereCollider sphere)
                    sphere.radius *= factor;
            }
        }
        scale = newScale;
    }
}