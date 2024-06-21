// Modified from https://github.com/omarvision/Softbody-Physics-Spring-Joint-

using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using static Softbody;

public static class Softbody
{
    #region --- helpers ---
    public enum ColliderShape
    {
        Box,
        Sphere,
    }
    #endregion

    public static ColliderShape Shape;
    public static float ColliderSize;
    public static float RigidbodyMass;
    public static float Spring;
    public static float Damper;
    public static RigidbodyConstraints Constraints;

    public static void Init(ColliderShape shape, float collidersize, float rigidbodymass, float spring, float damper, RigidbodyConstraints constraints)
    {
        Shape = shape;
        ColliderSize = collidersize;
        RigidbodyMass = rigidbodymass;
        Spring = spring;
        Damper = damper;
        Constraints = constraints;
    }
    public static Rigidbody AddCollider(ref GameObject go, Collider[] colliders)
    {
        return AddCollider(ref go, colliders, Shape, ColliderSize, RigidbodyMass, 1f);
    }
    public static SpringJoint AddSpring(ref GameObject go1, ref GameObject go2)
    {
        SpringJoint sp = AddSpring(ref go1, ref go2, Spring, Damper);

        return sp;
    }

    public static Rigidbody AddCollider(ref GameObject go, Collider[] colliders, 
                                        ColliderShape shape, float size, float mass, float drag)
    {
        switch (shape)
        {
            case ColliderShape.Box:
                //go.AddComponent<IgnoreOwnCollision>();
                BoxCollider bc = go.AddComponent<BoxCollider>();
                for (int i = 0; i < colliders.Length; ++i) Physics.IgnoreCollision(bc, colliders[i]);
                bc.size = new Vector3(size, size, size);
                break;
            case ColliderShape.Sphere:
                //go.AddComponent<IgnoreOwnCollision>();
                SphereCollider sc = go.AddComponent<SphereCollider>();
                for (int i = 0; i < colliders.Length; ++i) Physics.IgnoreCollision(sc, colliders[i]);
                sc.radius = size;
                break;
        }

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.drag = drag;
        rb.angularDrag = 1f;
        rb.constraints = Constraints;
        rb.useGravity = false;
        return rb;
    }
    public static SpringJoint AddSpring(ref GameObject go1, ref GameObject go2, float spring, float damper)
    {
        SpringJoint sp = go1.AddComponent<SpringJoint>();
        sp.connectedBody = go2.GetComponent<Rigidbody>();
        sp.spring = spring;
        sp.damper = damper;

        sp.tolerance = .0000001f;


        return sp;
    }
}