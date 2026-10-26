using System.Collections.Generic;
using UnityEngine;

namespace YeastSim
{
    /// <summary>
    /// Rest-shape measurements of the rigged cell model, taken once per mesh from a freshly
    /// instantiated (un-rotated, unit-scale) copy and cached.
    /// </summary>
    /// <remarks>
    /// The cell is treated as a prolate spheroid: semi-major axis <see cref="SemiMajor"/> along
    /// <see cref="AxisLocal"/> and semi-minor axis <see cref="SemiMinor"/> around it. Both are
    /// measured on the skinned rest-pose surface, so collider and bud placement match what is drawn.
    /// </remarks>
    public sealed class CellModelInfo
    {
        /// <summary>Centre of the bone cloud, relative to the instance root.</summary>
        public Vector3 CentroidLocal { get; private set; }

        /// <summary>Unit long axis of the cell, in instance-root space.</summary>
        public Vector3 AxisLocal { get; private set; }

        /// <summary>Semi-major axis of the rest surface at the model's native size.</summary>
        public float SemiMajor { get; private set; }

        /// <summary>Semi-minor axis of the rest surface at the model's native size.</summary>
        public float SemiMinor { get; private set; }

        /// <summary>Equivalent-sphere radius at native size: (a·b²)^(1/3).</summary>
        public float EquivalentRadius => Mathf.Pow(SemiMajor * SemiMinor * SemiMinor, 1f / 3f);

        /// <summary>Semi-axes relative to the equivalent radius (scale-free shape).</summary>
        public float RelativeSemiMajor => SemiMajor / EquivalentRadius;
        public float RelativeSemiMinor => SemiMinor / EquivalentRadius;

        private static readonly Dictionary<Mesh, CellModelInfo> Cache = new Dictionary<Mesh, CellModelInfo>();

        /// <summary>Measures (or returns the cached measurement of) a freshly instantiated model.</summary>
        public static CellModelInfo Get(Transform instanceRoot, SkinnedMeshRenderer renderer, IList<Transform> nodeBones)
        {
            Mesh mesh = renderer.sharedMesh;
            if (mesh != null && Cache.TryGetValue(mesh, out CellModelInfo cached)) return cached;

            var info = new CellModelInfo();
            info.Measure(instanceRoot, renderer, nodeBones);
            if (mesh != null) Cache[mesh] = info;
            return info;
        }

        private void Measure(Transform root, SkinnedMeshRenderer renderer, IList<Transform> nodeBones)
        {
            // Centroid and principal axis of the membrane nodes.
            Vector3 centroid = Vector3.zero;
            foreach (Transform bone in nodeBones) centroid += bone.position;
            centroid /= nodeBones.Count;

            float cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;
            foreach (Transform bone in nodeBones)
            {
                Vector3 d = bone.position - centroid;
                cxx += d.x * d.x; cxy += d.x * d.y; cxz += d.x * d.z;
                cyy += d.y * d.y; cyz += d.y * d.z; czz += d.z * d.z;
            }

            // Power iteration for the dominant eigenvector of the covariance matrix.
            Vector3 axis = new Vector3(0.11f, 0.23f, 0.97f).normalized;
            for (int i = 0; i < 64; i++)
            {
                var next = new Vector3(cxx * axis.x + cxy * axis.y + cxz * axis.z,
                                       cxy * axis.x + cyy * axis.y + cyz * axis.z,
                                       cxz * axis.x + cyz * axis.y + czz * axis.z);
                if (next.sqrMagnitude < 1e-20f) break;
                axis = next.normalized;
            }

            // Semi-axes of the rendered rest surface (falls back to the bones if the mesh can't be read).
            float major = 0f, minor = 0f;
            if (!TryMeasureSkinnedSurface(renderer, centroid, axis, ref major, ref minor))
            {
                Debug.LogWarning("[YeastSim] Could not read the cell mesh (enable Read/Write on the model import " +
                                 "settings); estimating the cell outline from the bones instead.");
                foreach (Transform bone in nodeBones)
                {
                    Vector3 d = bone.position - centroid;
                    float along = Mathf.Abs(Vector3.Dot(d, axis));
                    major = Mathf.Max(major, along);
                    minor = Mathf.Max(minor, (d - Vector3.Dot(d, axis) * axis).magnitude);
                }
                // Bones sit slightly inside the rendered surface.
                major *= 1.14f;
                minor *= 1.14f;
            }

            CentroidLocal = root.InverseTransformPoint(centroid);
            AxisLocal = root.InverseTransformDirection(axis).normalized;
            SemiMajor = Mathf.Max(major, minor);
            SemiMinor = Mathf.Min(major, minor);
        }

        /// <summary>Linear-blend skins the rest mesh on the CPU and measures its extent along/around the axis.</summary>
        private static bool TryMeasureSkinnedSurface(SkinnedMeshRenderer renderer, Vector3 centroid, Vector3 axis,
                                                     ref float major, ref float minor)
        {
            Mesh mesh = renderer.sharedMesh;
            if (mesh == null || !mesh.isReadable) return false;

            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4[] bindposes = mesh.bindposes;
            Transform[] bones = renderer.bones;
            if (vertices.Length == 0 || weights.Length != vertices.Length || bindposes.Length != bones.Length) return false;

            var skin = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                skin[i] = bones[i] != null ? bones[i].localToWorldMatrix * bindposes[i] : Matrix4x4.zero;

            for (int v = 0; v < vertices.Length; v++)
            {
                BoneWeight w = weights[v];
                Vector3 p = skin[w.boneIndex0].MultiplyPoint3x4(vertices[v]) * w.weight0
                          + skin[w.boneIndex1].MultiplyPoint3x4(vertices[v]) * w.weight1
                          + skin[w.boneIndex2].MultiplyPoint3x4(vertices[v]) * w.weight2
                          + skin[w.boneIndex3].MultiplyPoint3x4(vertices[v]) * w.weight3;

                Vector3 d = p - centroid;
                float along = Vector3.Dot(d, axis);
                major = Mathf.Max(major, Mathf.Abs(along));
                minor = Mathf.Max(minor, (d - along * axis).magnitude);
            }
            return major > 0f && minor > 0f;
        }

        /// <summary>
        /// Distance from the centre to the surface of a spheroid with semi-axes <paramref name="a"/>
        /// (along local z) and <paramref name="b"/>, in the unit direction <paramref name="localDirection"/>.
        /// </summary>
        public static float SpheroidRadius(Vector3 localDirection, float a, float b)
        {
            float c = localDirection.z;
            float s2 = Mathf.Max(0f, 1f - c * c);
            return 1f / Mathf.Sqrt(c * c / (a * a) + s2 / (b * b));
        }
    }
}
