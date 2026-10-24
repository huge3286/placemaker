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
    public class RoadsFreeAreaReplayTests
    {
        private readonly ITestOutputHelper _out;
        public RoadsFreeAreaReplayTests(ITestOutputHelper o) { _out = o; }

        internal static Func<float2, float> LoadHeights(string file)
        {
            string text = File.ReadAllText(Path.Combine(RoadsTestUtil.FixtureDir(), file));
            string[] head = text.Split('=')[0].Trim().Split(' ');
            float minX = float.Parse(head[0], CultureInfo.InvariantCulture), minZ = float.Parse(head[1], CultureInfo.InvariantCulture);
            float cell = float.Parse(head[2], CultureInfo.InvariantCulture);
            int nx = int.Parse(head[3]), nz = int.Parse(head[4]);
            float[] h = text.Split('=')[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            return p =>
            {
                float fx = math.clamp((p.x - minX) / cell, 0, nx - 1.001f), fz = math.clamp((p.y - minZ) / cell, 0, nz - 1.001f);
                int i = (int)fx, j = (int)fz; float tx = fx - i, tz = fz - j;
                float a = h[j * nx + i], b = h[j * nx + i + 1], c = h[(j + 1) * nx + i], d = h[(j + 1) * nx + i + 1];
                return math.lerp(math.lerp(a, b, tx), math.lerp(c, d, tx), tz);
            };
        }

        /// <summary>
        /// Drawn area captured in game (outline and terrain taken from the log): no pattern may leave
        /// an interior segment shorter than 16 m or a junction sharper than 35° in it.
        /// </summary>
        [Fact]
        public void RealFreeArea_HasNoShortSegmentsOrSharpJunctions()
        {
            var ring = File.ReadAllLines(Path.Combine(RoadsTestUtil.FixtureDir(), "free-area-ring.txt")).Select(l => l.Trim().Split(' '))
                .Select(q => new float3(float.Parse(q[0], CultureInfo.InvariantCulture), 0f, float.Parse(q[1], CultureInfo.InvariantCulture))).ToList();
            var height = LoadHeights("free-area-heights.txt");
            ring = ring.Select(p => new float3(p.x, height(p.xz), p.z)).ToList();
            foreach (string pattern in new[] { "grid", "loop", "contour", "organic", "tree" })
            {
                GridParameters p = GridParameters.Default;
                p.HeightAt = height;
                p.ContourMode = pattern == "contour"; p.ContourSpacing = 90; p.ContourConnectorSpacing = 250;
                p.OrganicMode = pattern == "organic"; p.TreeMode = pattern == "tree";
                List<RoadSegmentDef> interior = pattern == "loop" ? GridGenerator.GenerateLoopGrid(ring, p) : GridGenerator.GenerateGrid(ring, p);
                var road = FreeAreaPerimeter.PerimeterRoad(ring, interior);
                var all = interior.Concat(road).ToList();
                int shortCount = 0, steep = 0, sharp = 0, interiorShort = 0;
                var tangents = new Dictionary<float3, List<float2>>();
                foreach (var s in all)
                {
                    var c = RoadsConcentricGeneratorTests.GameCurve(s, 16);
                    float len = 0; for (int i = 1; i < c.Length; i++) len += math.distance(c[i - 1], c[i]);
                    if (len < 16 && !road.Contains(s)) interiorShort++;
                    if (len < 16) { shortCount++; _out.WriteLine($"{pattern} short {len:F1} {s.Start.xz} {(road.Contains(s) ? "P" : "I")}"); }
                    float grade = 0; for (int i = 1; i < c.Length; i++) grade = math.max(grade, math.abs(height(c[i]) - height(c[i - 1])) / math.distance(c[i - 1], c[i]));
                    if (grade > 0.2f) steep++;
                    void Add(float3 n, float2 t) { if (!tangents.TryGetValue(n, out var l)) tangents[n] = l = new List<float2>(); l.Add(math.normalizesafe(t)); }
                    Add(s.Start, c[1] - c[0]); Add(s.End, c[c.Length - 2] - c[c.Length - 1]);
                }
                foreach (var kv in tangents)
                    for (int i = 0; i < kv.Value.Count; i++) for (int j = i + 1; j < kv.Value.Count; j++)
                    {
                        float a = math.degrees(math.acos(math.clamp(math.dot(kv.Value[i], kv.Value[j]), -1, 1)));
                        if (a < 35) { sharp++; _out.WriteLine($"{pattern} angle {a:F0} at {kv.Key.xz}"); }
                    }
                _out.WriteLine($"== {pattern}: {interior.Count} int, {road.Count} perim, short={shortCount}, steep(>20%)={steep}, sharp={sharp}");
                Assert.Equal(0, sharp);
                Assert.True(interiorShort == 0, $"{pattern}: {interiorShort} interior segment(s) shorter than 16 m");
            }
        }
    }
}
