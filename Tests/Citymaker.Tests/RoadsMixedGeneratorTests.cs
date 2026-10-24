using System.Collections.Generic;
using System.Linq;
using Citymaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests
{
    public class RoadsMixedGeneratorTests
    {
        private readonly ITestOutputHelper _out;
        public RoadsMixedGeneratorTests(ITestOutputHelper output) { _out = output; }

        private static GridParameters Mixed(float radius = 220f, int seed = 1)
        {
            GridParameters p = GridParameters.Default;
            p.MixedMode = true;
            p.MixedCoreRadius = radius;
            p.RadialAvenues = 6;
            p.RadialRoundaboutRadius = 40f;
            p.RadialLayers = 2;
            p.OrganicStreetSpacing = 80f;
            p.OrganicCurviness = 60f;
            p.OrganicLoopShare = 30f;
            p.OrganicSeed = seed;
            return p;
        }

        private static bool Crosses(float2 a1, float2 a2, float2 b1, float2 b2)
        {
            float O(float2 a, float2 b, float2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            float d1 = O(b1, b2, a1), d2 = O(b1, b2, a2), d3 = O(a1, a2, b1), d4 = O(a1, a2, b2);
            return ((d1 > 0.5f && d2 < -0.5f) || (d1 < -0.5f && d2 > 0.5f)) && ((d3 > 0.5f && d4 < -0.5f) || (d3 < -0.5f && d4 > 0.5f));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(23)]
        public void RadialCoreAndOrganicRing_AreJoinedWithoutCrossings(int seed)
        {
            var perimeter = new List<float3> { new float3(0, 0, 0), new float3(1400, 0, 0), new float3(1400, 0, 1000), new float3(0, 0, 1000) };
            var segments = GridGenerator.GenerateGrid(perimeter, Mixed(seed: seed));
            float2 centre = new float2(700f, 500f);
            int inside = segments.Count(s => math.distance((s.Start.xz + s.End.xz) * 0.5f, centre) < 200f);
            int outside = segments.Count(s => math.distance((s.Start.xz + s.End.xz) * 0.5f, centre) > 240f);
            int ringRoad = segments.Count(s => s.IsAvenue && math.abs(math.distance(s.Start.xz, centre) - 220f) < 1f && math.abs(math.distance(s.End.xz, centre) - 220f) < 1f);
            _out.WriteLine($"seed {seed}: {segments.Count} segments, {inside} in the centre, {outside} around it, {ringRoad} on the ring");
            Assert.True(inside > 5 && outside > 10 && ringRoad >= 6);
            string outDir = System.Environment.GetEnvironmentVariable("GRG_OUT");
            if (!string.IsNullOrEmpty(outDir) && seed == 1)
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var lines = segments.Select(s => (s.IsRoundabout ? "R" : s.IsAvenue ? "A" : s.IsLocal || s.IsCulDeSacEnd ? "L" : "S") + " "
                    + string.Join(";", RoadsConcentricGeneratorTests.GameCurve(s, 12).Select(q => $"{q.x.ToString(inv)} {q.y.ToString(inv)}"))).ToList();
                System.IO.File.WriteAllLines(System.IO.Path.Combine(outDir, "mixed.txt"), lines);
            }

            // No crossings (game curves).
            var curves = segments.Select(s => RoadsConcentricGeneratorTests.GameCurve(s, 12)).ToList();
            for (int a = 0; a < curves.Count; a++)
                for (int b = a + 1; b < curves.Count; b++)
                    for (int i = 1; i < curves[a].Length; i++)
                        for (int j = 1; j < curves[b].Length; j++)
                            Assert.False(Crosses(curves[a][i - 1], curves[a][i], curves[b][j - 1], curves[b][j]), $"crossing near {curves[a][i]}");

            // Everything is connected to the outer perimeter.
            var adjacency = new Dictionary<float3, List<float3>>();
            foreach (RoadSegmentDef s in segments)
            {
                if (!adjacency.TryGetValue(s.Start, out var la)) adjacency[s.Start] = la = new List<float3>();
                if (!adjacency.TryGetValue(s.End, out var lb)) adjacency[s.End] = lb = new List<float3>();
                la.Add(s.End);
                lb.Add(s.Start);
            }
            bool OnOuter(float3 p) => p.x < 0.5f || p.x > 1399.5f || p.z < 0.5f || p.z > 999.5f;
            var seen = new HashSet<float3>();
            var stack = adjacency.Keys.Where(OnOuter).ToList();
            Assert.NotEmpty(stack);
            while (stack.Count > 0)
            {
                float3 n = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                if (!seen.Add(n)) continue;
                stack.AddRange(adjacency[n]);
            }
            Assert.True(seen.Count == adjacency.Count, $"{adjacency.Count - seen.Count} node(s) not connected to the perimeter");
        }

        [Fact]
        public void NarrowShape_FallsBackToOrganic()
        {
            var perimeter = new List<float3> { new float3(0, 0, 0), new float3(1400, 0, 0), new float3(1400, 0, 200), new float3(0, 0, 200) };
            GridParameters organic = Mixed();
            organic.MixedMode = false;
            organic.OrganicMode = true;
            Assert.Equal(GridGenerator.GenerateGrid(perimeter, organic).Count, GridGenerator.GenerateGrid(perimeter, Mixed()).Count);
        }
    }
}
