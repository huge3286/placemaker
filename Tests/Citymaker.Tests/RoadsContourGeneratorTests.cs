using System;
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
    public class RoadsContourGeneratorTests
    {
        private static GridParameters Contour(Func<float2, float> height, float spacing = 90f, float connectors = 250f)
        {
            GridParameters p = GridParameters.Default;
            p.ContourMode = true;
            p.ContourSpacing = spacing;
            p.ContourConnectorSpacing = connectors;
            p.HeightAt = height;
            return p;
        }

        private static List<float3> Rectangle(float width, float height) =>
            new List<float3> { new float3(0, 0, 0), new float3(width, 0, 0), new float3(width, 0, height), new float3(0, 0, height) };

        private static List<float3> LoadRealPerimeter()
        {
            string path = Path.Combine(RoadsTestUtil.FixtureDir(), "real-perimeter-backtrack.txt");
            return File.ReadAllLines(path).Where(l => l.Trim().Length > 0).Select(l => l.Trim().Split(' '))
                .Select(q => new float3(float.Parse(q[0], CultureInfo.InvariantCulture), 0f, float.Parse(q[1], CultureInfo.InvariantCulture))).ToList();
        }

        private static Func<float2, float> Hill(float2 centre, float height, float radius) =>
            p => height * math.exp(-math.lengthsq(p - centre) / (2f * radius * radius));

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

        private static void AssertWellFormed(List<RoadSegmentDef> segments, List<float3> perimeter, Func<float2, float> height, string label)
        {
            Assert.True(segments.Count(s => !s.IsAvenue) >= 6, $"{label}: only {segments.Count(s => !s.IsAvenue)} level segments");
            Assert.Contains(segments, s => s.IsAvenue);
            var curves = segments.Select(s => RoadsConcentricGeneratorTests.GameCurve(s, 24)).ToList();
            var nodes = new Dictionary<float3, List<float2>>();
            for (int k = 0; k < segments.Count; k++)
            {
                RoadSegmentDef s = segments[k];
                float2[] c = curves[k];
                float length = 0f;
                for (int i = 1; i < c.Length; i++) length += math.distance(c[i - 1], c[i]);
                Assert.True(length >= 34f, $"{label}: {length:F0} m segment at {s.Start.xz}");
                bool startOn = DistanceToPolygon(s.Start.xz, perimeter) < 0.5f, endOn = DistanceToPolygon(s.End.xz, perimeter) < 0.5f;
                float along = 0f;
                for (int i = 0; i < c.Length; i++)
                {
                    if (i > 0) along += math.distance(c[i - 1], c[i]);
                    Assert.True(Inside(c[i], perimeter) || DistanceToPolygon(c[i], perimeter) < 0.5f, $"{label}: outside the shape at {c[i]}");
                    if ((startOn && along < 45f) || (endOn && length - along < 45f)) continue;
                    Assert.True(DistanceToPolygon(c[i], perimeter) >= 22f, $"{label}: {DistanceToPolygon(c[i], perimeter):F1} m from the perimeter at {c[i]}");
                }
                if (s.IsAvenue)
                {
                    // Uphill link: grade below the maximum (15%).
                    float grade = math.abs(height(s.End.xz) - height(s.Start.xz)) / math.distance(s.Start.xz, s.End.xz);
                    Assert.True(grade <= 0.16f, $"{label}: uphill link at {grade:P0} at {s.Start.xz}");
                }
                void AddTangent(float3 n, float2 t)
                {
                    if (!nodes.TryGetValue(n, out var list)) nodes[n] = list = new List<float2>();
                    list.Add(math.normalize(t));
                }
                AddTangent(s.Start, c[1] - c[0]);
                AddTangent(s.End, c[c.Length - 2] - c[c.Length - 1]);
            }
            foreach (var pair in nodes)
                for (int i = 0; i < pair.Value.Count; i++)
                    for (int j = i + 1; j < pair.Value.Count; j++)
                    {
                        float angle = math.degrees(math.acos(math.clamp(math.dot(pair.Value[i], pair.Value[j]), -1f, 1f)));
                        Assert.True(angle >= 38f, $"{label}: {angle:F0}° angle at {pair.Key.xz}");
                    }
            var boxes = curves.Select(c => (min: c.Aggregate(new float2(float.MaxValue), math.min), max: c.Aggregate(new float2(float.MinValue), math.max))).ToList();
            for (int a = 0; a < curves.Count; a++)
                for (int b = a + 1; b < curves.Count; b++)
                {
                    if (math.any(boxes[a].max < boxes[b].min) || math.any(boxes[b].max < boxes[a].min)) continue;
                    for (int i = 1; i < curves[a].Length; i++)
                        for (int j = 1; j < curves[b].Length; j++)
                            Assert.False(Cross(curves[a][i - 1], curves[a][i], curves[b][j - 1], curves[b][j]), $"{label}: crossing near {curves[a][i]}");
                }
            // No cul-de-sac with a turning circle: only level streets and uphill links.
            Assert.DoesNotContain(segments, s => s.IsCulDeSacEnd);
            // Free ends off the perimeter: far from the perimeter and from other streets.
            var tips = nodes.Where(n => n.Value.Count == 1 && DistanceToPolygon(n.Key.xz, perimeter) >= 0.5f).Select(n => n.Key).ToList();
            foreach (float3 tip in tips)
            {
                Assert.True(DistanceToPolygon(tip.xz, perimeter) >= 29f, $"{label}: cul-de-sac {DistanceToPolygon(tip.xz, perimeter):F0} m from the perimeter");
                for (int k = 0; k < segments.Count; k++)
                {
                    if (segments[k].Start.Equals(tip) || segments[k].End.Equals(tip)) continue;
                    float d = curves[k].Min(q => math.distance(q, tip.xz));
                    Assert.True(d >= 38f, $"{label}: cul-de-sac end {d:F0} m from another street at {tip.xz}");
                }
            }
            // Everything connected to the perimeter.
            var adjacency = new Dictionary<float3, List<float3>>();
            foreach (RoadSegmentDef s in segments)
            {
                if (!adjacency.TryGetValue(s.Start, out var la)) adjacency[s.Start] = la = new List<float3>();
                if (!adjacency.TryGetValue(s.End, out var lb)) adjacency[s.End] = lb = new List<float3>();
                la.Add(s.End);
                lb.Add(s.Start);
            }
            var roots = adjacency.Keys.Where(n => DistanceToPolygon(n.xz, perimeter) < 0.5f).ToList();
            Assert.NotEmpty(roots);
            var seen = new HashSet<float3>();
            var stack = new Stack<float3>(roots);
            while (stack.Count > 0)
            {
                float3 n = stack.Pop();
                if (!seen.Add(n)) continue;
                foreach (float3 m in adjacency[n]) stack.Push(m);
            }
            Assert.True(seen.Count == adjacency.Count, $"{label}: {adjacency.Count - seen.Count} node(s) not connected to the perimeter");
        }

        [Fact]
        public void Hill_StreetsFollowTheContours()
        {
            var perimeter = Rectangle(1400f, 1000f);
            var height = Hill(new float2(700f, 500f), 80f, 350f);
            var segments = GridGenerator.GenerateGrid(perimeter, Contour(height));
            AssertWellFormed(segments, perimeter, height, "hill");
            // Level streets: height almost constant along each segment.
            foreach (RoadSegmentDef s in segments.Where(s => !s.IsAvenue))
            {
                var heights = RoadsConcentricGeneratorTests.GameCurve(s, 12).Select(q => height(q)).ToList();
                Assert.True(heights.Max() - heights.Min() <= 4f, $"level street climbing {heights.Max() - heights.Min():F1} m at {s.Start.xz}");
            }
        }

        [Fact]
        public void WavySlope_IsWellFormed()
        {
            var perimeter = Rectangle(1200f, 900f);
            Func<float2, float> height = p => 0.12f * p.x + 8f * math.sin(p.y / 120f);
            foreach (float spacing in new[] { 70f, 90f, 130f })
            {
                AssertWellFormed(GridGenerator.GenerateGrid(perimeter, Contour(height, spacing)), perimeter, height, $"slope {spacing} m");
            }
        }

        [Fact]
        public void RealPerimeter_WithAHill_IsWellFormed()
        {
            var perimeter = LoadRealPerimeter();
            float2 centre = perimeter.Aggregate(float2.zero, (sum, q) => sum + q.xz) / perimeter.Count;
            var height = Hill(centre, 90f, 450f);
            foreach (float spacing in new[] { 70f, 90f, 120f })
            {
                AssertWellFormed(GridGenerator.GenerateGrid(perimeter, Contour(height, spacing)), perimeter, height, $"real {spacing} m");
            }
        }

        [Fact]
        public void SteepSlope_GetsCurvedUphillLinks()
        {
            // 25%: a straight uphill link at <= 15% would leave the streets at under 45°, hence S-shaped uphill links.
            var perimeter = Rectangle(1200f, 900f);
            Func<float2, float> height = p => 0.25f * p.x;
            var segments = GridGenerator.GenerateGrid(perimeter, Contour(height));
            AssertWellFormed(segments, perimeter, height, "slope 25%");
            Assert.Contains(segments, s => s.IsAvenue && s.IsArc);
        }

        [Fact]
        public void RealTerrain_LeavesNoLargeGaps()
        {
            // Drawn area captured in game (outline and terrain taken from the log): an almost flat plateau and
            // varying slopes. Previously 24% of the area was more than 90 m from any street.
            var perimeter = File.ReadAllLines(Path.Combine(RoadsTestUtil.FixtureDir(), "free-area-ring.txt"))
                .Where(l => l.Trim().Length > 0).Select(l => l.Trim().Split(' '))
                .Select(q => new float3(float.Parse(q[0], CultureInfo.InvariantCulture), 0f, float.Parse(q[1], CultureInfo.InvariantCulture))).ToList();
            var height = RoadsFreeAreaReplayTests.LoadHeights("free-area-heights.txt");
            var segments = GridGenerator.GenerateGrid(perimeter, Contour(height));
            AssertWellFormed(segments, perimeter, height, "real terrain");
            var points = segments.SelectMany(s => RoadsConcentricGeneratorTests.GameCurve(s, 12)).ToList();
            float2 min = perimeter.Aggregate(new float2(float.MaxValue), (a, q) => math.min(a, q.xz));
            float2 max = perimeter.Aggregate(new float2(float.MinValue), (a, q) => math.max(a, q.xz));
            int inside = 0, far = 0;
            for (float x = min.x; x < max.x; x += 25f)
            {
                for (float z = min.y; z < max.y; z += 25f)
                {
                    var q = new float2(x, z);
                    if (!Inside(q, perimeter)) continue;
                    inside++;
                    float d = math.min(DistanceToPolygon(q, perimeter), points.Min(r => math.distance(r, q)));
                    if (d > 90f) far++;
                }
            }
            Assert.True(far <= inside * 0.02f, $"{100f * far / inside:F0}% of the area more than 90 m from a street");
        }

        [Fact]
        public void FlatTerrain_GeneratesNothing()
        {
            var perimeter = Rectangle(1000f, 800f);
            Assert.Empty(GridGenerator.GenerateGrid(perimeter, Contour(p => 12f)));
            Assert.True(GridGenerator.IsTerrainFlat(perimeter, p => 12f));
            Assert.False(GridGenerator.IsTerrainFlat(perimeter, Hill(new float2(500f, 400f), 60f, 300f)));
        }

        [Fact]
        public void GeneratesQuickly()
        {
            var perimeter = LoadRealPerimeter();
            float2 centre = perimeter.Aggregate(float2.zero, (sum, q) => sum + q.xz) / perimeter.Count;
            // Citymaker: generators no longer keep static result/distance-field caches (thread safety);
            // re-plans of an unchanged input are cached by the preview worker instead. Budgets are per
            // uncached generation, Debug build, with headroom for the parallel test run.
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 3; i++)
            {
                GridGenerator.GenerateGrid(perimeter, Contour(Hill(centre, 90f, 450f), 80f + i));
            }
            Assert.True(stopwatch.ElapsedMilliseconds < TimingBudget.Ms(2400), $"3 generations in {stopwatch.ElapsedMilliseconds} ms");
        }
    }
}
