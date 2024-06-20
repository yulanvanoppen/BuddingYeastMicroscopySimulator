using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class Contour : MonoBehaviour
{
    private Mesh mesh;
    private int[,] edges;


    // Start is called before the first frame update
    void Awake()
    {
        mesh = GetComponent<SkinnedMeshRenderer>().sharedMesh;
        edges = new int[mesh.triangles.Length, 2];
        Debug.Log(mesh.triangles.Length);
        for (int i = 0; i < mesh.triangles.Length; i += 3)
        {
            edges[i,   0] = edges[i+2, 1] = mesh.triangles[i];
            edges[i+1, 0] = edges[i,   1] = mesh.triangles[i+1];
            edges[i+2, 0] = edges[i+1, 1] = mesh.triangles[i+2];
        }
    }

    // Update is called once per frame
    void Update()
    {
        //Vector3[] points = new Vector3[edges.GetLength(0)];
        float t;
        Vector3 a;
        Vector3 b;
        Vector3 c;

        a = transform.TransformPoint(mesh.vertices[edges[0, 0]]);
        b = transform.TransformPoint(mesh.vertices[edges[0, 1]]);
        t = a.y / (a.y - b.y);
        c = a + (b-a) * t;
        Debug.Log(c);

        //for (int i = 0; i < mesh.triangles.Length; i++)
        //{
        //    a = transform.TransformPoint(mesh.vertices[edges[i, 0]]);
        //    b = transform.TransformPoint(mesh.vertices[edges[i, 1]]);
        //    if (Mathf.Sign(a.y) !=  Mathf.Sign(b.y))
        //    {
        //        t = a.y / (a.y - b.y);
        //        c = Vector3.Lerp(a, b, t);
        //        Debug.DrawLine(c, c*100, Color.red, Time.deltaTime, false);
        //    }
        //}
    }
}
