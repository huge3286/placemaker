using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Citymaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    [Collection(TimingCollection.Name)]
    public class RoadsOrganicGeneratorTests
    {
        private static GridParameters Organic(float block = 80f, float curviness = 60f, int seed = 1, float loops = 30f)
        {
            GridParameters p = GridParameters.Default;
            p.OrganicMode = true;
            p.OrganicStreetSpacing = block;
            p.OrganicCurviness = curviness;
            p.OrganicSeed = seed;
            p.OrganicLoopShare = loops;
            return p;
        }

        private static List<float3> Rectangle(float width, float height)
        {
            return new List<float3> { new float3(0, 0, 0), new float3(width, 0, 0), new float3(width, 0, height), new float3(0, 0, height) };
        }

        private static List<float3> LoadRealPerimeter()
        {
            string path = Path.Combine(RoadsTestUtil.FixtureDir(), "real-perimeter-backtrack.txt");
            return File.ReadAllLines(path).Where(l => l.Trim().Length > 0).Select(l => l.Trim().Split(' '))
                .Select(q => new float3(float.Parse(q[0], CultureInfo.InvariantCulture), 0f, float.Parse(q[1], CultureInfo.InvariantCulture))).ToList();
        }

        private static float DistanceToPolygon(float2 p, List<float3> polygon)
        {
            float best = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                float2 a = polygon[i].xz, b = polygon[(i + 1) % polygon.Count].xz, ab = b - a;
                float t = math.saturate(math.dot(p - a, ab) / math.lengthsq(ab));
                best = math.min(best, math.distance(p, a + t * ab));
            }
            return best;
        }

        private static bool Inside(float2 p, List<float3> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                float2 a = polygon[i].xz, b = polygon[j].xz;
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        private static bool Cross(float2 a1, float2 a2, float2 b1, float2 b2)
        {
            float Orient(float2 a, float2 b, float2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            float d1 = Orient(b1, b2, a1), d2 = Orient(b1, b2, a2), d3 = Orient(a1, a2, b1), d4 = Orient(a1, a2, b2);
            return ((d1 > 1e-2f && d2 < -1e-2f) || (d1 < -1e-2f && d2 > 1e-2f)) && ((d3 > 1e-2f && d4 < -1e-2f) || (d3 < -1e-2f && d4 > 1e-2f));
        }

        private static void AssertWellFormed(List<RoadSegmentDef> segments, List<float3> perimeter, string label)
        {
            Assert.True(segments.Count >= 10, $"{label}: only {segments.Count} segments");
            var curves = segments.Select(s => RoadsConcentricGeneratorTests.GameCurve(s, 24)).ToList();
            var nodes = new Dictionary<float3, List<float2>>(); // outgoing tangent of each segment, per node
            for (int k = 0; k < segments.Count; k++)
            {
                RoadSegmentDef s = segments[k];
                float2[] c = curves[k];
                float length = 0f;
                for (int i = 1; i < c.Length; i++) length += math.distance(c[i - 1], c[i]);
                Assert.True(length >= 34f, $"{label}: {length:F0} m segment at {s.Start.xz}");
                bool startOn = DistanceToPolygon(s.Start.xz, perimeter) < 0.5f, endOn = DistanceToPolygon(s.End.xz, perimeter) < 0.5f;
                foreach (float3 q in new[] { s.Start, s.End })
                {
                    float d = DistanceToPolygon(q.xz, perimeter);
                    Assert.True(d < 0.5f || d >= 24f, $"{label}: node {d:F1} m from the perimeter at {q.xz}");
                }
                float along = 0f;
                for (int i = 0; i < c.Length; i++)
                {
                    if (i > 0) along += math.distance(c[i - 1], c[i]);
                    Assert.True(Inside(c[i], perimeter) || DistanceToPolygon(c[i], perimeter) < 0.5f, $"{label}: segment outside the shape at {c[i]}");
                    // Outside the approach zone of an end on the perimeter (25 / sin 50° + margin).
                    if ((startOn && along < 42f) || (endOn && length - along < 42f)) continue;
                    Assert.True(DistanceToPolygon(c[i], perimeter) >= 23f, $"{label}: street {DistanceToPolygon(c[i], perimeter):F1} m from the perimeter at {c[i]}");
                }
                void AddTangent(float3 n, float2 t)
                {
                    if (!nodes.TryGetValue(n, out var list)) nodes[n] = list = new List<float2>();
                    list.Add(math.normalize(t));
                }
                AddTangent(s.Start, c[1] - c[0]);
                AddTangent(s.End, c[c.Length - 2] - c[c.Length - 1]);
            }
            // Junction angles (actual curve tangents).
            foreach (var pair in nodes)
            {
                for (int i = 0; i < pair.Value.Count; i++)
                    for (int j = i + 1; j < pair.Value.Count; j++)
                    {
                        float angle = math.degrees(math.acos(math.clamp(math.dot(pair.Value[i], pair.Value[j]), -1f, 1f)));
                        Assert.True(angle >= 38f, $"{label}: {angle:F0}° angle at node {pair.Key.xz}");
                    }
            }
            // No crossings (actual curves, shared endpoints excluded).
            var boxes = curves.Select(c => (min: c.Aggregate(new float2(float.MaxValue), math.min), max: c.Aggregate(new float2(float.MinValue), math.max))).ToList();
            for (int a = 0; a < curves.Count; a++)
                for (int b = a + 1; b < curves.Count; b++)
                {
                    if (math.any(boxes[a].max < boxes[b].min) || math.any(boxes[b].max < boxes[a].min)) continue;
                    for (int i = 1; i < curves[a].Length; i++)
                        for (int j = 1; j < curves[b].Length; j++)
                            Assert.False(Cross(curves[a][i - 1], curves[a][i], curves[b][j - 1], curves[b][j]),
                                $"{label}: crossing near {curves[a][i]}");
                }
            // Free ends: only entries (on the perimeter) or cul-de-sac ends, which must themselves be
            // far from the perimeter and from other streets (room for the turning circle).
            var tips = new HashSet<float3>(segments.Where(s => s.IsCulDeSacEnd).Select(s => s.End));
            Assert.NotEmpty(tips);
            foreach (var pair in nodes.Where(n => n.Value.Count == 1))
            {
                bool entry = DistanceToPolygon(pair.Key.xz, perimeter) < 0.5f;
                Assert.True(entry || tips.Contains(pair.Key), $"{label}: free end that is not a cul-de-sac at {pair.Key.xz}");
            }
            foreach (float3 tip in tips)
            {
                Assert.Single(nodes[tip]);
                Assert.True(DistanceToPolygon(tip.xz, perimeter) >= 29f, $"{label}: cul-de-sac {DistanceToPolygon(tip.xz, perimeter):F0} m from the perimeter at {tip.xz}");
                for (int k = 0; k < segments.Count; k++)
                {
                    if (segments[k].Start.Equals(tip) || segments[k].End.Equals(tip)) continue;
                    float d = curves[k].Min(q => math.distance(q, tip.xz));
                    Assert.True(d >= 38f, $"{label}: cul-de-sac end {d:F0} m from another street at {tip.xz}");
                }
            }
            // Two networks: the main street (avenue) and all other streets (cul-de-sac, IsLocal).
            Assert.All(segments, s => Assert.True(s.IsAvenue != s.IsLocal, $"{label}: segment neither avenue nor cul-de-sac at {s.Start.xz}"));
            // Everything connected to the entries.
            var adjacency = new Dictionary<float3, List<float3>>();
            foreach (RoadSegmentDef s in segments)
            {
                if (!adjacency.TryGetValue(s.Start, out var a)) adjacency[s.Start] = a = new List<float3>();
                if (!adjacency.TryGetValue(s.End, out var b)) adjacency[s.End] = b = new List<float3>();
                a.Add(s.End);
                b.Add(s.Start);
            }
            var entries = adjacency.Keys.Where(n => DistanceToPolygon(n.xz, perimeter) < 0.5f).ToList();
            Assert.True(entries.Count >= 2, $"{label}: {entries.Count} entry(ies)");
            var seen = new HashSet<float3>();
            var stack = new Stack<float3>(entries);
            while (stack.Count > 0)
            {
                float3 n = stack.Pop();
                if (!seen.Add(n)) continue;
                foreach (float3 m in adjacency[n]) stack.Push(m);
            }
            Assert.True(seen.Count == adjacency.Count, $"{label}: {adjacency.Count - seen.Count} node(s) not connected to the entries");
        }

        [Fact]
        public void Rectangle_IsWellFormed()
        {
            var perimeter = Rectangle(900f, 700f);
            foreach (int seed in new[] { 1, 2, 3 })
            {
                AssertWellFormed(GridGenerator.GenerateGrid(perimeter, Organic(seed: seed)), perimeter, $"rectangle seed {seed}");
            }
        }

        [Fact]
        public void RealPerimeter_IsWellFormed()
        {
            var perimeter = LoadRealPerimeter();
            foreach ((GridParameters p, string label) in new[] { (Organic(), "default"), (Organic(block: 60f, curviness: 100f, seed: 7, loops: 60f), "tight, very curvy"),
                         (Organic(block: 140f, curviness: 0f, seed: 42, loops: 0f), "wide, straight"), (Organic(curviness: 100f, seed: 99), "curvy 99") })
            {
                AssertWellFormed(GridGenerator.GenerateGrid(perimeter, p), perimeter, label);
            }
        }

        [Fact]
        public void RealPerimeter_ManySeeds_AreWellFormed()
        {
            // Random pattern: the guarantees must hold for every variant, not just one.
            var perimeter = LoadRealPerimeter();
            foreach (float spacing in new[] { 60f, 80f, 110f })
            {
                for (int seed = 10; seed < 22; seed++)
                {
                    AssertWellFormed(GridGenerator.GenerateGrid(perimeter, Organic(block: spacing, curviness: 80f, seed: seed, loops: 40f)), perimeter,
                        $"{spacing} m seed {seed}");
                }
            }
        }

        [Fact]
        public void SameSeed_SameResult_OtherSeed_Different()
        {
            var perimeter = LoadRealPerimeter();
            var a = GridGenerator.GenerateGrid(perimeter, Organic(seed: 5, block: 81f));
            var b = GridGenerator.GenerateGrid(perimeter, Organic(seed: 5, block: 81f));
            var c = GridGenerator.GenerateGrid(perimeter, Organic(seed: 6, block: 81f));
            Assert.Equal(a.Select(s => (s.Start, s.End)), b.Select(s => (s.Start, s.End)));
            Assert.NotEqual(a.Select(s => (s.Start, s.End)), c.Select(s => (s.Start, s.End)));
        }

        [Fact]
        public void RealPerimeter_GeneratesQuickly()
        {
            var perimeter = LoadRealPerimeter();
            GridGenerator.GenerateGrid(perimeter, Organic(seed: 50));
            // Citymaker: generators no longer keep static result/distance-field caches (thread safety);
            // re-plans of an unchanged input are cached by the preview worker instead. Budgets are per
            // uncached generation, Debug build, with headroom for the parallel test run.
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 5; i++)
            {
                GridGenerator.GenerateGrid(perimeter, Organic(seed: 51 + i)); // a different seed on each run
            }
            stopwatch.Stop();
            Assert.True(stopwatch.ElapsedMilliseconds < TimingBudget.Ms(1500), $"5 generations in {stopwatch.ElapsedMilliseconds} ms");
        }
    }
}
