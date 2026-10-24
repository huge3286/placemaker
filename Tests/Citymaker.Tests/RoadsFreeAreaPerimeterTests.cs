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
    public class RoadsFreeAreaPerimeterTests
    {
        private static readonly List<float3> Clicked = new List<float3>
        {
            new float3(0, 0, 0), new float3(700, 0, -80), new float3(1150, 0, 250),
            new float3(1000, 0, 800), new float3(450, 0, 950), new float3(-60, 0, 600),
        };

        private static Func<float2, float> Hill(float2 centre, float height, float radius) =>
            p => height * math.exp(-math.lengthsq(p - centre) / (2f * radius * radius));

        private static float DistanceToRing(float2 p, IReadOnlyList<float3> ring)
        {
            float best = float.MaxValue;
            for (int i = 0; i < ring.Count; i++)
            {
                float2 a = ring[i].xz, b = ring[(i + 1) % ring.Count].xz, ab = b - a;
                float t = math.saturate(math.dot(p - a, ab) / math.max(math.lengthsq(ab), 1e-6f));
                best = math.min(best, math.distance(p, a + t * ab));
            }
            return best;
        }

        [Fact]
        public void IsSimple_RejectsCrossingAndTinyShapes()
        {
            Assert.True(FreeAreaPerimeter.IsSimple(Clicked));
            var bowtie = new List<float3> { new float3(0, 0, 0), new float3(300, 0, 300), new float3(300, 0, 0), new float3(0, 0, 300) };
            Assert.False(FreeAreaPerimeter.IsSimple(bowtie));
            var tiny = new List<float3> { new float3(0, 0, 0), new float3(20, 0, 0), new float3(0, 0, 20) };
            Assert.False(FreeAreaPerimeter.IsSimple(tiny));
        }

        [Fact]
        public void Smooth_RoundsCornersWithoutLeavingTheShape()
        {
            List<float3> ring = FreeAreaPerimeter.Smooth(Clicked);
            Assert.True(ring.Count > Clicked.Count * 3);
            for (int i = 0; i < ring.Count; i++)
            {
                // Every point stays on or inside the clicked polygon, and close to its outline.
                Assert.True(DistanceToRing(ring[i].xz, Clicked) < FreeAreaPerimeter.CornerRadius);
                float2 a = ring[i].xz, b = ring[(i + 1) % ring.Count].xz, c = ring[(i + 2) % ring.Count].xz;
                float turn = math.degrees(math.acos(math.clamp(math.dot(math.normalize(b - a), math.normalize(c - b)), -1f, 1f)));
                Assert.True(turn < 25f, $"{turn:F0}° turn at {b}");
            }
        }

        [Theory]
        [InlineData("contour")]
        [InlineData("grid")]
        [InlineData("organic")]
        public void PerimeterRoad_FollowsTheRingAndMeetsEveryStreet(string pattern)
        {
            var height = Hill(new float2(550f, 420f), 70f, 330f);
            List<float3> ring = FreeAreaPerimeter.Smooth(Clicked).ToList();
            GridParameters parameters = GridParameters.Default;
            parameters.HeightAt = height;
            parameters.ContourMode = pattern == "contour";
            parameters.ContourSpacing = 90f;
            parameters.ContourConnectorSpacing = 250f;
            parameters.OrganicMode = pattern == "organic";
            List<RoadSegmentDef> interior = GridGenerator.GenerateGrid(ring, parameters);
            Assert.NotEmpty(interior);
            List<RoadSegmentDef> road = FreeAreaPerimeter.PerimeterRoad(ring, interior);

            // Closed chain: every node of the perimeter road has exactly two segments.
            var degree = new Dictionary<float3, int>();
            foreach (RoadSegmentDef s in road)
            {
                degree[s.Start] = degree.TryGetValue(s.Start, out int a) ? a + 1 : 1;
                degree[s.End] = degree.TryGetValue(s.End, out int b) ? b + 1 : 1;
            }
            Assert.All(degree, pair => Assert.Equal(2, pair.Value));

            // Every street end lying on the outline is a node of the road (same float3).
            foreach (RoadSegmentDef s in interior)
            {
                foreach (float3 end in new[] { s.Start, s.End })
                {
                    if (DistanceToRing(end.xz, ring) < 0.5f)
                    {
                        Assert.True(degree.ContainsKey(end), $"{pattern}: street at {end.xz} not joined to the perimeter road");
                    }
                }
            }

            // The curve the game actually builds hugs the outline (within a few metres where a segment
            // chains a straight piece and a corner: interior streets stay at least 22 m away).
            float total = 0f;
            foreach (RoadSegmentDef s in road)
            {
                float2[] curve = RoadsConcentricGeneratorTests.GameCurve(s, 24);
                float length = 0f;
                for (int i = 1; i < curve.Length; i++) length += math.distance(curve[i - 1], curve[i]);
                total += length;
                Assert.True(length <= FreeAreaPerimeter.MaxPieceLength * 1.15f, $"{pattern}: {length:F0} m segment from {s.Start.xz} to {s.End.xz} arc={s.IsArc}");
                foreach (float2 q in curve)
                {
                    Assert.True(DistanceToRing(q, ring) < 1.5f, $"{pattern}: curve {DistanceToRing(q, ring):F1} m from the outline at {q}");
                }
            }
            float ringLength = 0f;
            for (int i = 0; i < ring.Count; i++) ringLength += math.distance(ring[i].xz, ring[(i + 1) % ring.Count].xz);
            Assert.InRange(total, ringLength - 2f, ringLength + 2f);

            string outDir = Environment.GetEnvironmentVariable("GRG_OUT");
            if (!string.IsNullOrEmpty(outDir))
            {
                var inv = CultureInfo.InvariantCulture;
                var lines = new List<string> { "clicked " + string.Join(";", Clicked.Select(p => $"{p.x.ToString(inv)} {p.z.ToString(inv)}")) };
                lines.Add("ring " + string.Join(";", ring.Select(p => $"{p.x.ToString(inv)} {p.z.ToString(inv)}")));
                foreach (RoadSegmentDef s in interior.Concat(road))
                {
                    string kind = road.Contains(s) ? "P" : s.IsAvenue ? "A" : "S";
                    lines.Add(kind + " " + string.Join(";", RoadsConcentricGeneratorTests.GameCurve(s, 16).Select(q => $"{q.x.ToString(inv)} {q.y.ToString(inv)}")));
                }
                File.WriteAllLines(Path.Combine(outDir, $"freearea-{pattern}.txt"), lines);
            }
        }
    }
}
