using System;
using UnityEngine;

namespace YeastSim
{
    /// <summary>Appearance of the simulated brightfield images. Intensities are fractions of full scale.</summary>
    [Serializable]
    public class BrightfieldSettings
    {
        [Header("Cells")]
        [Tooltip("Background (empty slide) intensity.")]
        [Range(0f, 1f)] public float Background = 0.55f;

        [Tooltip("Brightness of the cell interior relative to the background (+ brighter, − darker).")]
        [Range(-0.5f, 0.5f)] public float Interior = 0.06f;

        [Tooltip("Random brightness difference between cells.")]
        [Range(0f, 0.3f)] public float CellVariability = 0.05f;

        [Tooltip("Darkening of the cell wall towards the outline.")]
        [Range(0f, 1f)] public float RimDarkness = 0.8f;

        [Tooltip("How sharply the dark rim is confined to the outline (higher = thinner rim).")]
        [Range(0.5f, 8f)] public float RimSharpness = 2.5f;

        [Tooltip("Extra darkening where two cells touch.")]
        [Range(0f, 1f)] public float ContactDarkness = 0.35f;

        [Header("Optics")]
        [Tooltip("Bright halo just outside each cell (phase-contrast-like defocus).")]
        [Range(0f, 1f)] public float Halo = 0.4f;

        [Tooltip("Width of the halo (pixels).")]
        [Range(0.5f, 10f)] public float HaloWidth = 3f;

        [Tooltip("Gaussian defocus blur (pixels).")]
        [Range(0f, 5f)] public float Defocus = 1.0f;

        [Tooltip("Linear illumination gradient across the image.")]
        [Range(0f, 0.3f)] public float IlluminationGradient = 0.06f;

        [Tooltip("Darkening towards the corners.")]
        [Range(0f, 0.5f)] public float Vignette = 0.1f;

        [Header("Camera noise")]
        [Tooltip("Shot (photon) noise: standard deviation at full scale.")]
        [Range(0f, 0.1f)] public float ShotNoise = 0.02f;

        [Tooltip("Read noise: constant standard deviation.")]
        [Range(0f, 0.1f)] public float ReadNoise = 0.01f;
    }

    /// <summary>
    /// Turns a label map (cell id per pixel) and the surface-facing map from <see cref="FrameCapture"/>
    /// into a simple brightfield-like greyscale image.
    /// </summary>
    /// <remarks>
    /// Not a physical optics model. It combines the main visual cues of yeast in brightfield:
    /// a slightly different interior, a dark rim along the wall, dark lines where cells touch, a
    /// bright halo outside the cells, defocus blur, uneven illumination and camera noise.
    /// </remarks>
    public sealed class BrightfieldRenderer
    {
        private readonly int _n;
        private readonly float[] _image;
        private readonly float[] _mask;
        private readonly float[] _contacts;
        private readonly float[] _scratch;
        private readonly byte[] _output;
        private readonly System.Random _random;

        public BrightfieldRenderer(int resolution, int seed)
        {
            _n = resolution;
            int size = resolution * resolution;
            _image = new float[size];
            _mask = new float[size];
            _contacts = new float[size];
            _scratch = new float[size];
            _output = new byte[size];
            _random = new System.Random(seed);
        }

        /// <summary>Renders one frame; returns 8-bit greyscale pixels, row 0 at the top (reused buffer).</summary>
        public byte[] Render(ushort[] ids, byte[] facing, BrightfieldSettings s)
        {
            int n = _n;
            for (int i = 0; i < ids.Length; i++)
            {
                _mask[i] = ids[i] != 0 ? 1f : 0f;
                _contacts[i] = 0f;
            }

            // Pixels on the border between two different cells.
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                int id = ids[i];
                if (id == 0) continue;
                if ((x + 1 < n && ids[i + 1] != 0 && ids[i + 1] != id) ||
                    (y + 1 < n && ids[i + n] != 0 && ids[i + n] != id))
                    _contacts[i] = 1f;
            }

            // Halo: blurred mask, used outside the cells.
            Array.Copy(_mask, _image, _mask.Length);
            GaussianBlur(_image, s.HaloWidth);
            GaussianBlur(_contacts, 0.7f);
            float contactScale = 1f / 0.45f;   // a 1-px line keeps ~45% of its peak after a 0.7-px blur

            for (int i = 0; i < ids.Length; i++)
            {
                float value;
                int id = ids[i];
                if (id == 0)
                {
                    value = s.Background * (1f + s.Halo * _image[i]);
                }
                else
                {
                    float rim = Mathf.Pow(1f - facing[i] / 255f, s.RimSharpness);
                    value = s.Background * (1f + s.Interior + s.CellVariability * CellOffset(id))
                          - s.Background * s.RimDarkness * rim;
                }
                value -= s.Background * s.ContactDarkness * Mathf.Min(1f, _contacts[i] * contactScale);
                _scratch[i] = value;
            }

            GaussianBlur(_scratch, s.Defocus);

            for (int y = 0; y < n; y++)
            {
                float v = (y + 0.5f) / n - 0.5f;
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n - 0.5f;
                    float illumination = 1f + s.IlluminationGradient * (u + 0.5f * v) * 2f
                                         - s.Vignette * (u * u + v * v) * 2f;
                    int i = y * n + x;
                    float value = _scratch[i] * illumination;
                    float sigma = Mathf.Sqrt(s.ShotNoise * s.ShotNoise * Mathf.Max(value, 0f) + s.ReadNoise * s.ReadNoise);
                    value += sigma * Gaussian();
                    _output[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
                }
            }
            return _output;
        }

        /// <summary>Deterministic per-cell value in [-1, 1].</summary>
        private static float CellOffset(int id)
        {
            uint h = (uint)id * 2654435761u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
        }

        private float Gaussian()
        {
            double u1 = 1.0 - _random.NextDouble();
            double u2 = _random.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        private float[] _blurTemp;
        private float[] _kernel = Array.Empty<float>();

        /// <summary>Separable Gaussian blur in place (edges clamped).</summary>
        private void GaussianBlur(float[] data, float sigma)
        {
            if (sigma <= 0.05f) return;
            int n = _n;
            int radius = Mathf.CeilToInt(3f * sigma);
            if (_kernel.Length != 2 * radius + 1) _kernel = new float[2 * radius + 1];
            float sum = 0f;
            for (int k = -radius; k <= radius; k++) sum += _kernel[k + radius] = Mathf.Exp(-k * k / (2f * sigma * sigma));
            for (int k = 0; k < _kernel.Length; k++) _kernel[k] /= sum;
            if (_blurTemp == null || _blurTemp.Length != data.Length) _blurTemp = new float[data.Length];

            for (int y = 0; y < n; y++)
            {
                int row = y * n;
                for (int x = 0; x < n; x++)
                {
                    float acc = 0f;
                    for (int k = -radius; k <= radius; k++)
                        acc += data[row + Mathf.Clamp(x + k, 0, n - 1)] * _kernel[k + radius];
                    _blurTemp[row + x] = acc;
                }
            }
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float acc = 0f;
                for (int k = -radius; k <= radius; k++)
                    acc += _blurTemp[Mathf.Clamp(y + k, 0, n - 1) * n + x] * _kernel[k + radius];
                data[y * n + x] = acc;
            }
        }
    }
}
