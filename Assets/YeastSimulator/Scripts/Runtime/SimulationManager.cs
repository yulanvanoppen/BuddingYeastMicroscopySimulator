using System;
using System.Collections.Generic;
using UnityEngine;
using PhysicsApi = UnityEngine.Physics;

namespace YeastSim
{
    /// <summary>
    /// Runs the colony simulation: seeds founder cells in several colonies, drives the cell cycle
    /// (bud emergence, growth, division) and steps PhysX manually.
    /// </summary>
    /// <remarks>
    /// Physics runs in <see cref="SimulationMode.Script"/> mode: <see cref="Update"/> advances a
    /// fixed number of steps per rendered frame, independent of real time, so a slow frame never
    /// changes the result and the recorder can stop the clock exactly at a capture time
    /// (<see cref="RunUntil"/>).
    /// Simulated time is measured in minutes; 1 Unity unit = 1 µm.
    /// </remarks>
    [DefaultExecutionOrder(-100)]
    public sealed class SimulationManager : MonoBehaviour
    {
        [Header("Cell model")]
        [Tooltip("Rigged cell model (budding_yeast.fbx): a skinned mesh whose leaf bones become membrane nodes.")]
        [SerializeField] private GameObject _cellModel;

        [Tooltip("Material used to draw cells in the scene view / game view (not used for exported images).")]
        [SerializeField] private Material _displayMaterial;

        [Tooltip("Tint each cell with its label colour in the game view.")]
        [SerializeField] private bool _showLabelColors = true;

        [Tooltip("Layer the cells are put on; the capture camera renders only this layer. Any unused layer works.")]
        [Range(8, 31)] [SerializeField] private int _cellLayer = 31;

        [Header("Settings")]
        public CellGeometrySettings Geometry = new CellGeometrySettings();
        public CellPhysicsSettings CellPhysics = new CellPhysicsSettings();
        public CellCycleSettings Cycle = new CellCycleSettings();
        public EnvironmentSettings Environment = new EnvironmentSettings();
        public TimeSettings Timing = new TimeSettings();
        public MicroscopeSettings Microscope = new MicroscopeSettings();

        [Header("Labels")]
        [Tooltip("How label colours are assigned to new cells.")]
        public ColorAssignment ColorAssignment = ColorAssignment.NeighbourAware;

        [Header("Randomness")]
        [Tooltip("Random seed. 0 = different every run. The recorder overrides this for batch runs.")]
        public int Seed;

        // ---- state ------------------------------------------------------------------------------
        /// <summary>Simulated time (minutes) = steps taken × minutes per step (no accumulated rounding).</summary>
        public float SimMinutes => (float)(_step * (double)Timing.MinutesPerStep);
        public long Steps => _step;
        public IReadOnlyList<YeastCell> Cells => _cells;
        public IReadOnlyList<LineageRecord> Lineage => _lineage;
        public int CellLayer => _cellLayer;
        public bool IsInitialized { get; private set; }
        public int ActiveSeed { get; private set; }

        /// <summary>True while the clock is held at the time requested with <see cref="RunUntil"/>.</summary>
        public bool IsStopped => _stopStep >= 0 && _step >= _stopStep;

        /// <summary>Raised after a bud emerges (bud, mother).</summary>
        public event Action<YeastCell, YeastCell> BudEmerged;
        /// <summary>Raised after a bud separates from its mother (daughter, mother).</summary>
        public event Action<YeastCell, YeastCell> CellDivided;

        private readonly List<YeastCell> _cells = new List<YeastCell>();
        private readonly List<LineageRecord> _lineage = new List<LineageRecord>();
        private readonly Dictionary<int, LineageRecord> _lineageById = new Dictionary<int, LineageRecord>();
        private readonly List<Vector3> _colonyCentroids = new List<Vector3>();
        private readonly List<int> _colonySizes = new List<int>();
        private readonly List<Color32> _neighbourColors = new List<Color32>();
        private LabelPalette _palette;
        private System.Random _random;
        private int _nextId = 1;
        private long _step;
        private long _stopStep = -1;
        private Transform _cellParent;
        private MaterialPropertyBlock _block;

        /// <summary>Gap kept between cells and the slide so they don't scrape along it (µm).</summary>
        private const float SlideClearance = 0.05f;

        // Physics settings are project-wide and changes made in Play Mode would stick in the editor,
        // so they are saved and restored around the lifetime of the simulation(s).
        private static int s_instances;
        private static SimulationMode s_savedMode;
        private static Vector3 s_savedGravity;
        private static int s_savedIterations, s_savedVelocityIterations;
        private static float s_savedContactOffset;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        #region Lifecycle
        private void Awake()
        {
            if (s_instances++ == 0)
            {
                s_savedMode = PhysicsApi.simulationMode;
                s_savedGravity = PhysicsApi.gravity;
                s_savedIterations = PhysicsApi.defaultSolverIterations;
                s_savedVelocityIterations = PhysicsApi.defaultSolverVelocityIterations;
                s_savedContactOffset = PhysicsApi.defaultContactOffset;
            }
            PhysicsApi.simulationMode = SimulationMode.Script;
            PhysicsApi.defaultSolverIterations = Timing.SolverIterations;
            PhysicsApi.defaultSolverVelocityIterations = Mathf.Max(1, Timing.SolverIterations / 4);
            PhysicsApi.defaultContactOffset = 0.02f;
            PhysicsApi.gravity = Vector3.zero;   // confinement is applied explicitly
        }

        private void Start()
        {
            if (!IsInitialized) Initialize(Seed);
        }

        private void OnDestroy()
        {
            if (--s_instances > 0) return;
            PhysicsApi.simulationMode = s_savedMode;
            PhysicsApi.gravity = s_savedGravity;
            PhysicsApi.defaultSolverIterations = s_savedIterations;
            PhysicsApi.defaultSolverVelocityIterations = s_savedVelocityIterations;
            PhysicsApi.defaultContactOffset = s_savedContactOffset;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_instances = 0;

        /// <summary>Seeds the colonies. Called automatically in Start, or by the recorder with a run seed.</summary>
        public void Initialize(int seed)
        {
            if (IsInitialized) return;
            if (_cellModel == null)
            {
                Debug.LogError("[YeastSim] No cell model assigned to the SimulationManager.", this);
                enabled = false;
                return;
            }

            ActiveSeed = seed != 0 ? seed : (int)(DateTime.Now.Ticks & 0x7FFFFFFF) | 1;
            _random = new System.Random(ActiveSeed);
            UnityEngine.Random.InitState(ActiveSeed);
            _palette = new LabelPalette();
            _block = new MaterialPropertyBlock();

            _cellParent = new GameObject("Cells").transform;
            BuildSlide();
            SeedColonies();
            IsInitialized = true;
        }

        private void Update()
        {
            if (!IsInitialized) return;
            for (int i = 0; i < Timing.StepsPerFrame; i++)
            {
                if (IsStopped) break;
                Step();
            }
        }

        /// <summary>
        /// Lets the simulation run until <paramref name="minute"/> (rounded to the nearest physics
        /// step), then holds it there.
        /// </summary>
        public void RunUntil(float minute) => _stopStep = Math.Max(0L, (long)Math.Round(minute / (double)Timing.MinutesPerStep));

        /// <summary>Removes the stop so the simulation runs freely.</summary>
        public void RunFreely() => _stopStep = -1;
        #endregion

        #region Setup
        private void BuildSlide()
        {
            // Glass slide (floor) and coverslip (ceiling): colliders only, so they never appear in captures.
            var slide = new GameObject("Slide (collider)");
            slide.transform.SetParent(transform, false);
            var floor = slide.AddComponent<BoxCollider>();
            float size = Microscope.FieldSize * 4f + 200f;
            floor.size = new Vector3(size, 10f, size);
            floor.center = new Vector3(Microscope.FieldCentre.x, -5f, Microscope.FieldCentre.y);

            if (Environment.CoverslipGap > 0f)
            {
                var coverslip = new GameObject("Coverslip (collider)");
                coverslip.transform.SetParent(transform, false);
                var ceiling = coverslip.AddComponent<BoxCollider>();
                ceiling.size = new Vector3(size, 10f, size);
                ceiling.center = new Vector3(Microscope.FieldCentre.x, Environment.CoverslipGap + 5f, Microscope.FieldCentre.y);
            }
        }

        private void SeedColonies()
        {
            float half = Microscope.FieldSize * 0.5f - Environment.SeedMargin;
            if (half <= 0f) half = Microscope.FieldSize * 0.25f;
            Vector2 centre = Microscope.FieldCentre;

            var seeds = new List<Vector2>();
            for (int attempt = 0; attempt < 2000 && seeds.Count < Environment.ColonyCount; attempt++)
            {
                var p = new Vector2(centre.x + RandomRange(-half, half), centre.y + RandomRange(-half, half));
                bool farEnough = true;
                foreach (Vector2 q in seeds)
                    if ((p - q).magnitude < Environment.MinColonySpacing) { farEnough = false; break; }
                if (farEnough) seeds.Add(p);
            }
            if (seeds.Count < Environment.ColonyCount)
                Debug.LogWarning($"[YeastSim] Only room for {seeds.Count} of {Environment.ColonyCount} colonies; " +
                                 "lower the colony spacing or margin, or enlarge the field of view.");

            for (int colony = 0; colony < seeds.Count; colony++)
            {
                _colonyCentroids.Add(new Vector3(seeds[colony].x, 0f, seeds[colony].y));
                _colonySizes.Add(0);

                for (int f = 0; f < Environment.FoundersPerColony; f++)
                {
                    float adult = SampleAdultRadius();
                    // Several founders: a small ring around the seed.
                    Vector2 offset = f == 0 ? Vector2.zero
                        : RandomOnCircle() * (2.5f * Geometry.AdultRadius * Mathf.Sqrt(f));
                    Vector2 p = seeds[colony] + offset;
                    Vector3 axis = HorizontalDirection(RandomRange(0f, 360f));

                    YeastCell cell = CreateCell(adult, adult, new Vector3(p.x, 0f, p.y), axis, colony + 1, mother: null);
                    if (cell == null) return;

                    // Founders start at a random point of G1 so colonies don't bud in lock-step.
                    cell.Phase = CellPhase.G1;
                    cell.PhaseDuration = Vary(Cycle.MotherG1Minutes + Cycle.BudGrowthMinutes);
                    cell.PhaseStartMinute = -RandomRange(0f, cell.PhaseDuration);
                    cell.PhaseStartRadius = cell.PhaseEndRadius = adult;
                    cell.LastBudDirection = _random.NextDouble() < 0.5 ? axis : -axis;
                    _lineageById[cell.Id].DivisionMinute = 0f;
                }
            }
        }
        #endregion

        #region Stepping
        private void Step()
        {
            _step++;

            UpdateColonyCentroids();

            // Iterate over the cells that existed at the start of the step; new buds start next step.
            int count = _cells.Count;
            for (int i = 0; i < count; i++) UpdateCellCycle(_cells[i]);
            for (int i = 0; i < count; i++) ApplyForces(_cells[i]);

            PhysicsApi.Simulate(Timing.PhysicsTimestep);
        }

        private void UpdateCellCycle(YeastCell cell)
        {
            float progress = cell.PhaseDuration > 0f
                ? Mathf.Clamp01((SimMinutes - cell.PhaseStartMinute) / cell.PhaseDuration)
                : 1f;

            switch (cell.Phase)
            {
                case CellPhase.Bud:
                    // Close the overlap at the neck during the last 20% of bud growth, so bud and
                    // mother just touch (and don't jump apart) when collisions resume at division.
                    cell.NeckOverlapScale = Mathf.Clamp01((1f - progress) / 0.2f);
                    cell.SetRadius(VolumeLerp(cell.PhaseStartRadius, cell.PhaseEndRadius, progress));
                    if (progress >= 1f) Divide(cell);
                    break;

                case CellPhase.G1:
                    cell.SetRadius(VolumeLerp(cell.PhaseStartRadius, cell.PhaseEndRadius, progress));
                    if (progress >= 1f && _cells.Count < Cycle.MaxCells) StartBud(cell);
                    break;

                case CellPhase.Budding:
                    break;   // the attached bud drives division
            }
        }

        private void ApplyForces(YeastCell cell)
        {
            Rigidbody core = cell.Core;
            Vector3 p = core.position;

            // Keep the cell resting on the slide; neighbours can still push it up (piling).
            float restHeight = cell.SemiMinor + SlideClearance;
            var force = new Vector3(0f, Environment.PlaneStiffness * (restHeight - p.y), 0f);

            // Weak pull towards the colony centre keeps colonies compact.
            if (Environment.Cohesion > 0f && cell.ColonyId > 0)
            {
                Vector3 toCentre = _colonyCentroids[cell.ColonyId - 1] - p;
                toCentre.y = 0f;
                float distance = toCentre.magnitude;
                if (distance > cell.Radius) force += toCentre / distance * Environment.Cohesion;
            }

            if (Environment.Jitter > 0f)
            {
                Vector2 noise = RandomOnCircle() * (Environment.Jitter * (float)_random.NextDouble());
                force += new Vector3(noise.x, 0f, noise.y);
            }

            core.AddForce(force, ForceMode.Force);
        }

        private void UpdateColonyCentroids()
        {
            for (int c = 0; c < _colonyCentroids.Count; c++) { _colonyCentroids[c] = Vector3.zero; _colonySizes[c] = 0; }
            foreach (YeastCell cell in _cells)
            {
                int c = cell.ColonyId - 1;
                _colonyCentroids[c] += cell.Position;
                _colonySizes[c]++;
            }
            for (int c = 0; c < _colonyCentroids.Count; c++)
                if (_colonySizes[c] > 0) _colonyCentroids[c] /= _colonySizes[c];
        }
        #endregion

        #region Cell cycle events
        private void StartBud(YeastCell mother)
        {
            Vector3 direction = ChooseBudDirection(mother);
            float adult = SampleAdultRadius();
            float budRadius = Geometry.BudInitialRadiusFraction * adult;

            // Start the bud at its neck position so the joint has nothing to correct
            // (same geometry as YeastCell.UpdateNeck: the bud's long axis points along the direction).
            float distance = mother.SurfaceDistance(direction)
                           + budRadius * CellShapeAspect() - Geometry.NeckOverlapFraction * budRadius;
            Vector3 position = mother.Position + direction * distance;

            YeastCell bud = CreateCell(budRadius, adult, position, direction, mother.ColonyId, mother);
            if (bud == null) return;

            bud.Phase = CellPhase.Bud;
            bud.PhaseStartMinute = SimMinutes;
            bud.PhaseDuration = Vary(Cycle.BudGrowthMinutes);
            bud.PhaseStartRadius = budRadius;
            bud.PhaseEndRadius = Geometry.DivisionRadiusFraction * adult;
            bud.LastBudDirection = -direction;   // the birth scar faces the mother
            bud.AttachToMother(mother, direction, CellPhysics, Geometry.NeckOverlapFraction);

            mother.Phase = CellPhase.Budding;
            mother.AttachedBud = bud;
            mother.LastBudDirection = direction;
            mother.BudCount++;
            _lineageById[mother.Id].BudCount = mother.BudCount;

            BudEmerged?.Invoke(bud, mother);
        }

        private void Divide(YeastCell bud)
        {
            YeastCell mother = bud.Mother;
            bud.DetachFromMother();

            bud.Phase = CellPhase.G1;
            bud.PhaseStartMinute = SimMinutes;
            bud.PhaseDuration = Vary(Cycle.DaughterG1Minutes);
            bud.PhaseStartRadius = bud.Radius;
            bud.PhaseEndRadius = bud.AdultRadius;
            _lineageById[bud.Id].DivisionMinute = SimMinutes;

            if (mother != null)
            {
                mother.AttachedBud = null;
                mother.Phase = CellPhase.G1;
                mother.PhaseStartMinute = SimMinutes;
                mother.PhaseDuration = Vary(Cycle.MotherG1Minutes);
                mother.PhaseStartRadius = mother.Radius;
                mother.PhaseEndRadius = Mathf.Max(mother.Radius, mother.AdultRadius);
            }

            CellDivided?.Invoke(bud, mother);
        }

        /// <summary>Bud site for the next bud of <paramref name="mother"/>, following the budding pattern.</summary>
        private Vector3 ChooseBudDirection(YeastCell mother)
        {
            Vector3 axis = mother.LongAxis;
            Vector3 last = mother.LastBudDirection.sqrMagnitude > 0.5f ? mother.LastBudDirection.normalized : axis;
            bool firstBud = mother.BudCount == 0;
            Vector3 direction;

            switch (Cycle.Pattern)
            {
                case BuddingPattern.Axial:
                    // Next to the previous bud site; a daughter's first bud is next to its birth scar.
                    direction = RotateAboutUp(last, RandomSign() * RandomRange(25f, 45f));
                    break;

                case BuddingPattern.Bipolar:
                    if (firstBud && mother.Mother != null)
                        direction = -last;   // distal pole, away from the birth scar
                    else
                        direction = _random.NextDouble() < 0.5 ? axis : -axis;
                    direction = RotateAboutUp(direction, RandomRange(-20f, 20f));
                    break;

                default:
                    direction = HorizontalDirection(RandomRange(0f, 360f));
                    break;
            }

            // Keep buds near the slide plane, with a little random tilt (allows mild piling).
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) direction = HorizontalDirection(RandomRange(0f, 360f));
            direction.Normalize();
            float tilt = RandomRange(-Cycle.BudTiltDegrees, Cycle.BudTiltDegrees);
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
            return Quaternion.AngleAxis(tilt, side) * direction;
        }

        /// <summary>Semi-major / equivalent radius of the cell shape (≈1.1 for the default model).</summary>
        private float CellShapeAspect() => _cells.Count > 0 ? _cells[0].SemiMajor / _cells[0].Radius : 1f;
        #endregion

        #region Cell creation
        private YeastCell CreateCell(float radius, float adultRadius, Vector3 centre, Vector3 longAxis, int colonyId, YeastCell mother)
        {
            GameObject instance = Instantiate(_cellModel, centre, Quaternion.identity, _cellParent);
            var cell = instance.AddComponent<YeastCell>();
            cell.Id = _nextId++;
            instance.name = $"Cell {cell.Id}";
            cell.ColonyId = colonyId;
            cell.Mother = mother;
            cell.MotherId = mother != null ? mother.Id : 0;
            cell.Generation = mother != null ? mother.Generation + 1 : 0;
            cell.AdultRadius = adultRadius;

            cell.Build(CellPhysics, radius, centre, longAxis, _cellLayer);
            if (cell.Core == null)
            {
                Destroy(instance);
                enabled = false;
                return null;
            }

            cell.LabelColor = _palette.Take(ColorAssignment, CollectNeighbourColors(centre));
            ApplyDisplayMaterial(cell);

            _cells.Add(cell);
            var record = new LineageRecord
            {
                CellId = cell.Id,
                MotherId = cell.MotherId,
                ColonyId = colonyId,
                Generation = cell.Generation,
                Color = cell.LabelColor,
                EmergenceMinute = SimMinutes,
            };
            _lineage.Add(record);
            _lineageById[cell.Id] = record;
            return cell;
        }

        private List<Color32> CollectNeighbourColors(Vector3 position)
        {
            _neighbourColors.Clear();
            float range = 4f * Geometry.AdultRadius;
            foreach (YeastCell other in _cells)
                if ((other.Position - position).sqrMagnitude < range * range) _neighbourColors.Add(other.LabelColor);
            return _neighbourColors;
        }

        private void ApplyDisplayMaterial(YeastCell cell)
        {
            if (_displayMaterial != null) cell.Renderer.sharedMaterial = _displayMaterial;
            if (!_showLabelColors) return;

            cell.Renderer.GetPropertyBlock(_block);
            Color color = cell.LabelColor;
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            cell.Renderer.SetPropertyBlock(_block);
        }

        public LineageRecord GetLineage(int cellId) => _lineageById.TryGetValue(cellId, out LineageRecord r) ? r : null;
        #endregion

        #region Random helpers
        private float RandomRange(float min, float max) => min + (float)_random.NextDouble() * (max - min);
        private float RandomSign() => _random.NextDouble() < 0.5 ? -1f : 1f;

        private Vector2 RandomOnCircle()
        {
            float angle = RandomRange(0f, 2f * Mathf.PI);
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private float Vary(float value) => Mathf.Max(0f, value * (1f + Cycle.DurationVariability * RandomRange(-1f, 1f)));

        private float SampleAdultRadius() =>
            Geometry.AdultRadius * (1f + Geometry.AdultRadiusVariability * RandomRange(-1f, 1f));

        private static Vector3 HorizontalDirection(float degrees) =>
            new Vector3(Mathf.Cos(degrees * Mathf.Deg2Rad), 0f, Mathf.Sin(degrees * Mathf.Deg2Rad));

        private static Vector3 RotateAboutUp(Vector3 v, float degrees) => Quaternion.AngleAxis(degrees, Vector3.up) * v;

        /// <summary>Interpolates linearly in volume, which is how cells grow, and returns the radius.</summary>
        private static float VolumeLerp(float r0, float r1, float t)
        {
            float v = Mathf.Lerp(r0 * r0 * r0, r1 * r1 * r1, t);
            return Mathf.Pow(Mathf.Max(v, 1e-9f), 1f / 3f);
        }
        #endregion
    }
}
