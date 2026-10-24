using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Citymaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests
{
    public class RoadsContourContinuityTests
    {
        private readonly ITestOutputHelper _out;
        public RoadsContourContinuityTests(ITestOutputHelper o) { _out = o; }

        /// <summary>
        /// Contour pattern on the real terrain captured in game (user feedback: "the roads lose continuity"):
        /// at most 3.2 free ends per km of road (5 to 6 before the fix, counting ends on the perimeter).
        /// </summary>
        [Fact]
        public void RealTerrain_StreetsKeepTheirContinuity()
        {
            var ring = File.ReadAllLines(Path.Combine(RoadsTestUtil.FixtureDir(), "free-area-ring.txt")).Select(l => l.Trim().Split(' '))
                .Select(q => new float3(float.Parse(q[0], CultureInfo.InvariantCulture), 0f, float.Parse(q[1], CultureInfo.InvariantCulture))).ToList();
            var height = RoadsFreeAreaReplayTests.LoadHeights("free-area-heights.txt");
            foreach (float spacing in new[] { 60f, 90f, 150f })
            {
                GridParameters p = GridParameters.Default;
                p.ContourMode = true; p.ContourSpacing = spacing; p.ContourConnectorSpacing = 250f; p.HeightAt = height;
                var segs = GridGenerator.GenerateGrid(ring.Select(q => new float3(q.x, height(q.xz), q.z)).ToList(), p);
                var degree = new Dictionary<(int, int), int>();
                (int, int) K(float3 v) => ((int)math.round(v.x * 2), (int)math.round(v.z * 2));
                float length = 0f;
                foreach (var s in segs)
                {
                    degree[K(s.Start)] = degree.TryGetValue(K(s.Start), out int a) ? a + 1 : 1;
                    degree[K(s.End)] = degree.TryGetValue(K(s.End), out int b) ? b + 1 : 1;
                    length += math.distance(s.Start.xz, s.End.xz);
                }
                int dead = degree.Values.Count(d => d == 1);
                int junctions = degree.Values.Count(d => d >= 3);
                _out.WriteLine($"spacing {spacing}: {segs.Count} segments, {length / 1000f:F1} km, {dead} free ends, {junctions} junctions, ends per km {dead / (length / 1000f):F1}");
                Assert.True(dead / (length / 1000f) <= 3.2f, $"spacing {spacing}: {dead / (length / 1000f):F1} free ends per km");
            }
        }
    }
}
