using System;
using UnityEngine;

namespace YeastSim
{
    /// <summary>Where a mother places its next bud.</summary>
    public enum BuddingPattern
    {
        /// <summary>Haploid-like: each bud appears next to the previous bud site (or the birth scar).</summary>
        Axial,
        /// <summary>Diploid-like: buds appear at either pole of the long axis; a daughter's first bud is at its distal pole.</summary>
        Bipolar,
        /// <summary>Uniformly random direction in the slide plane.</summary>
        Random,
    }

    /// <summary>Size and shape of cells. All lengths are in micrometres (1 Unity unit = 1 µm).</summary>
    [Serializable]
    public class CellGeometrySettings
    {
        [Tooltip("Equivalent-sphere radius of a mature cell (µm).")]
        [Min(0.1f)] public float AdultRadius = 2.4f;

        [Tooltip("Relative cell-to-cell variation of the adult radius.")]
        [Range(0f, 0.5f)] public float AdultRadiusVariability = 0.08f;

        [Tooltip("Radius of a newly emerged bud, as a fraction of its own adult radius.")]
        [Range(0.05f, 0.6f)] public float BudInitialRadiusFraction = 0.25f;

        [Tooltip("A bud separates from its mother when its radius reaches this fraction of its adult radius.")]
        [Range(0.3f, 1f)] public float DivisionRadiusFraction = 0.8f;

        [Tooltip("How far a bud sinks into its mother at the neck, as a fraction of the bud's radius.")]
        [Range(0f, 0.5f)] public float NeckOverlapFraction = 0.15f;
    }

    /// <summary>Soft-body and contact parameters.</summary>
    [Serializable]
    public class CellPhysicsSettings
    {
        [Header("Bodies")]
        [Tooltip("Mass of the central body of a cell.")]
        [Min(0.001f)] public float CoreMass = 1f;

        [Tooltip("Mass of each membrane node (one per bone of the cell model).")]
        [Min(0.0001f)] public float NodeMass = 0.05f;

        [Tooltip("Linear damping of all cell bodies. Cells live in liquid, so motion should be strongly damped.")]
        [Min(0f)] public float LinearDamping = 8f;

        [Header("Colliders")]
        [Tooltip("Radius of the hard inner core collider, as a fraction of the cell's semi-axes. Stops cells from passing through each other when squeezed hard.")]
        [Range(0.1f, 0.9f)] public float CoreColliderFraction = 0.55f;

        [Tooltip("Radius of each membrane node collider, as a fraction of the cell radius. Large enough that neighbouring nodes overlap and leave no gaps.")]
        [Range(0.1f, 0.6f)] public float NodeColliderFraction = 0.3f;

        [Header("Membrane springs")]
        [Tooltip("Stiffness pulling each membrane node back to its rest position on the cell outline. Lower = softer, more deformable cells.")]
        [Min(0f)] public float MembraneStiffness = 30f;

        [Tooltip("Damping of the membrane springs.")]
        [Min(0f)] public float MembraneDamping = 3f;

        [Header("Overlap prevention")]
        [Tooltip("Repulsion between the centres of touching cells, based on both cells' outlines. This is what " +
                 "keeps cells from being squeezed into each other; the membranes only add the soft dents. 0 = off.")]
        [Min(0f)] public float CellRepulsion = 200f;

        [Tooltip("Two cells are pushed apart once their centres are closer than this fraction of the sum of their " +
                 "outline distances. 1 = no overlap at all; 0.7 allows ~30% squeezing (visible flattening at contacts).")]
        [Range(0.6f, 1f)] public float ContactFraction = 0.7f;

        [Tooltip("Force pushing membrane nodes back out when they end up inside another cell. Stops cells from " +
                 "merging or interlocking. 0 = off.")]
        [Min(0f)] public float InterpenetrationStiffness = 150f;

        [Tooltip("Nodes are pushed out once they are inside this fraction of the other cell's outline. Below 1 " +
                 "leaves room for the normal dents where cells press together.")]
        [Range(0.5f, 1f)] public float InterpenetrationMargin = 0.8f;

        [Header("Relaxation")]
        [Tooltip("After every step, if cells overlap more than the tolerance, keep running physics with growth, " +
                 "division and the clock paused until they don't. Overlaps never accumulate and every captured " +
                 "frame is relaxed.")]
        public bool RelaxOverlaps = true;

        [Tooltip("Largest accepted overlap, as a fraction of the smaller cell's radius (beyond the Contact Fraction).")]
        [Range(0.005f, 0.2f)] public float OverlapTolerance = 0.03f;

        [Tooltip("Safety cap on relaxation steps after one simulation step.")]
        [Range(1, 2000)] public int MaxRelaxationSteps = 300;

        [Header("Bud neck")]
        [Tooltip("Around the neck, bud and mother don't collide (they overlap slightly there). Everywhere else they do. " +
                 "Half-angle of that neck region, in degrees.")]
        [Range(10f, 90f)] public float NeckZoneAngle = 60f;

        [Tooltip("Stiffness of the joint that holds a bud against its mother until division.")]
        [Min(0f)] public float NeckStiffness = 200f;

        [Tooltip("Damping of the bud neck joint.")]
        [Min(0f)] public float NeckDamping = 20f;
    }

    /// <summary>Cell-cycle timing. Times are in simulated minutes.</summary>
    [Serializable]
    public class CellCycleSettings
    {
        [Tooltip("Time from bud emergence until the bud separates from its mother (S/G2/M).")]
        [Min(1f)] public float BudGrowthMinutes = 75f;

        [Tooltip("Time a mother waits after division before it starts its next bud.")]
        [Min(0f)] public float MotherG1Minutes = 15f;

        [Tooltip("Time a new daughter grows after division before it starts its first bud.")]
        [Min(0f)] public float DaughterG1Minutes = 40f;

        [Tooltip("Relative random variation of all phase durations.")]
        [Range(0f, 0.5f)] public float DurationVariability = 0.15f;

        [Tooltip("Where mothers place their buds.")]
        public BuddingPattern Pattern = BuddingPattern.Axial;

        [Tooltip("Maximum random tilt of a bud out of the slide plane (degrees). Larger values make cells pile up more.")]
        [Range(0f, 45f)] public float BudTiltDegrees = 8f;

        [Tooltip("No new buds are started once this many cells exist.")]
        [Min(1)] public int MaxCells = 600;
    }

    /// <summary>Slide, colonies and global forces.</summary>
    [Serializable]
    public class EnvironmentSettings
    {
        [Header("Colonies")]
        [Tooltip("Number of separate colonies seeded in the field of view.")]
        [Min(1)] public int ColonyCount = 3;

        [Tooltip("Founder cells per colony.")]
        [Min(1)] public int FoundersPerColony = 1;

        [Tooltip("Minimum distance between colony seeds (µm).")]
        [Min(0f)] public float MinColonySpacing = 25f;

        [Tooltip("Keep colony seeds this far from the edge of the field of view (µm).")]
        [Min(0f)] public float SeedMargin = 12f;

        [Header("Confinement")]
        [Tooltip("Spring pulling every cell centre down to the height at which it rests on the slide. Cells can still be pushed on top of each other.")]
        [Min(0f)] public float PlaneStiffness = 20f;

        [Tooltip("Height of the coverslip above the slide (µm). Limits how far cells can pile up. 0 = no coverslip.")]
        [Min(0f)] public float CoverslipGap = 6f;

        [Header("Forces on cell centres")]
        [Tooltip("Constant force pulling cells towards their colony centre, which keeps colonies compact. These forces " +
                 "add up towards the middle of a colony; overlap relaxation keeps the squeezed cells apart.")]
        [Min(0f)] public float Cohesion = 6.5f;

        [Tooltip("Attraction between neighbouring cells that are close but not touching (cell–cell adhesion). " +
                 "Keeps colonies compact and makes cells press into each other instead of drifting apart.")]
        [Min(0f)] public float Adhesion = 4f;

        [Tooltip("Gap (µm) between two cell outlines over which adhesion acts.")]
        [Min(0f)] public float AdhesionRange = 1f;

        [Tooltip("Random force on each cell centre (thermal/flow noise).")]
        [Min(0f)] public float Jitter = 0.5f;
    }

    /// <summary>How simulated time maps onto physics steps.</summary>
    [Serializable]
    public class TimeSettings
    {
        [Tooltip("Physics timestep (s). Smaller is more stable but slower.")]
        [Range(0.002f, 0.04f)] public float PhysicsTimestep = 0.01f;

        [Tooltip("Simulated minutes per second of physics time. Higher = faster growth relative to cell motion (fewer steps per cell cycle).")]
        [Min(0.1f)] public float MinutesPerPhysicsSecond = 10f;

        [Tooltip("Physics steps run per rendered frame. Higher = faster simulation but a less responsive editor.")]
        [Range(1, 200)] public int StepsPerFrame = 20;

        [Tooltip("PhysX solver iterations.")]
        [Range(1, 30)] public int SolverIterations = 8;

        public float MinutesPerStep => PhysicsTimestep * MinutesPerPhysicsSecond;
    }

    /// <summary>The virtual microscope that produces the exported images.</summary>
    [Serializable]
    public class MicroscopeSettings
    {
        [Tooltip("Width and height of exported images (pixels).")]
        [Min(16)] public int Resolution = 512;

        [Tooltip("Size of one pixel in the sample plane (µm). 512 px × 0.15 µm ≈ 77 µm field of view.")]
        [Min(0.01f)] public float PixelSize = 0.15f;

        [Tooltip("Centre of the field of view in the slide plane (x, z in µm).")]
        public Vector2 FieldCentre = Vector2.zero;

        public float FieldSize => Resolution * PixelSize;
    }
}
