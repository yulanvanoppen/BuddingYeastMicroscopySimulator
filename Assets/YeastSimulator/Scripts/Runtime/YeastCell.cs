using System.Collections.Generic;
using UnityEngine;

namespace YeastSim
{
    /// <summary>Cell-cycle phase of a <see cref="YeastCell"/>.</summary>
    public enum CellPhase
    {
        /// <summary>A bud, still attached to its mother and growing.</summary>
        Bud,
        /// <summary>Separated cell growing (daughters) or resting (mothers) before its next bud.</summary>
        G1,
        /// <summary>A mother with an attached bud.</summary>
        Budding,
    }

    /// <summary>
    /// One soft-bodied yeast cell built from the rigged cell model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Physics layout: a central <b>core</b> rigidbody with a capsule collider, plus one small
    /// rigidbody + sphere collider per membrane bone of the model. Each membrane node is pulled
    /// towards its rest position on the cell outline by a single spring joint to the core, so
    /// the outline dents where neighbouring cells press on it and springs back afterwards.
    /// The skinned mesh follows the bones, so the drawn cell deforms with the physics.
    /// </para>
    /// <para>
    /// Growth only changes the rest positions (joint anchors) and the bone scale; nothing is
    /// teleported. Cells keep a fixed orientation (rotations are frozen), which keeps the joints
    /// cheap and stable. All lengths are in micrometres.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class YeastCell : MonoBehaviour
    {
        // ---- identity and lineage -------------------------------------------------------
        public int Id { get; internal set; }
        public int MotherId { get; internal set; }
        public int ColonyId { get; internal set; }
        /// <summary>0 for founders, mother's generation + 1 for daughters.</summary>
        public int Generation { get; internal set; }
        public Color32 LabelColor { get; internal set; }

        // ---- cell cycle -------------------------------------------------------------------
        public CellPhase Phase { get; internal set; }
        /// <summary>Mother this cell budded from (null for founders). Kept after division.</summary>
        public YeastCell Mother { get; internal set; }
        /// <summary>Bud currently attached to this cell, if any.</summary>
        public YeastCell AttachedBud { get; internal set; }
        public bool IsAttachedBud => Phase == CellPhase.Bud;
        public int BudCount { get; internal set; }
        /// <summary>Direction (world) of the last bud site, or of the birth scar for new daughters.</summary>
        public Vector3 LastBudDirection { get; internal set; }

        internal float PhaseStartMinute;
        internal float PhaseDuration;
        internal float PhaseStartRadius;
        internal float PhaseEndRadius;

        // ---- geometry ---------------------------------------------------------------------
        /// <summary>Current equivalent-sphere radius (µm).</summary>
        public float Radius { get; private set; }
        public float AdultRadius { get; internal set; }
        /// <summary>Unit long axis (world). Fixed for the life of the cell.</summary>
        public Vector3 LongAxis { get; private set; }
        public float SemiMajor => _info.RelativeSemiMajor * Radius;
        public float SemiMinor => _info.RelativeSemiMinor * Radius;
        public Vector3 Position => Core.position;

        public Rigidbody Core { get; private set; }
        public SkinnedMeshRenderer Renderer { get; private set; }
        public IReadOnlyList<Collider> Colliders => _colliders;

        private CellModelInfo _info;
        private Quaternion _coreRotation;
        private float _appliedScale = -1f;

        private readonly List<Collider> _colliders = new List<Collider>();
        private readonly List<Node> _nodes = new List<Node>();
        private readonly List<ScaledBone> _otherBones = new List<ScaledBone>();
        private CapsuleCollider _coreCollider;
        private float _coreColliderFraction;

        /// <summary>Multiplier (0–1) on the neck overlap; faded to 0 before division.</summary>
        internal float NeckOverlapScale = 1f;

        /// <summary>Cap on how fast PhysX separates overlapping cells (µm/s of physics time).</summary>
        private const float MaxDepenetrationVelocity = 1f;

        private SpringJoint _neck;
        private Vector3 _neckDirection;
        private float _neckOverlapFraction;

        private struct Node
        {
            public Transform Bone;
            public Vector3 RestOffset;   // core-local offset at native model size
            public Vector3 BaseScale;    // bone localScale at native model size
            public SpringJoint Joint;
        }

        private struct ScaledBone
        {
            public Transform Bone;
            public Vector3 RestOffset;
            public Vector3 BaseScale;
        }

        #region Construction
        /// <summary>
        /// Turns a freshly instantiated, un-rotated model into a physical cell centred on
        /// <paramref name="centre"/> with its long axis along <paramref name="longAxis"/>.
        /// </summary>
        internal void Build(CellPhysicsSettings physics, float radius, Vector3 centre, Vector3 longAxis, int layer)
        {
            Transform root = transform;
            root.rotation = Quaternion.identity;
            root.localScale = Vector3.one;

            // The model's Animator (no controller) would only fight the physics.
            foreach (Animator animator in GetComponentsInChildren<Animator>()) Destroy(animator);

            Renderer = GetComponentInChildren<SkinnedMeshRenderer>();
            if (Renderer == null)
            {
                Debug.LogError("[YeastSim] The cell model has no SkinnedMeshRenderer.", this);
                return;
            }
            Renderer.updateWhenOffscreen = true;   // bounds would otherwise lag behind the moving bones

            // Leaf bones are membrane nodes; any parent bones (e.g. a root bone) ride along with the core.
            var boneSet = new HashSet<Transform>(Renderer.bones);
            boneSet.Remove(null);
            var nodeBones = new List<Transform>();
            var otherBones = new List<Transform>();
            foreach (Transform bone in boneSet)
            {
                bool hasBoneChild = false;
                foreach (Transform child in bone)
                    if (boneSet.Contains(child)) { hasBoneChild = true; break; }
                (hasBoneChild ? otherBones : nodeBones).Add(bone);
            }
            if (nodeBones.Count < 4)
            {
                Debug.LogError("[YeastSim] The cell model needs at least 4 membrane bones.", this);
                return;
            }

            _info = CellModelInfo.Get(root, Renderer, nodeBones);
            LongAxis = longAxis.normalized;

            // Rotate the model so its long axis matches, and centre it.
            root.rotation = Quaternion.FromToRotation(_info.AxisLocal, LongAxis);
            root.position = centre - root.rotation * _info.CentroidLocal;

            Vector3 up = Mathf.Abs(Vector3.Dot(LongAxis, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            _coreRotation = Quaternion.LookRotation(LongAxis, up);

            // Core body.
            var coreObject = new GameObject("Core");
            coreObject.transform.SetParent(root, false);
            coreObject.transform.SetPositionAndRotation(centre, _coreRotation);
            Core = coreObject.AddComponent<Rigidbody>();
            ConfigureBody(Core, physics.CoreMass, physics.LinearDamping);
            _coreCollider = coreObject.AddComponent<CapsuleCollider>();
            _coreCollider.direction = 2;   // local z = long axis
            _coreColliderFraction = physics.CoreColliderFraction;
            _colliders.Add(_coreCollider);

            Quaternion toCore = Quaternion.Inverse(_coreRotation);

            // Detach the bones from the model hierarchy so each can be its own rigidbody.
            foreach (Transform bone in otherBones)
            {
                Vector3 offset = toCore * (bone.position - centre);
                bone.SetParent(coreObject.transform, true);
                _otherBones.Add(new ScaledBone { Bone = bone, RestOffset = offset, BaseScale = bone.localScale });
            }
            foreach (Transform bone in nodeBones)
            {
                Vector3 offset = toCore * (bone.position - centre);
                bone.SetParent(root, true);
                _nodes.Add(new Node { Bone = bone, RestOffset = offset, BaseScale = bone.localScale });
            }

            // Grow everything to the requested size before adding bodies, so nothing is teleported later.
            Radius = radius;
            float scale = radius / _info.EquivalentRadius;
            for (int i = 0; i < _otherBones.Count; i++)
            {
                ScaledBone b = _otherBones[i];
                b.Bone.position = centre + _coreRotation * (b.RestOffset * scale);
                b.Bone.localScale = b.BaseScale * scale;
            }

            float nodeRadius = physics.NodeColliderFraction * radius;
            for (int i = 0; i < _nodes.Count; i++)
            {
                Node node = _nodes[i];
                Transform bone = node.Bone;
                bone.position = centre + _coreRotation * (node.RestOffset * scale);
                bone.localScale = node.BaseScale * scale;

                var body = bone.gameObject.AddComponent<Rigidbody>();
                ConfigureBody(body, physics.NodeMass, physics.LinearDamping);

                // Place the collider so its outer edge sits on the drawn surface. Bone scale grows
                // with the cell, so these local values stay correct as the cell grows.
                Vector3 localDir = node.RestOffset.normalized;
                float surface = CellModelInfo.SpheroidRadius(localDir, SemiMajor, SemiMinor);
                float boneDistance = node.RestOffset.magnitude * scale;
                Vector3 centreOffsetWorld = (_coreRotation * localDir) * (surface - nodeRadius - boneDistance);
                var sphere = bone.gameObject.AddComponent<SphereCollider>();
                sphere.center = bone.InverseTransformVector(centreOffsetWorld);
                sphere.radius = nodeRadius / MaxScale(bone.lossyScale);   // PhysX scales spheres by the largest axis
                _colliders.Add(sphere);

                var joint = bone.gameObject.AddComponent<SpringJoint>();
                joint.autoConfigureConnectedAnchor = false;
                joint.connectedBody = Core;
                joint.anchor = Vector3.zero;
                joint.connectedAnchor = node.RestOffset * scale;
                joint.spring = physics.MembraneStiffness;
                joint.damper = physics.MembraneDamping;
                joint.minDistance = 0f;
                joint.maxDistance = 0f;
                joint.tolerance = 0.0001f;
                joint.enableCollision = false;

                node.Joint = joint;
                _nodes[i] = node;
            }

            // A cell's own parts never collide with each other.
            for (int i = 0; i < _colliders.Count; i++)
                for (int j = i + 1; j < _colliders.Count; j++)
                    Physics.IgnoreCollision(_colliders[i], _colliders[j], true);

            SetLayerRecursively(root, layer);
            _appliedScale = scale;
            UpdateCoreCollider();
        }

        private static void ConfigureBody(Rigidbody body, float mass, float damping)
        {
            body.mass = mass;
            body.linearDamping = damping;
            body.angularDamping = damping;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.sleepThreshold = 0f;   // growth moves rest positions; sleeping bodies would ignore that
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.maxDepenetrationVelocity = MaxDepenetrationVelocity;
        }

        private static float MaxScale(Vector3 s) => Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));

        private static void SetLayerRecursively(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayerRecursively(child, layer);
        }
        #endregion

        #region Growth
        /// <summary>Sets the equivalent radius (µm). Cheap when the size barely changed.</summary>
        public void SetRadius(float radius)
        {
            Radius = radius;
            float scale = radius / _info.EquivalentRadius;
            if (Mathf.Abs(scale - _appliedScale) <= 0.0005f * _appliedScale) return;
            _appliedScale = scale;

            for (int i = 0; i < _nodes.Count; i++)
            {
                Node node = _nodes[i];
                node.Bone.localScale = node.BaseScale * scale;
                node.Joint.connectedAnchor = node.RestOffset * scale;
            }
            for (int i = 0; i < _otherBones.Count; i++)
            {
                ScaledBone b = _otherBones[i];
                b.Bone.localPosition = b.RestOffset * scale;   // parent is the core: core-local offsets
                b.Bone.localScale = b.BaseScale * scale;
            }

            UpdateCoreCollider();
            if (_neck != null) UpdateNeck();
        }

        private void UpdateCoreCollider()
        {
            float radius = _coreColliderFraction * SemiMinor;
            _coreCollider.radius = radius;
            _coreCollider.height = Mathf.Max(2f * radius, 2f * _coreColliderFraction * SemiMajor);
        }

        /// <summary>Distance from the centre to the rest surface in a world direction.</summary>
        public float SurfaceDistance(Vector3 worldDirection)
        {
            Vector3 local = Quaternion.Inverse(_coreRotation) * worldDirection.normalized;
            return CellModelInfo.SpheroidRadius(local, SemiMajor, SemiMinor);
        }
        #endregion

        #region Bud neck
        /// <summary>
        /// Holds this bud against <paramref name="mother"/> on the side given by
        /// <paramref name="direction"/> (mother → bud) until <see cref="DetachFromMother"/>.
        /// Bud and mother don't collide while attached (they overlap slightly at the neck).
        /// </summary>
        internal void AttachToMother(YeastCell mother, Vector3 direction, CellPhysicsSettings physics, float neckOverlapFraction)
        {
            _neckDirection = direction.normalized;
            _neckOverlapFraction = neckOverlapFraction;

            _neck = Core.gameObject.AddComponent<SpringJoint>();
            _neck.autoConfigureConnectedAnchor = false;
            _neck.connectedBody = mother.Core;
            _neck.spring = physics.NeckStiffness;
            _neck.damper = physics.NeckDamping;
            _neck.minDistance = 0f;
            _neck.maxDistance = 0f;
            _neck.tolerance = 0.0001f;
            _neck.enableCollision = false;

            SetCollisionsWith(mother, false);
            UpdateNeck();
        }

        /// <summary>Re-computes the neck anchors for the current sizes of bud and mother.</summary>
        internal void UpdateNeck()
        {
            if (_neck == null || Mother == null) return;

            Vector3 budSide = -_neckDirection;
            float budDistance = SurfaceDistance(budSide) - NeckOverlapScale * _neckOverlapFraction * Radius;
            _neck.anchor = Quaternion.Inverse(_coreRotation) * budSide * budDistance;
            _neck.connectedAnchor = Quaternion.Inverse(Mother._coreRotation) * _neckDirection * Mother.SurfaceDistance(_neckDirection);
        }

        /// <summary>Cytokinesis: removes the neck and lets bud and mother collide again.</summary>
        internal void DetachFromMother()
        {
            if (_neck != null)
            {
                // Immediate: several physics steps can run before the end of this frame.
                DestroyImmediate(_neck);
                _neck = null;
            }
            if (Mother != null) SetCollisionsWith(Mother, true);
        }

        private void SetCollisionsWith(YeastCell other, bool collide)
        {
            foreach (Collider a in _colliders)
                foreach (Collider b in other._colliders)
                    Physics.IgnoreCollision(a, b, !collide);
        }
        #endregion
    }
}
