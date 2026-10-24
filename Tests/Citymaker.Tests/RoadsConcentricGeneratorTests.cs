using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Citymaker.Core.Layers.Roads.Generators;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    [Collection(TimingCollection.Name)]
    public class RoadsConcentricGeneratorTests
    {
        private readonly ConcentricDiagnostics _diag = new ConcentricDiagnostics();
        private readonly RadialDiagnostics _radial = new RadialDiagnostics();

        private static List<float3> Square(float size) => new List<float3>
        {
            new float3(0f, 0f, 0f), new float3(size, 0f, 0f), new float3(size, 0f, size), new float3(0f, 0f, size),
        };

        /// <summary>600×600 L with the top-right quarter removed: one concave corner.</summary>
        private static List<float3> LShape() => new List<float3>
        {
            new float3(0f, 0f, 0f), new float3(600f, 0f, 0f), new float3(600f, 0f, 300f),
            new float3(300f, 0f, 300f), new float3(300f, 0f, 600f), new float3(0f, 0f, 600f),
        };

        /// <summary>Two 400 m squares joined by a 60 m corridor: the rings split in two.</summary>
        private static List<float3> Dumbbell() => new List<float3>
        {
            new float3(0f, 0f, 0f), new float3(400f, 0f, 0f), new float3(400f, 0f, 170f), new float3(600f, 0f, 170f),
            new float3(600f, 0f, 0f), new float3(1000f, 0f, 0f), new float3(1000f, 0f, 400f), new float3(600f, 0f, 400f),
            new float3(600f, 0f, 230f), new float3(400f, 0f, 230f), new float3(400f, 0f, 400f), new float3(0f, 0f, 400f),
        };

        private static List<float3> Circle(float radius, int points)
        {
            var result = new List<float3>();
            for (int i = 0; i < points; i++)
            {
                float a = i * 2f * math.PI / points;
                result.Add(new float3(radius * math.cos(a), 0f, radius * math.sin(a)));
            }
            return result;
        }

        private static bool PointInPolygon(float2 p, List<float3> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                float2 a = polygon[i].xz;
                float2 b = polygon[j].xz;
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static float DistanceToPolygon(float2 p, List<float3> polygon)
        {
            float best = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                float2 a = polygon[i].xz;
                float2 b = polygon[(i + 1) % polygon.Count].xz;
                float2 ab = b - a;
                float t = math.clamp(math.dot(p - a, ab) / math.lengthsq(ab), 0f, 1f);
                best = math.min(best, math.distance(p, a + t * ab));
            }
            return best;
        }

        private static float2 PerimeterEdgeDirection(float2 p, List<float3> polygon)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                float2 a = polygon[i].xz;
                float2 ab = polygon[(i + 1) % polygon.Count].xz - a;
                float t = math.clamp(math.dot(p - a, ab) / math.lengthsq(ab), 0f, 1f);
                float d = math.distance(p, a + t * ab);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            return math.normalize(polygon[(best + 1) % polygon.Count].xz - polygon[best].xz);
        }

        private static float CurveLength(float2[] curve)
        {
            float length = 0f;
            for (int i = 1; i < curve.Length; i++)
            {
                length += math.distance(curve[i - 1], curve[i]);
            }
            return length;
        }

        private static bool Cross(float2 a1, float2 a2, float2 b1, float2 b2)
        {
            float d1 = Orient(b1, b2, a1), d2 = Orient(b1, b2, a2), d3 = Orient(a1, a2, b1), d4 = Orient(a1, a2, b2);
            return ((d1 > 1e-3f && d2 < -1e-3f) || (d1 < -1e-3f && d2 > 1e-3f))
                && ((d3 > 1e-3f && d4 < -1e-3f) || (d3 < -1e-3f && d4 > 1e-3f));
        }

        private static float Orient(float2 a, float2 b, float2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        /// <summary>
        /// Invariants shared by every shape: nothing outside the perimeter, no roads crossing
        /// without a shared node, and every endpoint is either a shared node or lies on the
        /// perimeter (where the game connects it to the existing road) — never a dead end.
        /// </summary>
        private static void AssertWellFormed(List<RoadSegmentDef> segments, List<float3> perimeter)
        {
            Assert.NotEmpty(segments);
            foreach (RoadSegmentDef s in segments)
            {
                float2 mid = (s.Start.xz + s.End.xz) * 0.5f;
                Assert.True(PointInPolygon(mid, perimeter) || DistanceToPolygon(mid, perimeter) < 0.5f,
                    $"Segment outside the perimeter: {s.Start} -> {s.End}");
            }

            for (int i = 0; i < segments.Count; i++)
            {
                for (int j = i + 1; j < segments.Count; j++)
                {
                    Assert.False(Cross(segments[i].Start.xz, segments[i].End.xz, segments[j].Start.xz, segments[j].End.xz),
                        $"Crossing without a node: {segments[i].Start}->{segments[i].End} × {segments[j].Start}->{segments[j].End}");
                }
            }

            var counts = new Dictionary<(float, float), int>();
            foreach (RoadSegmentDef s in segments)
            {
                foreach (float3 p in new[] { s.Start, s.End })
                {
                    var key = (p.x, p.z);
                    counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            }
            foreach (var kv in counts)
            {
                float2 p = new float2(kv.Key.Item1, kv.Key.Item2);
                Assert.True(kv.Value > 1 || DistanceToPolygon(p, perimeter) < 0.5f, $"Dead end at {p}");
            }

            // The whole network must be connected to the perimeter (existing road): no isolated ring.
            var parent = new Dictionary<(float, float), (float, float)>();
            var root = (float.NaN, float.NaN);
            (float, float) Find((float, float) x)
            {
                while (!parent[x].Equals(x)) x = parent[x];
                return x;
            }
            void Union((float, float) a, (float, float) b) => parent[Find(a)] = Find(b);
            parent[root] = root;
            foreach (RoadSegmentDef s in segments)
            {
                var a = (s.Start.x, s.Start.z);
                var b = (s.End.x, s.End.z);
                if (!parent.ContainsKey(a)) parent[a] = a;
                if (!parent.ContainsKey(b)) parent[b] = b;
                Union(a, b);
                if (DistanceToPolygon(s.Start.xz, perimeter) < 0.5f) Union(a, root);
                if (DistanceToPolygon(s.End.xz, perimeter) < 0.5f) Union(b, root);
            }
            foreach (var node in parent.Keys.ToList())
            {
                Assert.True(Find(node).Equals(Find(root)), $"Node {node} not connected to the perimeter");
            }
        }

        [Fact]
        public void Square_TwoLayers_TwoNestedSquaresEvenlySpacedToTheCentre()
        {
            // 600 m: max depth 300 m -> 2 rings at 100 m and 200 m from the edge.
            var perimeter = Square(600f);
            var segments = ConcentricGenerator.Generate(perimeter, layers: 2, connections: 4, diagnostics: _diag);
            AssertWellFormed(segments, perimeter);

            // Every ring vertex is within ±12 % of 100 m or 200 m from the edge (rings parallel
            // to the sides, at a constant fraction of the half-width).
            var ringDepths = segments
                .SelectMany(s => new[] { s.Start.xz, s.End.xz })
                .Select(p => DistanceToPolygon(p, perimeter))
                .Where(d => d > 1f)
                .ToList();
            Assert.Contains(ringDepths, d => d < 150f);
            Assert.Contains(ringDepths, d => d > 150f);
            Assert.All(ringDepths, d => Assert.True(math.abs(d - 100f) < 12f || math.abs(d - 200f) < 24f, $"Vertex {d:F0} m from the edge"));
        }

        [Fact]
        public void LayerCount_IsCappedWhenTheShapeIsTooNarrow()
        {
            // 300 m: depth 150 m -> at most 2 rings at MinLayerSpacing (40 m).
            var perimeter = Square(300f);
            var many = ConcentricGenerator.Generate(perimeter, layers: 10, connections: 4, diagnostics: _diag);
            AssertWellFormed(many, perimeter);
            float minDepth = many.SelectMany(s => new[] { s.Start.xz, s.End.xz })
                .Select(p => DistanceToPolygon(p, perimeter)).Where(d => d > 1f).Min();
            Assert.True(minDepth >= ConcentricGenerator.MinLayerSpacing - 2f, $"Rings too tight: {minDepth} m");
        }

        [Fact]
        public void MaxLayers_DependsOnTheShape_AndGenerateNeverExceedsIt()
        {
            // User feedback: "there needs to be a ring limit depending on the size of the
            // perimeter, and the panel should simply block going past the limit".
            int small = ConcentricGenerator.MaxLayers(Square(300f));
            int large = ConcentricGenerator.MaxLayers(Square(1200f));
            Assert.InRange(small, 1, 3);
            Assert.True(large > small, $"A square 4× larger should allow more rings ({large} vs {small})");
            Assert.Equal(0, ConcentricGenerator.MaxLayers(Square(80f)));

            foreach (var perimeter in new[] { Square(300f), Square(1200f), LShape(), Ellipse(600f, 220f, 400) })
            {
                int max = ConcentricGenerator.MaxLayers(perimeter);
                ConcentricGenerator.Generate(perimeter, layers: ConcentricGenerator.MaxLayersLimit, connections: 4, diagnostics: _diag);
                Assert.Equal(max, _diag.RingLoopCounts.Length);
            }
        }

        [Fact]
        public void ConcaveLShape_NeverSelfIntersectsNorLeavesThePerimeter()
        {
            var perimeter = LShape();
            foreach (int layers in new[] { 1, 2, 3 })
            {
                AssertWellFormed(ConcentricGenerator.Generate(perimeter, layers, connections: 4, diagnostics: _diag), perimeter);
            }
        }

        [Fact]
        public void WaistedShape_EveryRingGoesAllAround_TighterInTheNarrowPassage()
        {
            // Two 400 m squares joined by a 300 m wide passage (half-width 150 m).
            // User feedback: rings must not split ("see why it divides"), nor stop in a point
            // where they should carry on ("the tips are back... where the ring is supposed to
            // continue"), nor be capped by the passage ("it doesn't go beyond 4 layers"): every
            // ring goes around both squares, tighter in the passage.
            var perimeter = new List<float3>
            {
                new float3(0f, 0f, 0f), new float3(400f, 0f, 0f), new float3(400f, 0f, 50f), new float3(600f, 0f, 50f),
                new float3(600f, 0f, 0f), new float3(1000f, 0f, 0f), new float3(1000f, 0f, 400f), new float3(600f, 0f, 400f),
                new float3(600f, 0f, 350f), new float3(400f, 0f, 350f), new float3(400f, 0f, 400f), new float3(0f, 0f, 400f),
            };
            var segments = ConcentricGenerator.Generate(perimeter, layers: 6, connections: 4, diagnostics: _diag);
            AssertWellFormed(segments, perimeter);

            int[] loopsPerRing = _diag.RingLoopCounts;
            // The old limit (rings at least 40 m apart, bounded by the passage) left only 2.
            Assert.True(loopsPerRing.Length >= 2, $"Only {loopsPerRing.Length} rings");
            Assert.All(loopsPerRing, count => Assert.True(count == 1, $"Ring split into {count} pieces"));
            Assert.Contains(segments, s => s.Start.x < 400f && s.End.x < 400f);
            Assert.Contains(segments, s => s.Start.x > 600f && s.End.x > 600f);
        }

        [Fact]
        public void DenseCurvedPerimeter_IsFastAndWellFormed()
        {
            // Densified roundabout/curved avenue: 525 points, like the real perimeter from the log.
            var perimeter = Circle(500f, 525);
            var stopwatch = Stopwatch.StartNew();
            var segments = ConcentricGenerator.Generate(perimeter, layers: 4, connections: 6, diagnostics: _diag);
            stopwatch.Stop();
            AssertWellFormed(segments, perimeter);
            Assert.True(stopwatch.ElapsedMilliseconds < TimingBudget.Ms(600), $"Generation too slow: {stopwatch.ElapsedMilliseconds} ms");
        }

        private static List<float3> Ellipse(float a, float b, int points)
        {
            var result = new List<float3>();
            for (int i = 0; i < points; i++)
            {
                float t = i * 2f * math.PI / points;
                result.Add(new float3(a * math.cos(t), 0f, b * math.sin(t)));
            }
            return result;
        }

        /// <summary>
        /// Depth difference (m) tolerated between the two ends of a ring segment: rings wobble
        /// by a few metres, and a curved segment now covers a long stretch (see CanSpanAsArc);
        /// a spoke, on the other hand, joins two rings at least 40 m apart.
        /// </summary>
        private const float RingDepthTolerance = 10f;

        /// <summary>
        /// Largest turn (degrees) between two consecutive RING segments (spokes, which join two
        /// different depths, are ignored), taking the tangents of curved segments into account.
        /// </summary>
        private static float MaxRingTurnDegrees(List<RoadSegmentDef> segments, List<float3> perimeter)
        {
            var rings = segments.Where(s => math.abs(DistanceToPolygon(s.Start.xz, perimeter) - DistanceToPolygon(s.End.xz, perimeter)) < RingDepthTolerance).ToList();
            float2 TangentOut(RoadSegmentDef s) => s.IsArc ? math.normalize(s.StartTangent.xz) : math.normalize(s.End.xz - s.Start.xz);
            float2 TangentIn(RoadSegmentDef s) => s.IsArc ? math.normalize(s.EndTangent.xz) : math.normalize(s.End.xz - s.Start.xz);
            float max = 0f;
            foreach (RoadSegmentDef incoming in rings)
            {
                foreach (RoadSegmentDef outgoing in rings)
                {
                    if (math.all(outgoing.Start == incoming.End) && !math.all(outgoing.End == incoming.Start))
                    {
                        float cos = math.clamp(math.dot(TangentIn(incoming), TangentOut(outgoing)), -1f, 1f);
                        max = math.max(max, math.degrees(math.acos(cos)));
                    }
                }
            }
            return max;
        }

        [Fact]
        public void RoundedShape_InnerRingsKeepTheirCurvature_NoPointedTips()
        {
            // User feedback (screenshot): on a curved shape, the deep rings ended "with a tip"
            // (the exact offset turns pointed once the depth exceeds the radius of curvature at
            // the end: here 200²/500 = 80 m, rings at 50/100/150 m).
            var perimeter = Ellipse(500f, 200f, 400);
            var segments = ConcentricGenerator.Generate(perimeter, layers: 3, connections: 4, diagnostics: _diag);
            AssertWellFormed(segments, perimeter);
            float maxTurn = MaxRingTurnDegrees(segments, perimeter);
            Assert.True(maxTurn < 5f, $"Ring kinked or pointed: {maxTurn:F0}° turn");
        }

        /// <summary>
        /// Round lobe (radius 300 m) extended to the left by a narrow arm (150 m wide, rounded
        /// end): no sharp edge, but a "waist" — the lobe's deep rings close up near the arm.
        /// </summary>
        private static List<float3> LobeWithArm()
        {
            var result = new List<float3>();
            float junctionAngle = math.asin(75f / 300f);
            // Circle, from the arm's upper attachment to its lower attachment (going round the right).
            for (float a = math.PI - junctionAngle; a > -math.PI + junctionAngle; a -= 0.03f)
            {
                result.Add(new float3(300f * math.cos(a), 0f, 300f * math.sin(a)));
            }
            // Arm: lower edge towards the left, semicircular end, upper edge towards the right.
            for (float x = -290f; x > -700f; x -= 20f) result.Add(new float3(x, 0f, -75f));
            for (float a = -math.PI / 2f; a > -3f * math.PI / 2f; a -= 0.1f)
            {
                result.Add(new float3(-700f + 75f * math.cos(a), 0f, 75f * math.sin(a)));
            }
            for (float x = -700f; x < -290f; x += 20f) result.Add(new float3(x, 0f, 75f));
            return result;
        }

        [Fact]
        public void LobeWithNarrowArm_DeepRingsInTheLobeHaveNoPointedTips()
        {
            // User feedback (screenshot): "the tips are back" — rings that only fit inside the
            // lobe ended in a point towards the arm.
            var perimeter = LobeWithArm();
            var segments = ConcentricGenerator.Generate(perimeter, layers: 6, connections: 4, diagnostics: _diag);
            AssertWellFormed(segments, perimeter);
            float maxTurn = MaxRingTurnDegrees(segments, perimeter);
            Assert.True(maxTurn < 5f, $"Pointed ring: {maxTurn:F0}° turn");
        }

        /// <summary>Smallest radius (m) of the circle through three consecutive vertices of a closed ring.</summary>
        private static float MinThreePointRadius(List<float2> loop)
        {
            float min = float.MaxValue;
            for (int i = 0; i < loop.Count; i++)
            {
                float2 a = loop[(i - 1 + loop.Count) % loop.Count], p = loop[i], b = loop[(i + 1) % loop.Count];
                float2 dIn = math.normalize(p - a), dOut = math.normalize(b - p);
                float sin = math.abs(dIn.x * dOut.y - dIn.y * dOut.x);
                float radius = math.dot(dIn, dOut) < 0f ? 0f : sin < 1e-6f ? float.MaxValue : math.distance(a, b) / (2f * sin);
                min = math.min(min, radius);
            }
            return min;
        }

        [Fact]
        public void LimitCurvature_RoundsAPointedTip_KeepsTrueCornersAndStaysInside()
        {
            // User feedback (screenshot, ring ending in a point facing a U-shaped street):
            // "there are still some tips in other complex shapes". Safety net whatever the cause:
            // a teardrop with a 40° tip (vertices every 25 m), and a true corner (inherited from
            // the perimeter) on the opposite rounded end, which must stay in place.
            var loop = new List<float2>();
            var tip = new float2(0f, 0f);
            float half = math.radians(20f);
            float2 upper = new float2(math.cos(half), math.sin(half));
            float2 lower = new float2(math.cos(half), -math.sin(half));
            for (float d = 0f; d < 400f; d += 25f) loop.Add(tip + lower * d);
            float2 centre = new float2(400f * math.cos(half), 0f);
            float radius = 400f * math.sin(half);
            for (float a = -math.PI / 2f + 0.2f; a < math.PI / 2f - 0.1f; a += 0.2f) loop.Add(centre + radius * new float2(math.cos(a), math.sin(a)));
            for (float d = 400f; d > 0f; d -= 25f) loop.Add(tip + upper * d);
            var corners = new HashSet<(float, float)> { (loop[20].x, loop[20].y) };
            float2 corner = loop[20];
            var original = new List<float2>(loop);

            Assert.True(MinThreePointRadius(loop) < 10f, "The initial teardrop should have a tip.");
            ConcentricGenerator.LimitCurvature(loop, corners, 60f);

            Assert.Equal(corner, loop[20]);
            // The tip has moved back towards the inside of its curve, never going past the original shape.
            Assert.True(loop.Min(p => p.x) > 20f, $"Tip not rounded: min x = {loop.Min(p => p.x):F0}");
            var outline = original.Select(p => new float3(p.x, 0f, p.y)).ToList();
            Assert.All(loop, p => Assert.True(PointInPolygon(p, outline) || DistanceToPolygon(p, outline) < 0.5f, $"Vertex outside: {p}"));
            // No turn tighter than the minimum radius, except at the true corner and its neighbours.
            var withoutCorner = loop.Where((p, i) => math.abs(i - 20) > 1).ToList();
            Assert.True(MinThreePointRadius(withoutCorner) > 40f, $"Still a tip: radius {MinThreePointRadius(withoutCorner):F0} m");
        }

        [Fact]
        public void CurvedRings_UseFewLongArcs_NotANodeEvery25Metres()
        {
            // User feedback (screenshot): "when the mod generates the curves, it creates too many nodes".
            // 500 m circle, 3 rings (radii ~375/250/125 m): a node every 25 m gave ~190; with arcs
            // of at most 90° split at the spoke connections, fewer than 40 (28 measured).
            var perimeter = Circle(500f, 400);
            var segments = ConcentricGenerator.Generate(perimeter, layers: 3, connections: 4, diagnostics: _diag);
            AssertWellFormed(segments, perimeter);
            var rings = segments.Where(s => math.abs(DistanceToPolygon(s.Start.xz, perimeter) - DistanceToPolygon(s.End.xz, perimeter)) < RingDepthTolerance).ToList();
            Assert.True(rings.Count <= 40, $"{rings.Count} ring segments");
            // Every segment stays true to the circle: its midpoint (game curve ≈ arc) is at the right radius.
            foreach (RoadSegmentDef s in rings)
            {
                float r0 = math.length(s.Start.xz), r1 = math.length(s.End.xz);
                Assert.True(math.abs(r0 - r1) < RingDepthTolerance, $"Segment changes radius: {r0:F0} -> {r1:F0}");
            }
        }

        [Fact]
        public void RealPerimeterWithBacktrack_RingsHaveNoPointedTips()
        {
            // User feedback (persistent tips, "they only appear when a connecting road passes
            // through"): real 990-point perimeter, exported from the game via the [Diag concêntrico]
            // log. Where two sampled roads join, the outline backs up by ~5 m and then carries on
            // (179° turn). This ultra-sharp false corner "protected" every ring tip in the shape as
            // an inherited corner — 17 kinks of 57° to 128°, never rounded.
            string path = System.IO.Path.Combine(RoadsTestUtil.FixtureDir(), "real-perimeter-backtrack.txt");
            var perimeter = System.IO.File.ReadAllLines(path)
                .Where(l => l.Trim().Length > 0)
                .Select(l => l.Trim().Split(' '))
                .Select(a => new float3(float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture), 0f,
                    float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();
            foreach ((int layers, int connections) in new[] { (10, 2), (10, 4), (3, 2) })
            {
                var segments = ConcentricGenerator.Generate(perimeter, layers, connections, diagnostics: _diag);
                float maxTurn = MaxRingTurnDegrees(segments, perimeter);
                Assert.True(maxTurn < 20f, $"{layers} rings, {connections} spokes: ring kinked at {maxTurn:F0}°");
            }

            // Next user feedback: "it is creating ever more circular shapes instead of keeping
            // the original shape" (blurred distance field). Every ring must stay at a nearly
            // constant distance from the outline. Measured on this perimeter, 10 rings: exact
            // distance -> max/min ratio ≤ 1.13 per ring; with the blur -> up to 3.4 (48 to 163 m
            // for the first ring, which cut across the lobe instead of entering it).
            ConcentricGenerator.Generate(perimeter, 10, 3, diagnostics: _diag); // different settings; _diag now holds this run's rings
            foreach (List<List<float2>> ring in _diag.Rings)
            {
                var ringDepths = ring.SelectMany(loop => loop).Select(p => DistanceToPolygon(p, perimeter)).ToList();
                Assert.True(ringDepths.Max() < 1.25f * ringDepths.Min(),
                    $"Distorted ring: {ringDepths.Min():F0} to {ringDepths.Max():F0} m from the perimeter");
            }
        }

        /// <summary>Copy of the game's curve fitting: the curve the game actually places for an IsArc segment.</summary>
        internal static float2[] GameCurve(RoadSegmentDef s, int samples = 24)
        {
            float2 a = s.Start.xz, dpos = s.End.xz;
            float2 p0 = a, p1, p2, p3 = dpos;
            if (!s.IsArc)
            {
                p1 = math.lerp(a, dpos, 1f / 3f); p2 = math.lerp(a, dpos, 2f / 3f);
            }
            else
            {
                float2 st = math.normalizesafe(s.StartTangent.xz), et = math.normalizesafe(s.EndTangent.xz);
                float num = math.distance(a, dpos);
                // Intersection of a + x*st and dpos - y*et.
                float2 r = st, q = -et; float den = r.x * q.y - r.y * q.x;
                float2 tt;
                if (math.abs(den) < 1e-6f) tt = new float2(num * 0.75f);
                else
                {
                    float2 w = dpos - a;
                    float x = (w.x * q.y - w.y * q.x) / den;
                    float y = (r.x * w.y - r.y * w.x) / den;
                    tt = math.clamp(new float2(x, y), num * 0.01f, num);
                }
                float num2 = math.dot(st, et);
                if (num2 > 0f) tt = math.lerp(tt, new float2(num / math.sqrt(2f * num2 + 2f)), math.min(1f, num2 * num2));
                else if (num2 < 0f) tt = math.lerp(tt, new float2(num * 1.2071068f), math.min(1f, num2 * num2));
                float l1 = tt.x, l2 = tt.y;
                float2 f5 = st, f6 = -et;
                float num3 = math.acos(math.saturate(-math.dot(f5, f6)));
                float num4 = math.tan(num3 / 2f);
                float num5 = (l1 + l2) / 6f;
                num5 = num4 >= 0.0001f ? num5 * (4f * math.tan(num3 / 4f) / num4) : num5 * 2f;
                p1 = a + f5 * math.min(l1, num5);
                p2 = dpos + f6 * math.min(l2, num5);
            }
            var pts = new float2[samples + 1];
            for (int i = 0; i <= samples; i++)
            {
                float u = (float)i / samples, v = 1f - u;
                pts[i] = v * v * v * p0 + 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u * p3;
            }
            return pts;
        }

        [Fact]
        public void RealPerimeter_JunctionsHaveOpenAnglesAndNoStubs()
        {
            // User feedback (in-game screenshot, "Overlapping objects" on the lobe): spokes
            // followed the lobe's axis and entered each ring's fold at ~44° (road edges overlapping
            // at the junction), and some connections landed 3-15 m from a neighbouring vertex (tiny
            // segment stuck to the junction). Angles measured on the curve the game actually places
            // (copy of the game's curve fitting, see GameCurve).
            string path = System.IO.Path.Combine(RoadsTestUtil.FixtureDir(), "real-perimeter-backtrack.txt");
            var perimeter = System.IO.File.ReadAllLines(path).Where(l => l.Trim().Length > 0).Select(l => l.Trim().Split(' '))
                .Select(q => new float3(float.Parse(q[0], System.Globalization.CultureInfo.InvariantCulture), 0f,
                    float.Parse(q[1], System.Globalization.CultureInfo.InvariantCulture))).ToList();
            const float minJunctionAngle = 40f;
            // Width of a two-lane road: any shorter and the junction swallows the whole segment.
            const float minStubLength = 16f;
            foreach ((int layers, int connections) in new[] { (10, 2), (6, 4), (3, 2) })
            {
                var segs = ConcentricGenerator.Generate(perimeter, layers, connections, diagnostics: _diag);
                AssertWellFormed(segs, perimeter);
                // Leaving direction of each segment from each of its endpoints.
                var leaving = new Dictionary<(float, float), List<float2>>();
                foreach (RoadSegmentDef s in segs)
                {
                    float2[] c = GameCurve(s);
                    var start = (s.Start.x, s.Start.z);
                    var end = (s.End.x, s.End.z);
                    if (!leaving.ContainsKey(start)) leaving[start] = new List<float2>();
                    if (!leaving.ContainsKey(end)) leaving[end] = new List<float2>();
                    leaving[start].Add(math.normalize(c[2] - c[0]));
                    leaving[end].Add(math.normalize(c[c.Length - 3] - c[c.Length - 1]));
                }
                foreach (var node in leaving)
                {
                    List<float2> dirs = node.Value;
                    for (int i = 0; i < dirs.Count; i++)
                    {
                        for (int j = i + 1; j < dirs.Count; j++)
                        {
                            float angle = math.degrees(math.acos(math.clamp(math.dot(dirs[i], dirs[j]), -1f, 1f)));
                            Assert.True(angle >= minJunctionAngle,
                                $"{layers} rings, {connections} spokes: two roads at {angle:F0}° at node ({node.Key.Item1:F0}, {node.Key.Item2:F0})");
                        }
                    }
                }
                foreach (RoadSegmentDef s in segs)
                {
                    bool atJunction = leaving[(s.Start.x, s.Start.z)].Count >= 3 || leaving[(s.End.x, s.End.z)].Count >= 3;
                    float length = math.distance(s.Start.xz, s.End.xz);
                    Assert.True(!atJunction || length >= minStubLength || DistanceToPolygon(s.Start.xz, perimeter) < 0.5f || DistanceToPolygon(s.End.xz, perimeter) < 0.5f,
                        $"{layers} rings, {connections} spokes: {length:F1} m segment stuck to a junction ({s.Start.xz} -> {s.End.xz})");
                }
            }
        }

        /// <summary>
        /// For every junction (node with 3 or more roads): smallest angle between two roads leaving
        /// it, measured from the node position to the point `reach` metres along each road
        /// (following segments past 2-road nodes), for each `reach` in the list. Returns (min angle,
        /// node, shortest segment touching a junction, shortest road between a junction and the
        /// next junction or end — a segment split by a mere shape node is not shorter for the game).
        /// </summary>
        internal static (float minAngle, float2 at, float shortestStub, float shortestRoad) JunctionDivergence(List<RoadSegmentDef> segs, float[] reaches)
        {
            var curves = segs.Select(s => GameCurve(s, 48)).ToList();
            var byNode = new Dictionary<(float, float), List<int>>();
            for (int i = 0; i < segs.Count; i++)
            {
                foreach (var q in new[] { (segs[i].Start.x, segs[i].Start.z), (segs[i].End.x, segs[i].End.z) })
                {
                    if (!byNode.ContainsKey(q)) byNode[q] = new List<int>();
                    byNode[q].Add(i);
                }
            }
            // Polyline of a road leaving `node` through segment `first`, extended through 2-road nodes.
            List<float2> Walk((float, float) node, int first, float length)
            {
                var pts = new List<float2>();
                int seg = first;
                var from = node;
                float walked = 0f;
                for (int guard = 0; guard < 50 && walked < length; guard++)
                {
                    float2[] c = curves[seg];
                    bool forward = segs[seg].Start.x == from.Item1 && segs[seg].Start.z == from.Item2;
                    IEnumerable<float2> ordered = forward ? c : c.Reverse();
                    foreach (float2 q in ordered)
                    {
                        if (pts.Count > 0) walked += math.distance(pts[pts.Count - 1], q);
                        pts.Add(q);
                    }
                    var to = forward ? (segs[seg].End.x, segs[seg].End.z) : (segs[seg].Start.x, segs[seg].Start.z);
                    if (byNode[to].Count != 2) break;
                    int next = byNode[to][0] == seg ? byNode[to][1] : byNode[to][0];
                    from = to;
                    seg = next;
                }
                return pts;
            }
            float2 PointAt(List<float2> pts, float reach)
            {
                float walked = 0f;
                for (int i = 1; i < pts.Count; i++)
                {
                    float e = math.distance(pts[i - 1], pts[i]);
                    if (walked + e >= reach) return math.lerp(pts[i - 1], pts[i], (reach - walked) / math.max(e, 1e-4f));
                    walked += e;
                }
                return pts[pts.Count - 1];
            }
            float min = 180f;
            float2 at = default;
            float shortest = float.MaxValue;
            float shortestRoad = float.MaxValue;
            foreach (var kv in byNode.Where(kv => kv.Value.Count >= 3))
            {
                float2 n = new float2(kv.Key.Item1, kv.Key.Item2);
                var roads = kv.Value.Select(i => Walk(kv.Key, i, reaches.Max() + 5f)).ToList();
                // Road length up to the next junction (or end), through 2-road nodes:
                // a segment split in two by a mere shape node is not shorter for the game.
                foreach (int i in kv.Value)
                {
                    shortest = math.min(shortest, math.distance(segs[i].Start.xz, segs[i].End.xz));
                    List<float2> road = Walk(kv.Key, i, 1e6f);
                    float roadLength = 0f;
                    for (int q = 1; q < road.Count; q++) roadLength += math.distance(road[q - 1], road[q]);
                    shortestRoad = math.min(shortestRoad, roadLength);
                }
                foreach (float reach in reaches)
                {
                    var dirs = roads.Select(r => math.normalizesafe(PointAt(r, reach) - n)).ToList();
                    for (int a = 0; a < dirs.Count; a++)
                    for (int b = a + 1; b < dirs.Count; b++)
                    {
                        float ang = math.degrees(math.acos(math.clamp(math.dot(dirs[a], dirs[b]), -1f, 1f)));
                        if (ang < min) { min = ang; at = n; }
                    }
                }
            }
            return (min, at, shortest, shortestRoad);
        }

        [Fact]
        public void RealPerimeter_RoadsDivergeFromEachJunction()
        {
            // User feedback ([Diag colisão] log, 10 rings, 6 spokes, game rejected three
            // junctions): a spoke left the ring at 54°, but the ring curved back towards it
            // (33° at 40 m), and a 16 m ring segment stayed stuck to the junction. Measured on the
            // curves the game places (see GameCurve), up to 50 m from each junction.
            string path = System.IO.Path.Combine(RoadsTestUtil.FixtureDir(), "real-perimeter-backtrack.txt");
            var perimeter = System.IO.File.ReadAllLines(path).Where(l => l.Trim().Length > 0).Select(l => l.Trim().Split(' '))
                .Select(q => new float3(float.Parse(q[0], System.Globalization.CultureInfo.InvariantCulture), 0f,
                    float.Parse(q[1], System.Globalization.CultureInfo.InvariantCulture))).ToList();
            foreach ((int layers, int connections) in new[] { (10, 6), (10, 2), (8, 3), (6, 4), (4, 6), (3, 2) })
            {
                var r = JunctionDivergence(ConcentricGenerator.Generate(perimeter, layers, connections, diagnostics: _diag), new[] { 10f, 20f, 35f, 50f });
                Assert.True(r.minAngle >= 40f,
                    $"{layers} rings, {connections} spokes: two roads at {r.minAngle:F0}° near the junction ({r.at.x:F0}, {r.at.y:F0})");
                Assert.True(r.shortestStub >= 30f,
                    $"{layers} rings, {connections} spokes: {r.shortestStub:F1} m segment stuck to a junction");
            }
        }

        [Fact]
        public void RealPerimeter_ConnectionsAreContinuous()
        {
            // User feedback (in-game screenshot): "the road is not continuous" — a blocked spoke
            // restarted from another point of the ring and the connection became a staircase of
            // offset segments. A chain never restarts from a point other than where it arrived:
            // on an intermediate ring, a node with a single spoke is at most the END of a chain
            // (spoke coming from outside), never a start without an arrival. And up to 8 spokes,
            // no chain stops before the centre.
            string path = System.IO.Path.Combine(RoadsTestUtil.FixtureDir(), "real-perimeter-backtrack.txt");
            var perimeter = System.IO.File.ReadAllLines(path).Where(l => l.Trim().Length > 0).Select(l => l.Trim().Split(' '))
                .Select(q => new float3(float.Parse(q[0], System.Globalization.CultureInfo.InvariantCulture), 0f,
                    float.Parse(q[1], System.Globalization.CultureInfo.InvariantCulture))).ToList();
            foreach ((int layers, int connections) in new[] { (10, 6), (10, 2), (10, 4), (10, 12), (8, 3), (6, 4), (6, 8), (4, 6), (3, 2) })
            {
                var segments = ConcentricGenerator.Generate(perimeter, layers, connections, diagnostics: _diag);
                var rings = _diag.Rings;
                var levelOf = new Dictionary<(float, float), int>();
                for (int k = 0; k < rings.Count; k++)
                {
                    foreach (float2 q in rings[k].SelectMany(loop => loop))
                    {
                        levelOf[(q.x, q.y)] = k + 1;
                    }
                }
                int Level(float3 q) => DistanceToPolygon(q.xz, perimeter) < 0.5f ? 0 : levelOf.TryGetValue((q.x, q.z), out int l) ? l : -1;
                var spokes = segments.Where(s => Level(s.Start) != Level(s.End)).ToList();
                var spokesAt = new Dictionary<(float, float), List<RoadSegmentDef>>();
                foreach (RoadSegmentDef s in spokes)
                {
                    foreach (float3 q in new[] { s.Start, s.End })
                    {
                        var key = (q.x, q.z);
                        if (!spokesAt.ContainsKey(key)) spokesAt[key] = new List<RoadSegmentDef>();
                        spokesAt[key].Add(s);
                    }
                }
                foreach (var node in spokesAt.Where(n => n.Value.Count == 1))
                {
                    int level = levelOf.TryGetValue(node.Key, out int l) ? l : 0;
                    if (level <= 0 || level >= rings.Count)
                    {
                        continue;
                    }
                    RoadSegmentDef only = node.Value[0];
                    float3 other = only.Start.x == node.Key.Item1 && only.Start.z == node.Key.Item2 ? only.End : only.Start;
                    Assert.True(Level(other) < level,
                        $"{layers} rings, {connections} spokes: connection restarts elsewhere at ring {level} at ({node.Key.Item1:F0}, {node.Key.Item2:F0})");
                    // Up to 8 spokes, all reach the centre; beyond that, a spoke may stop a little short
                    // for lack of room in a narrow part (seen: 12 spokes, ring 9/10).
                    Assert.True(connections > 8,
                        $"{layers} rings, {connections} spokes: connection stopped at ring {level} at ({node.Key.Item1:F0}, {node.Key.Item2:F0})");
                }
            }
        }

        [Fact]
        public void RealPerimeter_ConnectionsReachTheInnermostRing()
        {
            // User feedback (in-game screenshot, 10 rings): "the connection doesn't go to the centre".
            // The lobe's spoke stopped at the first ring fold (closed angle); it must now go around
            // it and reach the innermost ring, for every spoke.
            string path = System.IO.Path.Combine(RoadsTestUtil.FixtureDir(), "real-perimeter-backtrack.txt");
            var perimeter = System.IO.File.ReadAllLines(path).Where(l => l.Trim().Length > 0).Select(l => l.Trim().Split(' '))
                .Select(q => new float3(float.Parse(q[0], System.Globalization.CultureInfo.InvariantCulture), 0f,
                    float.Parse(q[1], System.Globalization.CultureInfo.InvariantCulture))).ToList();
            foreach ((int layers, int connections) in new[] { (10, 2), (10, 4), (6, 3), (4, 2), (3, 2), (3, 4) })
            {
                var segments = ConcentricGenerator.Generate(perimeter, layers, connections, diagnostics: _diag);
                var innermost = new HashSet<(float, float)>(_diag.Rings[_diag.Rings.Count - 1]
                    .SelectMany(loop => loop).Select(q => (q.x, q.y)));
                int spokesIntoCentre = segments.Count(s => !s.IsArc
                    && (innermost.Contains((s.Start.x, s.Start.z)) ^ innermost.Contains((s.End.x, s.End.z))));
                // Two spokes may merge on the way (at an open angle) and end as a single road:
                // at least one spoke enters the centre, and none stops on the way
                // (see RealPerimeter_ConnectionsAreContinuous).
                Assert.True(spokesIntoCentre >= 1,
                    $"{layers} rings, {connections} spokes: no spoke reaches the innermost ring");
            }
        }

        private static List<float3> LoadRealPerimeter()
        {
            string path = System.IO.Path.Combine(RoadsTestUtil.FixtureDir(), "real-perimeter-backtrack.txt");
            return System.IO.File.ReadAllLines(path).Where(l => l.Trim().Length > 0).Select(l => l.Trim().Split(' '))
                .Select(q => new float3(float.Parse(q[0], System.Globalization.CultureInfo.InvariantCulture), 0f,
                    float.Parse(q[1], System.Globalization.CultureInfo.InvariantCulture))).ToList();
        }

        [Fact]
        public void Radial_Circle_RoundaboutAndStraightAvenuesToThePerimeter()
        {
            // Radial pattern (user feedback: "only the avenues and not the layers, and in the centre
            // a roundabout, not an asset but a circle").
            var perimeter = Circle(600f, 400);
            var segments = ConcentricGenerator.GenerateRadial(perimeter, avenues: 6, roundaboutRadius: 60f, diagnostics: _radial);
            AssertWellFormed(segments, perimeter);
            float2 centre = _radial.Centre;
            float radius = _radial.Radius;
            Assert.True(math.length(centre) < 10f, $"Roundabout off-centre: {centre}");
            Assert.True(math.abs(radius - 60f) < 0.5f, $"Radius {radius:F1} instead of 60");
            // Road circle: non-avenue segments, all ~60 m from the centre.
            var ring = segments.Where(s => !s.IsAvenue).ToList();
            Assert.NotEmpty(ring);
            Assert.All(ring, s => Assert.True(math.abs(math.distance(s.Start.xz, centre) - radius) < 1f));
            // 6 avenues, each from the roundabout to the perimeter, aligned with the centre.
            var avenues = segments.Where(s => s.IsAvenue).ToList();
            Assert.Equal(6, avenues.Count(s => DistanceToPolygon(s.End.xz, perimeter) < 0.5f));
            Assert.Equal(6, avenues.Count(s => math.abs(math.distance(s.Start.xz, centre) - radius) < 1f));
            foreach (RoadSegmentDef s in avenues)
            {
                float2 u = s.Start.xz - centre, v = s.End.xz - centre;
                float offCentre = math.abs(u.x * v.y - u.y * v.x) / math.max(math.distance(u, v), 1e-3f);
                Assert.True(offCentre < 1f, $"Avenue not straight from the centre ({offCentre:F1} m)");
            }
        }

        [Fact]
        public void Radial_TooManyAvenuesForTheRadius_RoundaboutGrowsToKeepJunctionsApart()
        {
            // 16 avenues on a 25 m roundabout: connections 10 m apart. The radius is enlarged
            // to keep at least MinAvenueJointSpacing between neighbouring connections.
            var perimeter = Circle(700f, 400);
            var segments = ConcentricGenerator.GenerateRadial(perimeter, avenues: 16, roundaboutRadius: 25f, diagnostics: _radial);
            AssertWellFormed(segments, perimeter);
            var joints = segments.Where(s => s.IsAvenue && math.abs(math.distance(s.Start.xz, _radial.Centre) - _radial.Radius) < 1f)
                .Select(s => s.Start.xz).ToList();
            Assert.Equal(16, joints.Count);
            float closest = joints.SelectMany((p, i) => joints.Skip(i + 1).Select(q => math.distance(p, q))).Min();
            Assert.True(closest >= 38f, $"Connections {closest:F1} m apart on the roundabout");
        }

        [Fact]
        public void Radial_SmallShape_FewerAvenuesNoCrash()
        {
            var perimeter = Square(220f);
            var segments = ConcentricGenerator.GenerateRadial(perimeter, avenues: 16, roundaboutRadius: 150f, diagnostics: _radial);
            if (segments.Count > 0)
            {
                AssertWellFormed(segments, perimeter);
                Assert.True(_radial.Radius <= 110f - 40f + 1f, $"{_radial.Radius:F0} m roundabout in a 220 m square");
            }
        }

        [Fact]
        public void Radial_AvenuesAreEquallySpacedAroundTheRoundabout()
        {
            // User feedback: "the lines have to be the same distance from each other when they
            // reach the roundabout" — each avenue rotated on its own to find a good angle on the
            // perimeter. The connection angles must all be multiples of 2π/n (relative to one of
            // them), including on the irregular shape.
            foreach ((List<float3> perimeter, int n) in new[] { (LoadRealPerimeter(), 8), (LoadRealPerimeter(), 5), (LoadRealPerimeter(), 12), (Circle(600f, 400), 7) })
            {
                var segments = ConcentricGenerator.GenerateRadial(perimeter, n, 50f, diagnostics: _radial);
                float2 centre = _radial.Centre;
                float radius = _radial.Radius;
                var angles = segments.Where(s => s.IsAvenue && math.abs(math.distance(s.Start.xz, centre) - radius) < 1f)
                    .Select(s => math.atan2(s.Start.z - centre.y, s.Start.x - centre.x)).ToList();
                Assert.True(angles.Count >= 2);
                float gap = 2f * math.PI / n;
                foreach (float angle in angles)
                {
                    float steps = (angle - angles[0]) / gap;
                    float offBy = math.abs(steps - math.round(steps)) * gap * radius;
                    Assert.True(offBy < 1.5f, $"{n} avenues: connection offset by {offBy:F1} m on the roundabout");
                }
            }
        }

        [Fact]
        public void Radial_Layers_AreCirclesAroundTheRoundabout()
        {
            // User feedback: "add the layers back, but following the roundabout and not the
            // outer road" — circular rings concentric with the roundabout, evenly spaced, wholly
            // inside the shape, crossed at right angles by every avenue.
            var perimeter = Circle(600f, 400);
            var segments = ConcentricGenerator.GenerateRadial(perimeter, avenues: 8, roundaboutRadius: 50f, layers: 3, diagnostics: _radial);
            AssertWellFormed(segments, perimeter);
            float2 centre = _radial.Centre;
            float[] rings = _radial.RingRadii;
            Assert.Equal(3, rings.Length);
            float spacing = rings[0] - _radial.Radius;
            Assert.True(spacing >= ConcentricGenerator.MinLayerSpacing, $"Rings {spacing:F0} m apart");
            for (int k = 1; k < rings.Length; k++)
            {
                Assert.True(math.abs(rings[k] - rings[k - 1] - spacing) < 0.5f, "Rings not evenly spaced");
            }
            Assert.True(rings[rings.Length - 1] <= 600f - 40f + 5f, $"Last ring {rings[rings.Length - 1]:F0} m from the centre");
            // Each ring: a circle (non-avenue segments at its radius), crossed by the 8 avenues.
            foreach (float r in rings)
            {
                Assert.Contains(segments, s => !s.IsAvenue && math.abs(math.distance(s.Start.xz, centre) - r) < 1f);
                int crossings = segments.Count(s => s.IsAvenue && math.abs(math.distance(s.End.xz, centre) - r) < 1f);
                Assert.Equal(8, crossings);
            }
        }

        [Fact]
        public void Radial_Layers_FillTheWholeShape_OuterRingsAsArcsToThePerimeter()
        {
            // User feedback: "the layers have to fill the whole perimeter even if only half a
            // circle remains". On the real (elongated) shape, the rings reach the farthest point:
            // the largest ones leave the shape and keep only arcs that join the perimeter road.
            var perimeter = LoadRealPerimeter();
            int max = ConcentricGenerator.RadialMaxLayers(perimeter, 8, 50f);
            Assert.True(max >= 5, $"Only {max} rings possible on the real perimeter");
            var segments = ConcentricGenerator.GenerateRadial(perimeter, 8, 50f, layers: 99, diagnostics: _radial);
            Assert.Equal(max, _radial.RingRadii.Length);
            AssertWellFormed(segments, perimeter);
            float2 centre = _radial.Centre;
            float outer = _radial.RingRadii.Last();
            // A (non-avenue) ring arc that touches the perimeter, on a ring larger than the
            // largest inscribed circle.
            Assert.Contains(segments, s => !s.IsAvenue
                && (DistanceToPolygon(s.Start.xz, perimeter) < 0.5f || DistanceToPolygon(s.End.xz, perimeter) < 0.5f));
            float inscribed = perimeter.Select(q => math.distance(q.xz, centre)).Min();
            Assert.True(outer > inscribed + 100f, $"Last ring at {outer:F0} m, inscribed circle {inscribed:F0} m");
        }

        [Fact]
        public void Radial_RealPerimeter_RoadsKeepClearOfThePerimeterRoad()
        {
            // User feedback ([Diag colisão] log: 12 avenues, 150 m roundabout, 10 rings): a ring
            // passed 12-14 m from the perimeter road and an avenue stopped on it; the game snapped
            // that node to the existing road (8 m segment) and the roads overlapped.
            var perimeter = LoadRealPerimeter();
            foreach ((int avenues, float radius, int layers) in new[] { (12, 150f, 10), (8, 50f, 10), (6, 100f, 6), (16, 30f, 10) })
            {
                var segments = ConcentricGenerator.GenerateRadial(perimeter, avenues, radius, layers, diagnostics: _radial);
                AssertWellFormed(segments, perimeter);
                // No generated node in the band where the game would snap it to the perimeter.
                foreach (float3 q in segments.SelectMany(s => new[] { s.Start, s.End }))
                {
                    float d = DistanceToPolygon(q.xz, perimeter);
                    Assert.True(d < 0.5f || d >= 24f, $"{avenues}/{radius}/{layers}: node {d:F1} m from the perimeter at {q.xz}");
                }
                // No grazing road: outside the approach zone of its arrival on the perimeter
                // (25 m / sin θ, θ being the crossing angle), a generated road stays at least 24 m
                // from it (real curves, see GameCurve). Game rules: no minimum angle, but the
                // segment must contain the node cutback, otherwise the game reports an invalid shape.
                foreach (RoadSegmentDef s in segments)
                {
                    float2[] curve = GameCurve(s, 48);
                    var ends = new List<(float2 at, float reach)>();
                    foreach ((float2 e, float2 next) in new[] { (curve[0], curve[1]), (curve[curve.Length - 1], curve[curve.Length - 2]) })
                    {
                        if (DistanceToPolygon(e, perimeter) >= 0.5f) continue;
                        float2 edge = PerimeterEdgeDirection(e, perimeter);
                        float2 dir = math.normalize(next - e);
                        float sin = math.abs(dir.x * edge.y - dir.y * edge.x);
                        float cos = math.abs(math.dot(dir, edge));
                        Assert.True(sin >= math.sin(math.radians(14.5f)), $"{avenues}/{radius}/{layers}: perimeter connection at {math.degrees(math.asin(sin)):F0}° at {e}");
                        float cutback = 12.5f * (1f + cos) / sin;
                        float length = CurveLength(curve);
                        Assert.True(length >= cutback, $"{avenues}/{radius}/{layers}: {length:F0} m segment shorter than the node cutback ({cutback:F0} m) at {e}");
                        ends.Add((e, 25f / sin + 8f));
                    }
                    foreach (float2 q in curve)
                    {
                        if (ends.Exists(e => math.distance(e.at, q) < e.reach)) continue;
                        float d = DistanceToPolygon(q, perimeter);
                        Assert.True(d >= 24f, $"{avenues}/{radius}/{layers}: road {d:F1} m from the perimeter at ({q.x:F0}, {q.y:F0})");
                    }
                }
            }
        }

        [Fact]
        public void Radial_RealPerimeter_LayersReachThePerimeterAtShallowAngles()
        {
            // User feedback: rings nearly parallel to the perimeter stopped at the neighbouring
            // avenue (connection rejected below 40°), leaving empty corners. The game imposes no
            // minimum angle (only the node cutback must fit in the segment, see
            // Radial_RealPerimeter_RoadsKeepClearOfThePerimeterRoad): these connections are kept.
            var perimeter = LoadRealPerimeter();
            int shallow = 0;
            foreach ((int avenues, float radius, int layers) in new[] { (12, 150f, 10), (8, 50f, 10), (6, 100f, 6) })
            {
                foreach (RoadSegmentDef s in ConcentricGenerator.GenerateRadial(perimeter, avenues, radius, layers, diagnostics: _radial))
                {
                    float2[] curve = GameCurve(s, 48);
                    foreach ((float2 e, float2 next) in new[] { (curve[0], curve[1]), (curve[curve.Length - 1], curve[curve.Length - 2]) })
                    {
                        if (DistanceToPolygon(e, perimeter) >= 0.5f) continue;
                        float2 edge = PerimeterEdgeDirection(e, perimeter);
                        float2 dir = math.normalize(next - e);
                        if (math.abs(dir.x * edge.y - dir.y * edge.x) < math.sin(math.radians(40f))) shallow++;
                    }
                }
            }
            Assert.True(shallow >= 3, $"Only {shallow} ring connections to the perimeter below 40°");
        }

        [Fact]
        public void Radial_RealPerimeter_LayersLeaveNoEmptyWedges()
        {
            // User feedback ("there are still layers that don't go all the way"): where the ring
            // runs nearly parallel to the perimeter, it stopped at the last avenue and left an empty
            // corner between the avenue and the perimeter. It now carries on, then turns towards the
            // perimeter. Metric: interior area (excluding the roundabout) more than 70 m from any road.
            var perimeter = LoadRealPerimeter();
            var segments = ConcentricGenerator.GenerateRadial(perimeter, 12, 150f, 10, diagnostics: _radial);
            float2 centre = _radial.Centre;
            float roundabout = _radial.Radius;
            var roads = segments.Select(s => GameCurve(s, 16)).ToList();
            roads.Add(perimeter.Select(q => q.xz).Concat(new[] { perimeter[0].xz }).ToArray());
            float minX = perimeter.Min(q => q.x), maxX = perimeter.Max(q => q.x);
            float minZ = perimeter.Min(q => q.z), maxZ = perimeter.Max(q => q.z);
            const float cell = 16f;
            int empty = 0;
            for (float x = minX; x <= maxX; x += cell)
            {
                for (float z = minZ; z <= maxZ; z += cell)
                {
                    var p = new float2(x, z);
                    if (math.distance(p, centre) < roundabout || !PointInPolygon(p, perimeter)) continue;
                    bool near = false;
                    foreach (float2[] road in roads)
                    {
                        for (int i = 0; i + 1 < road.Length && !near; i++)
                        {
                            float2 ab = road[i + 1] - road[i];
                            float t = math.lengthsq(ab) < 1e-6f ? 0f : math.saturate(math.dot(p - road[i], ab) / math.lengthsq(ab));
                            near = math.distancesq(p, road[i] + t * ab) < 70f * 70f;
                        }
                        if (near) break;
                    }
                    if (!near) empty++;
                }
            }
            // Before: ~84,000 m² empty (4 corners); after: ~30,000 m² (1 corner, arrival too closed against the avenue).
            Assert.True(empty * cell * cell < 50000f, $"{empty * cell * cell:F0} m² more than 70 m from any road");
        }

        [Fact]
        public void Radial_RealPerimeter_GeneratesQuickly()
        {
            // Regenerated on every panel setting change: must stay fast even at the maximum.
            var perimeter = LoadRealPerimeter();
            ConcentricGenerator.GenerateRadial(perimeter, 8, 50f, 2, diagnostics: _radial);
            // Citymaker: generators no longer keep static result/distance-field caches (thread safety);
            // re-plans of an unchanged input are cached by the preview worker instead. Budgets are per
            // uncached generation, Debug build, with headroom for the parallel test run.
            var stopwatch = Stopwatch.StartNew();
            ConcentricGenerator.GenerateRadial(perimeter, 16, 150f, 10, diagnostics: _radial);
            ConcentricGenerator.GenerateRadial(perimeter, 12, 50f, 10, diagnostics: _radial);
            stopwatch.Stop();
            Assert.True(stopwatch.ElapsedMilliseconds < TimingBudget.Ms(1800), $"Two Radial generations in {stopwatch.ElapsedMilliseconds} ms");
        }

        [Fact]
        public void Radial_RealPerimeter_WellFormedWithOpenJunctions()
        {
            var perimeter = LoadRealPerimeter();
            foreach ((int avenues, float radius, int layers) in new[] { (8, 40f, 0), (3, 25f, 0), (12, 60f, 0), (16, 150f, 0), (6, 100f, 0), (8, 50f, 3), (12, 40f, 10), (5, 80f, 2) })
            {
                var segments = ConcentricGenerator.GenerateRadial(perimeter, avenues, radius, layers, diagnostics: _radial);
                AssertWellFormed(segments, perimeter);
                Assert.True(segments.Count(s => s.IsAvenue && DistanceToPolygon(s.End.xz, perimeter) < 0.5f) >= 2,
                    $"Radial {avenues}/{radius}/{layers}: fewer than 2 avenues reach the perimeter");
                var r = JunctionDivergence(segments, new[] { 10f, 20f, 35f, 50f });
                Assert.True(r.minAngle >= 40f, $"Radial {avenues}/{radius}/{layers}: two roads at {r.minAngle:F0}° near the junction ({r.at.x:F0}, {r.at.y:F0})");
                Assert.True(r.shortestRoad >= 30f, $"Radial {avenues}/{radius}/{layers}: {r.shortestRoad:F1} m road between two junctions");
            }
        }

        [Fact]
        public void Connections_AreSpreadAroundThePerimeter_NotBunchedInTheMiddle()
        {
            // User feedback (screenshot): on an elongated shape, the spokes started from the small
            // inner ring and all arrived bunched in the middle of the perimeter.
            var perimeter = Ellipse(600f, 220f, 400);
            var segments = ConcentricGenerator.Generate(perimeter, layers: 3, connections: 8, diagnostics: _diag);
            AssertWellFormed(segments, perimeter);
            var onPerimeter = segments
                .SelectMany(s => new[] { s.Start, s.End })
                .Where(p => DistanceToPolygon(p.xz, perimeter) < 0.5f)
                .ToList();
            Assert.True(onPerimeter.Count >= 6, $"Only {onPerimeter.Count} spokes reach the perimeter");
            Assert.Contains(onPerimeter, p => p.x > 360f);
            Assert.Contains(onPerimeter, p => p.x < -360f);
        }

        [Fact]
        public void SharpCorneredShape_InnerRingsKeepSharpCorners()
        {
            // A square must stay a square (sharp corners inherited from the perimeter), not become rounded.
            var perimeter = Square(600f);
            var segments = ConcentricGenerator.Generate(perimeter, layers: 2, connections: 4, diagnostics: _diag);
            Assert.True(MaxRingTurnDegrees(segments, perimeter) > 70f, "The square's corners were rounded.");
        }

        [Fact]
        public void ChangingLayersOnTheSamePerimeter_StaysInteractive()
        {
            // User feedback: "slowness when trying to change parameters" — the distance field
            // (the expensive part) depends only on the perimeter.
            var perimeter = Circle(520f, 500);
            ConcentricGenerator.Generate(perimeter, layers: 2, connections: 4, diagnostics: _diag);
            // Citymaker: generators no longer keep static result/distance-field caches (thread safety);
            // re-plans of an unchanged input are cached by the preview worker instead. Budgets are per
            // uncached generation, Debug build, with headroom for the parallel test run.
            var stopwatch = Stopwatch.StartNew();
            for (int layers = 3; layers <= 6; layers++)
            {
                ConcentricGenerator.Generate(perimeter, layers, connections: 5, diagnostics: _diag);
            }
            stopwatch.Stop();
            Assert.True(stopwatch.ElapsedMilliseconds < TimingBudget.Ms(2800), $"4 setting changes in {stopwatch.ElapsedMilliseconds} ms");
        }

        [Fact]
        public void ThroughGenerateLoopGrid_ConcentricModeTakesOver()
        {
            var parameters = new GridParameters { CollectorSpacingMeters = 300f, ConcentricMode = true, ConcentricLayers = 2, ConcentricConnections = 4 };
            var segments = GridGenerator.GenerateLoopGrid(Square(600f), parameters);
            Assert.NotEmpty(segments);
            Assert.DoesNotContain(segments, s => s.IsAvenue || s.IsPedestrian || s.IsCulDeSacEnd);
        }

        [Fact]
        public void Radial_MarksOnlyTheRoundaboutRing()
        {
            var perimeter = new List<float3> { new float3(0, 0, 0), new float3(800, 0, 0), new float3(800, 0, 700), new float3(0, 0, 700) };
            var segments = ConcentricGenerator.GenerateRadial(perimeter, 6, 40f, 2, diagnostics: _radial);
            var ring = segments.Where(s => s.IsRoundabout).ToList();
            Assert.NotEmpty(ring);
            float2 centre = ring.Aggregate(float2.zero, (sum, s) => sum + s.Start.xz) / ring.Count;
            foreach (RoadSegmentDef s in ring)
            {
                Assert.True(s.IsArc);
                Assert.False(s.IsAvenue);
                // Counter-clockwise: the start tangent turns left around the centre.
                float2 radial = s.Start.xz - centre, t = s.StartTangent.xz;
                Assert.True(radial.x * t.y - radial.y * t.x > 0f);
                Assert.InRange(math.distance(s.Start.xz, centre), 30f, 50f);
            }
            // The outer rings are not the roundabout.
            Assert.Contains(segments, s => s.IsArc && !s.IsRoundabout);
        }
    }
}
