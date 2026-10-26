using System.Collections.Generic;
using UnityEngine;

namespace YeastSim
{
    /// <summary>How label colours are handed out to new cells.</summary>
    public enum ColorAssignment
    {
        /// <summary>Strictly in palette order: yellow, blue, red, green, orange, purple, …</summary>
        Sequential,
        /// <summary>
        /// From the next few unused palette colours, pick the one most different from the cells
        /// around the new cell. Keeps neighbours distinguishable even when the colony is large.
        /// </summary>
        NeighbourAware,
    }

    /// <summary>
    /// Unique, maximally contrasting label colours on a black background.
    /// </summary>
    /// <remarks>
    /// The palette starts with yellow, blue, red, green, orange and purple and is then extended
    /// greedily: each next colour is the candidate (from an RGB grid) whose smallest perceptual
    /// distance (OKLab) to all earlier colours and to black is largest. Every cell gets its own
    /// colour for its whole life, so colour ↔ cell is one-to-one within a movie.
    /// </remarks>
    public sealed class LabelPalette
    {
        private static readonly Color32[] Seeds =
        {
            new Color32(255, 230, 0, 255),    // yellow
            new Color32(30, 100, 255, 255),   // blue
            new Color32(255, 20, 20, 255),    // red
            new Color32(0, 210, 40, 255),     // green
            new Color32(255, 140, 0, 255),    // orange
            new Color32(160, 40, 255, 255),   // purple
        };

        private const int GridLevels = 16;
        private const float MinLightness = 0.55f;   // OKLab L; keeps colours clearly brighter than the background

        private readonly List<Color32> _colors = new List<Color32>();
        private readonly List<Vector3> _labs = new List<Vector3>();
        private readonly List<bool> _used = new List<bool>();
        private int _firstUnused;

        private readonly List<Color32> _candidates = new List<Color32>();
        private readonly List<Vector3> _candidateLabs = new List<Vector3>();
        private float[] _candidateMinDistance;
        private bool[] _candidateTaken;

        private int _fallbackCounter;

        public LabelPalette()
        {
            Vector3 black = ToOkLab(new Color32(0, 0, 0, 255));
            for (int r = 0; r < GridLevels; r++)
            for (int g = 0; g < GridLevels; g++)
            for (int b = 0; b < GridLevels; b++)
            {
                var c = new Color32((byte)(r * 17), (byte)(g * 17), (byte)(b * 17), 255);
                Vector3 lab = ToOkLab(c);
                if (lab.x < MinLightness) continue;
                _candidates.Add(c);
                _candidateLabs.Add(lab);
            }

            _candidateMinDistance = new float[_candidates.Count];
            _candidateTaken = new bool[_candidates.Count];
            for (int i = 0; i < _candidates.Count; i++) _candidateMinDistance[i] = Vector3.Distance(_candidateLabs[i], black);

            foreach (Color32 seed in Seeds) Append(seed, ToOkLab(seed));
        }

        /// <summary>Palette colour <paramref name="index"/> (generated on demand).</summary>
        public Color32 this[int index]
        {
            get
            {
                EnsureCount(index + 1);
                return _colors[index];
            }
        }

        /// <summary>
        /// Takes an unused colour for a new cell. <paramref name="neighbourColors"/> are the colours
        /// of cells near the new one (ignored for <see cref="ColorAssignment.Sequential"/>).
        /// </summary>
        public Color32 Take(ColorAssignment mode, IReadOnlyList<Color32> neighbourColors, int window = 10)
        {
            while (_firstUnused < _colors.Count && _used[_firstUnused]) _firstUnused++;
            EnsureCount(_firstUnused + window);

            int chosen = _firstUnused;
            if (mode == ColorAssignment.NeighbourAware && neighbourColors != null && neighbourColors.Count > 0)
            {
                var neighbourLabs = new List<Vector3>(neighbourColors.Count);
                foreach (Color32 c in neighbourColors) neighbourLabs.Add(ToOkLab(c));

                // Score the next few unused colours by their distance to the closest neighbour colour,
                // then take the earliest one that is nearly as good as the best. This keeps the palette
                // order (yellow, blue, red, …) wherever it already gives good contrast.
                var candidates = new List<int>(window);
                var scores = new List<float>(window);
                float best = 0f;
                for (int i = _firstUnused; i < _colors.Count && candidates.Count < window; i++)
                {
                    if (_used[i]) continue;
                    float score = float.MaxValue;
                    foreach (Vector3 lab in neighbourLabs) score = Mathf.Min(score, Vector3.Distance(_labs[i], lab));
                    candidates.Add(i);
                    scores.Add(score);
                    best = Mathf.Max(best, score);
                }
                for (int k = 0; k < candidates.Count; k++)
                    if (scores[k] >= 0.8f * best) { chosen = candidates[k]; break; }
            }

            if (chosen >= _colors.Count) return NextFallback();
            _used[chosen] = true;
            return _colors[chosen];
        }

        private void EnsureCount(int count)
        {
            while (_colors.Count < count)
            {
                int best = -1;
                float bestDistance = -1f;
                for (int i = 0; i < _candidates.Count; i++)
                {
                    if (_candidateTaken[i]) continue;
                    if (_candidateMinDistance[i] > bestDistance) { bestDistance = _candidateMinDistance[i]; best = i; }
                }
                if (best < 0) return;   // grid exhausted (thousands of colours); Take() falls back
                _candidateTaken[best] = true;
                Append(_candidates[best], _candidateLabs[best]);
            }
        }

        private void Append(Color32 color, Vector3 lab)
        {
            _colors.Add(color);
            _labs.Add(lab);
            _used.Add(false);
            for (int i = 0; i < _candidates.Count; i++)
            {
                if (_candidateTaken[i]) continue;
                float d = Vector3.Distance(_candidateLabs[i], lab);
                if (d < _candidateMinDistance[i]) _candidateMinDistance[i] = d;
                if (d < 1e-6f) _candidateTaken[i] = true;   // identical to a seed colour
            }
        }

        /// <summary>Unique but not contrast-optimised colours, only used beyond ~2000 cells.</summary>
        private Color32 NextFallback()
        {
            var taken = new HashSet<int>();
            foreach (Color32 c in _colors) taken.Add(c.r << 16 | c.g << 8 | c.b);
            while (true)
            {
                int v = 0x404040 + 7919 * ++_fallbackCounter;
                v &= 0xFFFFFF;
                if (taken.Contains(v) || v == 0) continue;
                return new Color32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
            }
        }

        /// <summary>sRGB (8-bit) → OKLab (L, a, b).</summary>
        public static Vector3 ToOkLab(Color32 c)
        {
            float r = SrgbToLinear(c.r / 255f), g = SrgbToLinear(c.g / 255f), b = SrgbToLinear(c.b / 255f);
            float l = Mathf.Pow(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b, 1f / 3f);
            float m = Mathf.Pow(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b, 1f / 3f);
            float s = Mathf.Pow(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b, 1f / 3f);
            return new Vector3(0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                               1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                               0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
        }

        private static float SrgbToLinear(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);

        public static string ToHex(Color32 c) => $"#{c.r:X2}{c.g:X2}{c.b:X2}";
    }
}
