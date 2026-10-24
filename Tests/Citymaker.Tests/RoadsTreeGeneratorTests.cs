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
    public class RoadsTreeGeneratorTests
    {
        private static GridParameters Tree(float branch = 240f, float spacing = 80f, float length = 90f, float angle = 0f)
        {
            GridParameters p = GridParameters.Default;
            p.TreeMode = true;
            p.TreeBranchSpacing = branch;
            p.TreeCulDeSacSpacing = spacing;
            p.TreeCulDeSacLength = length;
            p.AngleOffsetDegrees = angle;
            return p;
        }

        private static List<float3> Rectangle(float width, float height, float rotationDegrees = 0f)
        {
            float a = math.radians(rotationDegrees);
            var u = new float2(math.cos(a), math.sin(a));
            var v = new float2(-u.y, u.x);
            var corners = new[] { new float2(0, 0), new float2(width, 0), new float2(width, height), new float2(0, height) };
            return corners.Select(c => { float2 w = c.x * u + c.y * v; return new float3(w.x, 0f, w.y); }).ToList();
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

        private static bool OnPerimeter(float3 q, List<float3> polygon) => DistanceToPolygon(q.xz, polygon) < 0.5f;

        private static bool ProperCross(RoadSegmentDef s, RoadSegmentDef t)
        {
            float Orient(float2 a, float2 b, float2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            float2 a1 = s.Start.xz, a2 = s.End.xz, b1 = t.Start.xz, b2 = t.End.xz;
            float d1 = Orient(b1, b2, a1), d2 = Orient(b1, b2, a2), d3 = Orient(a1, a2, b1), d4 = Orient(a1, a2, b2);
            return ((d1 > 1e-2f && d2 < -1e-2f) || (d1 < -1e-2f && d2 > 1e-2f)) && ((d3 > 1e-2f && d4 < -1e-2f) || (d3 < -1e-2f && d4 > 1e-2f));
        }

        /// <summary>Common checks: inside the shape, away from the perimeter except where the collector joins it, no crossings, everything connected to the collector.</summary>
        private static void AssertWellFormed(List<RoadSegmentDef> segments, List<float3> perimeter, string label)
        {
            Assert.Contains(segments, s => s.IsAvenue);
            Assert.Contains(segments, s => s.IsCulDeSacEnd);
            foreach (RoadSegmentDef s in segments)
            {
                foreach (float3 q in new[] { s.Start, s.End })
                {
                    float d = DistanceToPolygon(q.xz, perimeter);
                    // Only the collector touches the perimeter; everything else stays away from it.
                    Assert.True(s.IsAvenue && d < 0.5f || d >= 24f, $"{label}: node {d:F1} m from the perimeter at {q.xz}");
                }
                for (int i = 1; i < 10; i++)
                {
                    Assert.True(Inside(math.lerp(s.Start.xz, s.End.xz, i / 10f), perimeter), $"{label}: segment outside the shape at {s.Start.xz}");
                }
                if (!s.IsAvenue)
                {
                    float length = math.distance(s.Start.xz, s.End.xz);
                    for (float t = 0f; t <= length; t += 4f)
                    {
                        float2 q = math.lerp(s.Start.xz, s.End.xz, t / length);
                        Assert.True(DistanceToPolygon(q, perimeter) >= 24f, $"{label}: street {DistanceToPolygon(q, perimeter):F1} m from the perimeter at {q}");
                    }
                }
            }
            // No crossings other than at shared nodes.
            for (int i = 0; i < segments.Count; i++)
            {
                for (int j = i + 1; j < segments.Count; j++)
                {
                    Assert.False(ProperCross(segments[i], segments[j]), $"{label}: crossing between {segments[i].Start.xz} and {segments[j].Start.xz}");
                }
            }
            // Cul-de-sac ends: at least 40 m from each other (turning circles).
            var tips = segments.Where(s => s.IsCulDeSacEnd).Select(s => s.End.xz).ToList();
            for (int i = 0; i < tips.Count; i++)
            {
                for (int j = i + 1; j < tips.Count; j++)
                {
                    Assert.True(math.distance(tips[i], tips[j]) >= 39f, $"{label}: cul-de-sac ends {math.distance(tips[i], tips[j]):F0} m apart at {tips[i]}");
                }
            }
            // Everything is connected to the collector (a single network, no isolated piece).
            var adjacency = new Dictionary<float3, List<float3>>();
            void Link(float3 a, float3 b)
            {
                if (!adjacency.TryGetValue(a, out var list)) adjacency[a] = list = new List<float3>();
                list.Add(b);
            }
            foreach (RoadSegmentDef s in segments)
            {
                Link(s.Start, s.End);
                Link(s.End, s.Start);
            }
            var seen = new HashSet<float3>();
            var stack = new Stack<float3>();
            stack.Push(segments.First(s => s.IsAvenue).Start);
            while (stack.Count > 0)
            {
                float3 n = stack.Pop();
                if (!seen.Add(n)) continue;
                foreach (float3 m in adjacency[n]) stack.Push(m);
            }
            Assert.True(seen.Count == adjacency.Count, $"{label}: {adjacency.Count - seen.Count} node(s) not connected to the collector");
        }

        [Fact]
        public void Rectangle_CollectorBranchesAndCulDeSacs()
        {
            var perimeter = Rectangle(1200f, 700f, 15f);
            var segments = GridGenerator.GenerateGrid(perimeter, Tree());
            AssertWellFormed(segments, perimeter, "rectangle");
            var collector = segments.Where(s => s.IsAvenue).ToList();
            // Collector along the long side (15°), joined to the perimeter at both ends.
            float2 dir = math.normalize(collector.Last().End.xz - collector.First().Start.xz);
            Assert.True(math.abs(math.dot(dir, new float2(math.cos(math.radians(15f)), math.sin(math.radians(15f))))) > 0.999f);
            Assert.True(OnPerimeter(collector.First().Start, perimeter) && OnPerimeter(collector.Last().End, perimeter));
            // Branches on both sides, as T junctions (never two branches at the same collector node).
            var collectorNodes = new HashSet<float3>(collector.SelectMany(s => new[] { s.Start, s.End }));
            var branchStarts = segments.Where(s => !s.IsAvenue && !s.IsCulDeSacEnd && collectorNodes.Contains(s.Start)).ToList();
            Assert.True(branchStarts.Count >= 6, $"{branchStarts.Count} branches");
            Assert.All(branchStarts.GroupBy(s => s.Start), g => Assert.Single(g));
            // Cul-de-sacs of the requested length (90 m), along the collector's axis.
            var culDeSacs = segments.Where(s => s.IsCulDeSacEnd).ToList();
            Assert.True(culDeSacs.Count >= 20, $"{culDeSacs.Count} cul-de-sacs");
            Assert.All(culDeSacs, s => Assert.True(math.abs(math.dot(math.normalize(s.End.xz - s.Start.xz), dir)) > 0.999f));
            Assert.Contains(culDeSacs, s => math.abs(math.distance(s.Start.xz, s.End.xz) - 90f) < 0.5f);
        }

        [Fact]
        public void CulDeSacsBetweenBranches_KeepTheirTipsApart()
        {
            // Branches 160 m apart: cul-de-sacs shortened to (160 - 40) / 2 = 60 m so they do not touch.
            var perimeter = Rectangle(1200f, 700f);
            var segments = GridGenerator.GenerateGrid(perimeter, Tree(branch: 160f, length: 150f));
            AssertWellFormed(segments, perimeter, "tight branches");
            Assert.All(segments.Where(s => s.IsCulDeSacEnd), s => Assert.True(math.distance(s.Start.xz, s.End.xz) <= 60.5f));
        }

        [Fact]
        public void RealPerimeter_IsWellFormed()
        {
            var perimeter = LoadRealPerimeter();
            foreach ((GridParameters p, string label) in new[] { (Tree(), "default"), (Tree(branch: 160f, spacing: 60f, length: 60f), "tight"),
                         (Tree(branch: 400f, spacing: 150f, length: 150f), "large"), (Tree(angle: 40f), "40°") })
            {
                AssertWellFormed(GridGenerator.GenerateGrid(perimeter, p), perimeter, label);
            }
        }

        [Fact]
        public void RealPerimeter_GeneratesQuickly()
        {
            var perimeter = LoadRealPerimeter();
            GridGenerator.GenerateGrid(perimeter, Tree(spacing: 61f));
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 5; i++)
            {
                GridGenerator.GenerateGrid(perimeter, Tree(spacing: 62f + i)); // different settings on each run
            }
            stopwatch.Stop();
            Assert.True(stopwatch.ElapsedMilliseconds < TimingBudget.Ms(250), $"5 generations in {stopwatch.ElapsedMilliseconds} ms");
        }

        [Fact]
        public void TreeOff_GridUnchanged()
        {
            var perimeter = Rectangle(600f, 400f);
            GridParameters p = GridParameters.Default;
            int before = GridGenerator.GenerateGrid(perimeter, p).Count;
            p.TreeBranchSpacing = 300f; // pattern setting without the pattern enabled: no effect
            Assert.Equal(before, GridGenerator.GenerateGrid(perimeter, p).Count);
        }

        [Theory]
        [InlineData("tree")]
        [InlineData("organic")]
        public void PedestrianLinks_JoinCulDeSacEndsWithoutCrossingStreets(string pattern)
        {
            var perimeter = new List<float3> { new float3(0, 0, 0), new float3(1000, 0, 0), new float3(1000, 0, 700), new float3(0, 0, 700) };
            GridParameters p = GridParameters.Default;
            p.TreeMode = pattern == "tree";
            p.OrganicMode = pattern == "organic";
            p.PedestrianLinks = true;
            var segments = GridGenerator.GenerateGrid(perimeter, p);
            var links = segments.Where(s => s.IsPedestrian).ToList();
            Assert.NotEmpty(links);
            var tips = segments.Where(s => s.IsCulDeSacEnd).Select(s => s.End).ToList();
            var nodes = segments.Where(s => !s.IsPedestrian).SelectMany(s => new[] { s.Start, s.End }).ToList();
            var streets = segments.Where(s => !s.IsPedestrian).Select(s => RoadsConcentricGeneratorTests.GameCurve(s, 16)).ToList();
            foreach (RoadSegmentDef link in links)
            {
                float length = math.distance(link.Start.xz, link.End.xz);
                Assert.InRange(length, 15f, GridGenerator.PedestrianLinkMaxLength);
                Assert.Contains(link.Start, tips);
                bool onPerimeter = link.End.x < 0.5f || link.End.x > 999.5f || link.End.z < 0.5f || link.End.z > 699.5f;
                Assert.True(onPerimeter || nodes.Contains(link.End), $"{pattern}: link to {link.End.xz}, neither a node nor the perimeter");
                foreach (float2[] street in streets)
                {
                    for (int k = 1; k < street.Length; k++)
                    {
                        Assert.False(Crosses(link.Start.xz, link.End.xz, street[k - 1], street[k]), $"{pattern}: link {link.Start.xz} → {link.End.xz} crosses a street");
                    }
                }
            }
        }

        private static bool Crosses(float2 a1, float2 a2, float2 b1, float2 b2)
        {
            float O(float2 a, float2 b, float2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            float d1 = O(b1, b2, a1), d2 = O(b1, b2, a2), d3 = O(a1, a2, b1), d4 = O(a1, a2, b2);
            return ((d1 > 0.5f && d2 < -0.5f) || (d1 < -0.5f && d2 > 0.5f)) && ((d3 > 0.5f && d4 < -0.5f) || (d3 < -0.5f && d4 > 0.5f));
        }
    }
}
