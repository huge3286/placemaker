using System;
using System.Collections.Generic;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Layers.Roads;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using static Citymaker.Tests.RoadsTestUtil;

namespace Citymaker.Tests
{
    /// <summary>
    /// s61: with Corner rounding 0 the perimeter follows the outline exactly (sharp corners, one closed loop); obstacles are
    /// bypassed before pieces are dropped; "Follow contours" does not bend a grid on flat ground; every located issue names
    /// the setting behind it and shows in the legend.
    /// </summary>
    public class RoadsPerimeterTests
    {
        private static List<float3> Square(float side, float degrees, float cx = 1000f, float cz = 1000f)
        {
            float h = side / 2f;
            return Rotated(Poly(-h, -h, h, -h, h, h, -h, h), degrees).Select(p => new float3(p.x + cx, 0, p.z + cz)).ToList();
        }

        private static SettingsBag Bag(float corner = 0f, bool follow = true, string fit = "auto", bool avoid = true) => Settings(
            (RoadsIds.PerimeterRoad, "auto"), (RoadsIds.PerimeterCornerRadius, (double)corner), (RoadsIds.FollowTerrain, follow),
            (RoadsIds.TerrainFit, fit), (RoadsIds.AvoidObstacles, avoid), (RoadsIds.ConnectExisting, true));

        private static AreaPlan Plan(List<float3> ring, Func<float2, float> h, SettingsBag bag, SiteContext site = null)
        {
            site = site ?? new SiteContext();
            site.HeightAt = h;
            return Run(Input(ring, AreaSource.Drawn, bag, site));
        }

        private static (int, int) Key(float3 p) => ((int)math.round(p.x * 4), (int)math.round(p.z * 4));

        private static int OpenPerimeterEnds(AreaPlan plan)
        {
            var ends = new Dictionary<(int, int), int>();
            foreach (var n in plan.Nets)
                foreach (var p in new[] { n.A, n.D })
                    ends[Key(p)] = ends.TryGetValue(Key(p), out var v) ? v + 1 : 1;
            return plan.Nets.Where(n => n.Role == NetRole.PerimeterRoad).Sum(n => new[] { n.A, n.D }.Count(p => ends[Key(p)] < 2));
        }

        private static float DistSeg(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float t = math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.dot(ab, ab)));
            return math.distance(p, a + t * ab);
        }

        private static Obstacle Box(float cx, float cz, float w, float d, ObstacleKind kind = ObstacleKind.Building) => new Obstacle
        {
            A = new float2(cx - w / 2, cz - d / 2), B = new float2(cx + w / 2, cz - d / 2), C = new float2(cx + w / 2, cz + d / 2), D = new float2(cx - w / 2, cz + d / 2), Kind = kind,
        };

        /// <summary>The direction of each perimeter piece leaving the outline corner <paramref name="c"/>.</summary>
        private static List<float2> CornerDirections(List<PlannedNet> perim, float3 c)
        {
            var at = perim.Where(n => math.distance(n.A.xz, c.xz) < 0.6f || math.distance(n.D.xz, c.xz) < 0.6f).ToList();
            Assert.Equal(2, at.Count);
            return at.Select(n =>
            {
                var cv = new RoadCurve(n.A, n.B, n.C, n.D);
                return math.distance(n.A.xz, c.xz) < 0.6f ? cv.TangentXZ(0f) : -cv.TangentXZ(1f);
            }).ToList();
        }

        public static IEnumerable<object[]> Angles() => new[] { 0f, 12f, 28f, 45f, 63f }.Select(a => new object[] { a });

        [Theory, MemberData(nameof(Angles))]
        public void RoundingZeroKeepsTheOutlineExactlyAndClosed(float degrees)
        {
            var ring = Square(420f, degrees);
            var plan = Plan(ring, p => 40f + 0.004f * p.x, Bag());
            var perim = plan.Nets.Where(n => n.Role == NetRole.PerimeterRoad).ToList();
            Assert.NotEmpty(perim);
            Assert.Equal(0, OpenPerimeterEnds(plan));
            var ring2 = Ring2(ring.ToArray());
            // Every perimeter piece stays on the outline (a bulge or a cut corner would leave it).
            foreach (var n in perim)
                for (int i = 0; i <= 8; i++)
                {
                    var q = new RoadCurve(n.A, n.B, n.C, n.D).Position(i / 8f).xz;
                    float d = float.MaxValue;
                    for (int k = 0; k < ring2.Length; k++) d = math.min(d, DistSeg(q, ring2[k], ring2[(k + 1) % ring2.Length]));
                    Assert.True(d < 0.6f, $"perimeter {d:0.00} m off the outline at {q}");
                }
            // Corner angles match the outline's: at each corner two perimeter pieces meet at 90 degrees.
            foreach (var c in ring)
            {
                var dirs = CornerDirections(perim, c);
                float angle = math.degrees(math.acos(math.clamp(math.dot(dirs[0], dirs[1]), -1f, 1f)));
                Assert.InRange(angle, 87f, 93f);
            }
        }

        [Fact]
        public void ARhombusKeepsItsAcuteAndObtuseCorners()
        {
            // 60 / 120 degree diamond, tilted.
            var ring = Rotated(Poly(0, 0, 300, 0, 450, 260, 150, 260), 17f).Select(p => new float3(p.x + 900, 0, p.z + 900)).ToList();
            var plan = Plan(ring, p => 30f, Bag());
            Assert.Equal(0, OpenPerimeterEnds(plan));
            var perim = plan.Nets.Where(n => n.Role == NetRole.PerimeterRoad).ToList();
            var c2 = Ccw(ring);
            for (int i = 0; i < c2.Length; i++)
            {
                float3 c = c2[i];
                float2 d0 = math.normalize(c2[(i + c2.Length - 1) % c2.Length].xz - c.xz), d1 = math.normalize(c2[(i + 1) % c2.Length].xz - c.xz);
                float want = math.degrees(math.acos(math.dot(d0, d1)));
                var dirs = CornerDirections(perim, c);
                float got = math.degrees(math.acos(math.clamp(math.dot(dirs[0], dirs[1]), -1f, 1f)));
                Assert.InRange(got, want - 3f, want + 3f);
            }
        }

        [Fact]
        public void RoundingStillRoundsWhenAsked()
        {
            var ring = Square(420f, 20f);
            var plan = Plan(ring, p => 40f, Bag(corner: 40f));
            var perim = plan.Nets.Where(n => n.Role == NetRole.PerimeterRoad).ToList();
            foreach (var c in ring)
            {
                float nearest = perim.SelectMany(n => new[] { n.A, n.D }).Min(p => math.distance(p.xz, c.xz));
                Assert.True(nearest > 8f, $"corner kept sharp ({nearest:0.0} m) with rounding 40");
            }
            Assert.Equal(0, OpenPerimeterEnds(plan));
        }

        [Theory, MemberData(nameof(Angles))]
        public void FlatSquareSitesLoseNothing(float degrees)
        {
            var ring = Square(420f, degrees);
            var plan = Plan(ring, p => 40f, Bag());
            Assert.Equal(0, (int)plan.Facts[RoadsPlanner.FactRemoved]);
            Assert.Equal(0, (int)plan.Facts[RoadsPlanner.FactIslandsDropped]);
            Assert.DoesNotContain(plan.Issues, i => i.Dropped);
            Assert.Empty(plan.Issues.Where(i => i.At.HasValue));
            var audit = RoadsAudit.Measure(plan, _ => 40f, 0.1f, false, Ring2(ring.ToArray()));
            Assert.True(audit.Connected);
            Assert.Equal(0, OpenPerimeterEnds(plan));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.01f)]
        [InlineData(0.025f)]
        public void FollowContoursLeavesAGridStraightOnNearlyFlatGround(float tilt)
        {
            var ring = Square(420f, 13f);
            Func<float2, float> h = p => 40f + tilt * (0.8f * p.x + 0.6f * p.y) + 0.15f * math.sin(p.x / 40f);
            var followed = Plan(ring, h, Bag(fit: "follow"));
            var strict = Plan(ring, h, Bag(fit: "ignore"));
            Assert.Equal(strict.Nets.Count, followed.Nets.Count);
            foreach (var n in followed.Nets.Where(n => n.Role != NetRole.PerimeterRoad && n.Role != NetRole.Pathway && n.Role != NetRole.PathwayWide))
            {
                var cv = new RoadCurve(n.A, n.B, n.C, n.D);
                Assert.True(cv.IsStraight(0.3f), "a street bent on flat ground: " + n.A + " -> " + n.D);
            }
            var a = followed.Nets.Select(n => (Key(n.A), Key(n.D))).OrderBy(x => x).ToList();
            var b = strict.Nets.Select(n => (Key(n.A), Key(n.D))).OrderBy(x => x).ToList();
            Assert.Equal(b, a);
        }

        [Fact]
        public void ABuildingOnTheOutlineIsBypassedAndThePerimeterStaysClosed()
        {
            var ring = Square(420f, 0f);
            var site = new SiteContext();
            // A house right on the south edge, mid-way between two grid streets.
            site.Obstacles.Add(Box(1000f, 790f, 14f, 12f));
            var plan = Plan(ring, p => 40f, Bag(), site);
            Assert.Equal(0, OpenPerimeterEnds(plan));
            Assert.True(plan.Facts.ContainsKey(RoadsPlanner.FactBypassed) && plan.Facts[RoadsPlanner.FactBypassed] >= 1, "the blocked perimeter piece was bent round the house");
            Assert.DoesNotContain(plan.Issues, i => i.Key == RoadsPlanner.IssuePerimeterBlocked || i.Key == RoadsPlanner.IssuePerimeterOpen);
            // The bypass keeps clear of the house.
            var obs = new RoadObstacles(site, new float2(700, 700), new float2(1300, 1300));
            foreach (var n in plan.Nets) Assert.False(obs.Blocked(new RoadCurve(n.A, n.B, n.C, n.D), 8f), "a road crosses the house");
        }

        [Fact]
        public void APondAcrossTheOutlineClosesOrExplainsTheGap()
        {
            var ring = Square(420f, 20f);
            var site = new SiteContext();
            site.Obstacles.Add(Box(1000f, 790f, 130f, 40f, ObstacleKind.Water));
            var plan = Plan(ring, p => 40f, Bag(), site);
            int open = OpenPerimeterEnds(plan);
            if (open > 0) Assert.Contains(plan.Issues, i => i.Key == RoadsPlanner.IssuePerimeterOpen && i.Dropped && i.Cause.Length > 0);
            foreach (var i in plan.Issues.Where(i => i.At.HasValue)) Assert.False(string.IsNullOrEmpty(i.Cause), "marker without a cause: " + i.Key);
        }

        [Fact]
        public void EveryLocatedIssueNamesAnExistingSetting()
        {
            var ids = new HashSet<string>(new RoadsLayer().Controls.Select(c => c.Id));
            int seen = 0;
            foreach (var kind in new[] { "flat", "hill", "valley", "steep" })
                foreach (var preset in new RoadsLayer().Presets)
                {
                    var input = RoadsDesignTests.SiteInput(preset.Values.Clone(), kind, out _, 150000f);
                    var plan = Run(input);
                    foreach (var i in plan.Issues.Where(i => i.At.HasValue || i.Dropped))
                    {
                        seen++;
                        Assert.False(string.IsNullOrEmpty(i.Cause), $"{preset.Id}/{kind}: issue {i.Key} has no cause setting");
                        Assert.Contains(i.Cause, ids);
                        Assert.False(string.IsNullOrEmpty(i.CauseValue), $"{preset.Id}/{kind}: {i.Key} has no cause value");
                    }
                }
            Assert.True(seen > 0, "the sweep produced no markers to check");
        }

        [Fact]
        public void IssuesShowInTheLegendAndTheUiJsonNamesTheCause()
        {
            var ring = Square(420f, 0f);
            var site = new SiteContext();
            site.Obstacles.Add(Box(1000f, 1000f, 60f, 50f));
            var plan = Plan(ring, p => 40f + 0.12f * (p.x - 800f), Bag(), site);
            var located = plan.Issues.Where(i => i.At.HasValue).ToList();
            Assert.NotEmpty(located);
            var composed = new ComposedPlan();
            var layer = new AreaPlan { GeneratorId = RoadsLayer.LayerId };
            layer.Issues.AddRange(plan.Issues);
            composed.Layers.Add(layer);
            var legend = PreviewLegend.Compute(composed);
            var entry = legend.Single(e => e.Id == PreviewLegend.Issues);
            Assert.Equal(located.Count, entry.Count);
            Assert.Equal(located.Count(i => i.Dropped || i.Severity == IssueSeverity.Error), entry.Blocked);
            Assert.NotNull(entry.Color2);
            Assert.Contains(PreviewLegend.Issues, PreviewLegend.Order);
            var withCause = located.First(i => !string.IsNullOrEmpty(i.Cause));
            string json = UiModels.Issue(withCause, null, null).ToString();
            Assert.Contains("\"cause\"", json);
            Assert.Contains("Citymaker.Roads.Control." + withCause.Cause, json);
        }

        [Fact]
        public void ACornerStaysWhenAStreetEndCrowdsIt()
        {
            var net = new RoadNetwork();
            var corner = new float3(0, 0, 0);
            net.Corners.Add(corner.xz);
            net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(0, 0, -100), corner), NetRole.PerimeterRoad));
            net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(5, 0, 0), new float3(100, 0, 0)), NetRole.PerimeterRoad));
            net.Edges.Add(new RoadEdge(RoadCurve.Straight(new float3(5, 0, 0), new float3(5, 0, 120)), NetRole.Street));
            net.Edges.Add(new RoadEdge(RoadCurve.Straight(corner, new float3(5, 0, 0)), NetRole.PerimeterRoad));
            net.Weld();
            net.CollapseShortEdges(8f);
            net.Weld();
            // The outline corner node did not move; the street end that crowded it moved onto it.
            Assert.Contains(net.Edges, e => e.Alive && e.Role == NetRole.PerimeterRoad && (math.distance(e.Curve.A.xz, corner.xz) < 0.05f || math.distance(e.Curve.D.xz, corner.xz) < 0.05f));
            Assert.Contains(net.Edges, e => e.Alive && e.Role == NetRole.Street && (math.distance(e.Curve.A.xz, corner.xz) < 0.05f || math.distance(e.Curve.D.xz, corner.xz) < 0.05f));
        }
    }
}
