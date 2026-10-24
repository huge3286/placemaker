// Shared fixtures for the geometry tests (owner: geometry).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Citymaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    internal static class G
    {
        public static float2[] Rect(float x0, float y0, float x1, float y1) =>
            new[] { new float2(x0, y0), new float2(x1, y0), new float2(x1, y1), new float2(x0, y1) };

        public static float2[] Square(float size, float2 at = default) => Rect(at.x, at.y, at.x + size, at.y + size);

        /// <summary>L-shape: size×size with the top-right quarter (cut×cut) removed.</summary>
        public static float2[] L(float size = 100, float cut = 50, float2 at = default) => Offset(new[]
        {
            new float2(0, 0), new float2(size, 0), new float2(size, size - cut), new float2(size - cut, size - cut),
            new float2(size - cut, size), new float2(0, size),
        }, at);

        /// <summary>U-shape opening upwards: width w, height h, arm thickness t, base thickness b.</summary>
        public static float2[] U(float w = 100, float h = 80, float t = 20, float b = 20, float2 at = default) => Offset(new[]
        {
            new float2(0, 0), new float2(w, 0), new float2(w, h), new float2(w - t, h), new float2(w - t, b),
            new float2(t, b), new float2(t, h), new float2(0, h),
        }, at);

        /// <summary>Two squares joined by a thin neck (dumbbell).</summary>
        public static float2[] Dumbbell(float lobe = 40, float neckLength = 20, float neckWidth = 4, float2 at = default)
        {
            float y0 = lobe / 2 - neckWidth / 2, y1 = lobe / 2 + neckWidth / 2, x2 = lobe + neckLength;
            return Offset(new[]
            {
                new float2(0, 0), new float2(lobe, 0), new float2(lobe, y0), new float2(x2, y0), new float2(x2, 0),
                new float2(x2 + lobe, 0), new float2(x2 + lobe, lobe), new float2(x2, lobe), new float2(x2, y1),
                new float2(lobe, y1), new float2(lobe, lobe), new float2(0, lobe),
            }, at);
        }

        public static float2[] Circle(float r, int n, float2 c = default, float phase = 0)
        {
            var p = new float2[n];
            for (int i = 0; i < n; i++)
            {
                float a = phase + 2 * math.PI * i / n;
                p[i] = c + r * new float2(math.cos(a), math.sin(a));
            }
            return p;
        }

        /// <summary>Star / gear: alternating radii (reflex-heavy).</summary>
        public static float2[] Star(float rOuter, float rInner, int points, float2 c = default)
        {
            var p = new float2[points * 2];
            for (int i = 0; i < p.Length; i++)
            {
                float a = math.PI * i / points;
                float r = i % 2 == 0 ? rOuter : rInner;
                p[i] = c + r * new float2(math.cos(a), math.sin(a));
            }
            return p;
        }

        /// <summary>Wavy blob with n vertices (radius modulated), deterministic.</summary>
        public static float2[] Blob(float r, int n, float amp = 0.25f, int lobes = 7, float2 c = default)
        {
            var p = new float2[n];
            for (int i = 0; i < n; i++)
            {
                float a = 2 * math.PI * i / n;
                float rr = r * (1 + amp * math.sin(lobes * a) + 0.5f * amp * math.sin(3 * a + 1));
                p[i] = c + rr * new float2(math.cos(a), math.sin(a));
            }
            return p;
        }

        /// <summary>Comb: base strip with many teeth (reflex-heavy, ~4·teeth+4 vertices).</summary>
        public static float2[] Comb(int teeth, float toothW = 6, float gap = 4, float toothH = 30, float baseH = 10)
        {
            float total = teeth * toothW + (teeth - 1) * gap;
            var ring = new List<float2> { new float2(0, 0), new float2(total, 0) };
            for (int k = teeth - 1; k >= 0; k--)
            {
                float x0 = k * (toothW + gap), x1 = x0 + toothW;
                ring.Add(new float2(x1, baseH + toothH));
                ring.Add(new float2(x0, baseH + toothH));
                if (k > 0) { ring.Add(new float2(x0, baseH)); ring.Add(new float2(x0 - gap, baseH)); }
            }
            return ring.ToArray();
        }

        public static float2[] Offset(float2[] ring, float2 at)
        {
            if (math.all(at == float2.zero)) return ring;
            return ring.Select(p => p + at).ToArray();
        }

        public static float2[] Rotate(float2[] ring, float degrees, float2 pivot = default)
        {
            float a = math.radians(degrees), c = math.cos(a), s = math.sin(a);
            return ring.Select(p => { var d = p - pivot; return pivot + new float2(d.x * c - d.y * s, d.x * s + d.y * c); }).ToArray();
        }

        public static float TotalArea(IEnumerable<float2[]> rings) => rings.Sum(r => Poly.Area(r));
        public static float TotalArea(IEnumerable<Region> regions) => regions.Sum(r => r.Area);

        public static void AssertSimpleCcw(float2[] ring, string what = "ring")
        {
            Assert.True(ring.Length >= 3, $"{what}: {ring.Length} points");
            Assert.True(Poly.IsSimple(ring), $"{what} not simple: {Dump(ring)}");
            Assert.True(Poly.SignedArea(ring) > 0, $"{what} not CCW");
        }

        public static string Dump(IEnumerable<float2> ring) =>
            string.Join(" ", ring.Select(p => string.Format(CultureInfo.InvariantCulture, "({0:0.###},{1:0.###})", p.x, p.y)));

        public static string FixturePath(string name, [CallerFilePath] string here = "") =>
            Path.Combine(Path.GetDirectoryName(here) ?? ".", "Fixtures", name);

        /// <summary>"x z" per line (Grid Road Generator fixture format).</summary>
        public static float2[] LoadRing(string name)
        {
            return File.ReadAllLines(FixturePath(name))
                .Select(l => l.Trim()).Where(l => l.Length > 0)
                .Select(l => l.Split(' '))
                .Select(q => new float2(float.Parse(q[0], CultureInfo.InvariantCulture), float.Parse(q[1], CultureInfo.InvariantCulture)))
                .ToArray();
        }

        /// <summary>
        /// Best-of-N wall time (ms) after warm-up: the test host JITs in tiers and other processes share
        /// the CPU, the minimum is the stable measure of the algorithm's cost.
        /// </summary>
        public static double BestMs(System.Action action, int warmup = 3, int runs = 5)
        {
            for (int i = 0; i < warmup; i++) action();
            double best = double.MaxValue;
            for (int i = 0; i < runs; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                action();
                best = System.Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        /// <summary>Deterministic pseudo-random generator (tests must not depend on System.Random's algorithm).</summary>
        public sealed class Rng
        {
            private uint _s;
            public Rng(uint seed) { _s = seed * 2654435761u + 1; }
            public float Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return (_s & 0xffffff) / (float)0x1000000; }
            public float Range(float a, float b) => a + (b - a) * Next();
        }
    }
}
