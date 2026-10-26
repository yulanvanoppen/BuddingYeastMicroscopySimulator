using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace YeastSim
{
    /// <summary>How attached buds appear in the label images.</summary>
    public enum BudLabeling
    {
        /// <summary>A bud has its own id and colour from the moment it emerges.</summary>
        Separate,
        /// <summary>A bud is drawn with its mother's id until it separates (division).</summary>
        MergeWithMother,
    }

    /// <summary>
    /// Records a time-lapse of the simulation: label movies with consistent per-cell colours,
    /// a 16-bit cell-id TIFF stack, simulated brightfield frames, a lineage table and per-frame
    /// cell tracks. Optionally encodes lossless FFV1 videos with ffmpeg and runs several
    /// movies back to back with different seeds.
    /// </summary>
    /// <remarks>
    /// Output (one folder per run):
    /// <code>
    /// labels/labels_0000.png        RGB label frames, black background, one colour per cell
    /// brightfield/brightfield_0000.png
    /// labels_id.tif                 16-bit stack, pixel = cell id (0 = background)
    /// labels.avi, brightfield.avi   lossless FFV1 (needs ffmpeg)
    /// lineage.csv                   one row per cell: mother, colony, colour, emergence/division
    /// tracks.csv                    one row per cell per frame: centroid and area in pixels
    /// metadata.json                 seed, pixel size, interval and all settings
    /// </code>
    /// </remarks>
    [RequireComponent(typeof(SimulationManager))]
    public sealed class SimulationRecorder : MonoBehaviour
    {
        [Tooltip("Shader YeastSim/CellCapture.")]
        [SerializeField] private Shader _captureShader;

        [Header("Time-lapse")]
        [Tooltip("Number of frames per movie.")]
        [Min(1)] public int Frames = 60;

        [Tooltip("Simulated minutes between frames.")]
        [Min(0.1f)] public float FrameIntervalMinutes = 5f;

        [Tooltip("Simulated minutes before the first frame, so colonies already have a few cells.")]
        [Min(0f)] public float WarmupMinutes = 120f;

        [Header("Batch")]
        [Tooltip("Number of movies to record one after another (the scene is reloaded between runs).")]
        [Min(1)] public int Runs = 1;

        [Tooltip("Seed of the first run; run i uses seed + i. 0 = random seeds.")]
        public int BaseSeed;

        [Tooltip("Leave Play Mode (or quit a build) after the last run.")]
        public bool StopWhenDone = true;

        [Header("Output")]
        [Tooltip("Output folder; relative paths are relative to the project folder (or the build folder).")]
        public string OutputFolder = "Recordings";

        public BudLabeling BudLabels = BudLabeling.Separate;
        public bool ExportLabelPngs = true;
        public bool ExportIdTiff = true;
        public bool ExportBrightfield = true;
        public bool ExportTracks = true;

        [Tooltip("Encode lossless FFV1 .avi movies from the PNG frames with ffmpeg.")]
        public bool ExportVideos = true;

        [Tooltip("ffmpeg executable: a full path, or just 'ffmpeg' if it is on the PATH.")]
        public string FfmpegPath = "ffmpeg";

        [Min(1)] public int VideoFps = 10;

        [Header("Brightfield")]
        public BrightfieldSettings Brightfield = new BrightfieldSettings();

        // Batch state survives scene reloads within one play session.
        private static int s_runIndex;
        private static string s_batchFolder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_runIndex = 0;
            s_batchFolder = null;
        }

        private SimulationManager _simulation;
        private FrameCapture _capture;
        private BrightfieldRenderer _brightfield;
        private TiffStackWriter _tiff;
        private StringBuilder _tracks;
        private readonly List<float> _frameMinutes = new List<float>();
        private readonly Dictionary<int, Color32> _colorById = new Dictionary<int, Color32>();
        private readonly Dictionary<int, YeastCell> _cellById = new Dictionary<int, YeastCell>();
        private ushort[] _ids;
        private byte[] _facing;
        private Texture2D _pngTexture;
        private Color32[] _pngPixels;
        private string _runFolder;
        private int _frame;
        private string _status = "";

        #region Lifecycle
        private void Awake()
        {
            _simulation = GetComponent<SimulationManager>();
            int seed = BaseSeed != 0 ? BaseSeed + s_runIndex : 0;
            _simulation.Initialize(seed);
        }

        private void Start()
        {
            if (!_simulation.IsInitialized) return;
            if (_captureShader == null) _captureShader = Shader.Find("YeastSim/CellCapture");
            if (_captureShader == null)
            {
                Debug.LogError("[YeastSim] Capture shader 'YeastSim/CellCapture' not found; recording disabled.", this);
                return;
            }
            StartCoroutine(Record());
        }

        private void OnDestroy()
        {
            _capture?.Dispose();
            _tiff?.Dispose();
            _tiff = null;
            if (_pngTexture != null) Destroy(_pngTexture);
        }
        #endregion

        #region Recording
        private IEnumerator Record()
        {
            MicroscopeSettings scope = _simulation.Microscope;
            int n = scope.Resolution;
            _ids = new ushort[n * n];
            _facing = new byte[n * n];
            _pngPixels = new Color32[n * n];
            _pngTexture = new Texture2D(n, n, TextureFormat.RGB24, false);
            _capture = new FrameCapture(_captureShader, _simulation.CellLayer, scope, transform);
            _brightfield = new BrightfieldRenderer(n, _simulation.ActiveSeed);

            _runFolder = CreateRunFolder();
            if (ExportLabelPngs || ExportVideos) Directory.CreateDirectory(Path.Combine(_runFolder, "labels"));
            if (ExportBrightfield) Directory.CreateDirectory(Path.Combine(_runFolder, "brightfield"));
            if (ExportIdTiff) _tiff = new TiffStackWriter(Path.Combine(_runFolder, "labels_id.tif"), n, n);
            if (ExportTracks)
            {
                _tracks = new StringBuilder();
                _tracks.AppendLine("frame,time_min,cell_id,mother_id,colony_id,x_px,y_px,area_px,is_attached_bud");
            }
            Debug.Log($"[YeastSim] Run {s_runIndex + 1}/{Runs} (seed {_simulation.ActiveSeed}) → {_runFolder}");

            for (_frame = 0; _frame < Frames; _frame++)
            {
                _simulation.RunUntil(WarmupMinutes + _frame * FrameIntervalMinutes);
                while (!_simulation.IsStopped) yield return null;
                yield return new WaitForEndOfFrame();   // bones and skinning are up to date for this frame
                CaptureFrame();
            }

            FinishRun();
        }

        private void CaptureFrame()
        {
            IReadOnlyList<YeastCell> cells = _simulation.Cells;
            _cellById.Clear();
            foreach (YeastCell cell in cells)
            {
                _cellById[cell.Id] = cell;
                _colorById[cell.Id] = cell.LabelColor;
            }

            float badFraction = _capture.Capture(cells, LabelIdOf, _ids, _facing);
            if (badFraction > 0.001f)
                Debug.LogWarning($"[YeastSim] Frame {_frame}: {badFraction:P1} of cell pixels have an unknown id. " +
                                 "Something blends or rescales the capture (URP Render Scale must be 1, no MSAA/upscaling).");

            float minute = _simulation.SimMinutes;
            _frameMinutes.Add(minute);
            string index = _frame.ToString("D4");
            int n = _capture.Resolution;

            if (ExportLabelPngs || ExportVideos)
            {
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int id = _ids[y * n + x];
                    _pngPixels[(n - 1 - y) * n + x] = id == 0 ? new Color32(0, 0, 0, 255)
                        : _colorById.TryGetValue(id, out Color32 c) ? c : new Color32(255, 255, 255, 255);
                }
                WritePng(Path.Combine(_runFolder, "labels", $"labels_{index}.png"));
            }

            _tiff?.AddPage(_ids);

            if (ExportBrightfield)
            {
                byte[] gray = _brightfield.Render(_ids, _facing, Brightfield);
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    byte g = gray[y * n + x];
                    _pngPixels[(n - 1 - y) * n + x] = new Color32(g, g, g, 255);
                }
                WritePng(Path.Combine(_runFolder, "brightfield", $"brightfield_{index}.png"));
            }

            if (_tracks != null) AppendTracks(minute);

            _status = $"Run {s_runIndex + 1}/{Runs} · frame {_frame + 1}/{Frames} · {minute:F0} min · {cells.Count} cells" +
                      $" · relaxation steps so far: {_simulation.TotalRelaxationSteps}";
        }

        private int LabelIdOf(YeastCell cell) =>
            BudLabels == BudLabeling.MergeWithMother && cell.IsAttachedBud && cell.Mother != null ? cell.Mother.Id : cell.Id;

        private void WritePng(string path)
        {
            _pngTexture.SetPixels32(_pngPixels);
            _pngTexture.Apply(false);
            File.WriteAllBytes(path, _pngTexture.EncodeToPNG());
        }

        private void AppendTracks(float minute)
        {
            int n = _capture.Resolution;
            var area = new Dictionary<int, int>();
            var sumX = new Dictionary<int, double>();
            var sumY = new Dictionary<int, double>();
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int id = _ids[y * n + x];
                if (id == 0) continue;
                area.TryGetValue(id, out int a); area[id] = a + 1;
                sumX.TryGetValue(id, out double sx); sumX[id] = sx + x;
                sumY.TryGetValue(id, out double sy); sumY[id] = sy + y;
            }

            var ids = new List<int>(area.Keys);
            ids.Sort();
            foreach (int id in ids)
            {
                _cellById.TryGetValue(id, out YeastCell cell);
                int a = area[id];
                _tracks.Append(_frame).Append(',')
                       .Append(F(minute - WarmupMinutes)).Append(',')
                       .Append(id).Append(',')
                       .Append(cell != null ? cell.MotherId : 0).Append(',')
                       .Append(cell != null ? cell.ColonyId : 0).Append(',')
                       .Append(F(sumX[id] / a)).Append(',')
                       .Append(F(sumY[id] / a)).Append(',')
                       .Append(a).Append(',')
                       .Append(cell != null && cell.IsAttachedBud ? 1 : 0).AppendLine();
            }
        }
        #endregion

        #region Finishing
        private void FinishRun()
        {
            _tiff?.Dispose();
            _tiff = null;
            if (_tracks != null) File.WriteAllText(Path.Combine(_runFolder, "tracks.csv"), _tracks.ToString());
            WriteLineage();
            WriteMetadata();
            if (ExportVideos) EncodeVideos();

            Debug.Log($"[YeastSim] Run {s_runIndex + 1}/{Runs} finished: {Frames} frames, {_simulation.Cells.Count} cells → {_runFolder}");

            s_runIndex++;
            if (s_runIndex < Runs)
            {
                ReloadScene();
                return;
            }

            _status = $"Done: {Runs} run(s) written to {Path.GetDirectoryName(_runFolder)}";
            s_runIndex = 0;
            s_batchFolder = null;
            if (StopWhenDone) StopPlaying();
        }

        private void WriteLineage()
        {
            var csv = new StringBuilder();
            csv.AppendLine("cell_id,mother_id,colony_id,generation,bud_count,color_hex,color_r,color_g,color_b," +
                           "emergence_min,division_min,first_frame,division_frame");
            foreach (LineageRecord r in _simulation.Lineage)
            {
                // Times relative to the first frame; frames are the first frame at/after the event.
                float emergence = r.EmergenceMinute - WarmupMinutes;
                bool divided = !float.IsNaN(r.DivisionMinute);
                float division = divided ? r.DivisionMinute - WarmupMinutes : float.NaN;
                int firstFrame = FrameAtOrAfter(r.EmergenceMinute);
                int divisionFrame = divided ? FrameAtOrAfter(r.DivisionMinute) : -1;

                csv.Append(r.CellId).Append(',')
                   .Append(r.MotherId).Append(',')
                   .Append(r.ColonyId).Append(',')
                   .Append(r.Generation).Append(',')
                   .Append(r.BudCount).Append(',')
                   .Append(LabelPalette.ToHex(r.Color)).Append(',')
                   .Append(r.Color.r).Append(',').Append(r.Color.g).Append(',').Append(r.Color.b).Append(',')
                   .Append(F(emergence)).Append(',')
                   .Append(divided ? F(division) : "").Append(',')
                   .Append(firstFrame >= 0 ? firstFrame.ToString(CultureInfo.InvariantCulture) : "").Append(',')
                   .Append(divisionFrame >= 0 ? divisionFrame.ToString(CultureInfo.InvariantCulture) : "")
                   .AppendLine();
            }
            File.WriteAllText(Path.Combine(_runFolder, "lineage.csv"), csv.ToString());
        }

        /// <summary>Index of the first recorded frame at or after <paramref name="minute"/>; -1 if none.</summary>
        private int FrameAtOrAfter(float minute)
        {
            for (int i = 0; i < _frameMinutes.Count; i++)
                if (_frameMinutes[i] >= minute - 1e-4f) return i;
            return -1;
        }

        [Serializable]
        private class RunMetadata
        {
            public int seed;
            public int run;
            public int frames;
            public float frameIntervalMinutes;
            public float warmupMinutes;
            public int resolution;
            public float pixelSizeMicrometers;
            public float fieldSizeMicrometers;
            public string budLabeling;
            public int cells;
            public float[] frameTimesMinutes;
            public string labelEncoding;
            public CellGeometrySettings geometry;
            public CellPhysicsSettings physics;
            public CellCycleSettings cycle;
            public EnvironmentSettings environment;
            public TimeSettings timing;
            public BrightfieldSettings brightfield;
        }

        private void WriteMetadata()
        {
            var times = new float[_frameMinutes.Count];
            for (int i = 0; i < times.Length; i++) times[i] = _frameMinutes[i] - WarmupMinutes;

            var meta = new RunMetadata
            {
                seed = _simulation.ActiveSeed,
                run = s_runIndex,
                frames = _frameMinutes.Count,
                frameIntervalMinutes = FrameIntervalMinutes,
                warmupMinutes = WarmupMinutes,
                resolution = _simulation.Microscope.Resolution,
                pixelSizeMicrometers = _simulation.Microscope.PixelSize,
                fieldSizeMicrometers = _simulation.Microscope.FieldSize,
                budLabeling = BudLabels.ToString(),
                cells = _simulation.Cells.Count,
                frameTimesMinutes = times,
                labelEncoding = "labels_id.tif: uint16 cell id per pixel (0 = background). " +
                                "labels/*.png: colour per cell as listed in lineage.csv. Image right = +x, up = +z.",
                geometry = _simulation.Geometry,
                physics = _simulation.CellPhysics,
                cycle = _simulation.Cycle,
                environment = _simulation.Environment,
                timing = _simulation.Timing,
                brightfield = Brightfield,
            };
            File.WriteAllText(Path.Combine(_runFolder, "metadata.json"), JsonUtility.ToJson(meta, true));
        }

        private void EncodeVideos()
        {
            if (ExportLabelPngs || ExportVideos)
                RunFfmpeg($"-y -hide_banner -loglevel error -framerate {VideoFps} -i labels/labels_%04d.png " +
                          "-c:v ffv1 -level 3 -g 1 -pix_fmt bgr0 labels.avi", "labels.avi");
            if (ExportBrightfield)
                RunFfmpeg($"-y -hide_banner -loglevel error -framerate {VideoFps} -i brightfield/brightfield_%04d.png " +
                          "-c:v ffv1 -level 3 -g 1 -pix_fmt gray brightfield.avi", "brightfield.avi");
        }

        private void RunFfmpeg(string arguments, string output)
        {
            string folder = _runFolder;
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo(FfmpegPath, arguments)
                    {
                        WorkingDirectory = folder,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                    },
                    EnableRaisingEvents = true,
                };
                var errors = new StringBuilder();
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) errors.AppendLine(e.Data); };
                process.Exited += (_, __) =>
                {
                    if (process.ExitCode == 0) Debug.Log($"[YeastSim] Wrote {Path.Combine(folder, output)}");
                    else Debug.LogWarning($"[YeastSim] ffmpeg failed on {output} (exit {process.ExitCode}):\n{errors}");
                    process.Dispose();
                };
                process.Start();
                process.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[YeastSim] Could not start ffmpeg ('{FfmpegPath}'): {e.Message}. Install ffmpeg and " +
                                 "add it to PATH, or set the full path on the recorder. The PNG frames were written anyway.");
            }
        }

        private string CreateRunFolder()
        {
            string root = Path.IsPathRooted(OutputFolder)
                ? OutputFolder
                : Path.Combine(Directory.GetParent(Application.dataPath).FullName, OutputFolder);

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string folder;
            if (Runs > 1)
            {
                if (s_batchFolder == null) s_batchFolder = Path.Combine(root, $"batch_{stamp}");
                folder = Path.Combine(s_batchFolder, $"run_{s_runIndex:D3}_seed{_simulation.ActiveSeed}");
            }
            else folder = Path.Combine(root, $"{stamp}_seed{_simulation.ActiveSeed}");

            Directory.CreateDirectory(folder);
            return folder;
        }

        private static void ReloadScene()
        {
            Scene scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(scene.buildIndex);
#endif
        }

        private static void StopPlaying()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        #endregion

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(_status)) return;
            GUI.Label(new Rect(10, 10, 900, 24), _status);
        }
    }
}
