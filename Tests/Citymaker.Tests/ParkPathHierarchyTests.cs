// Grand park wave (g3): park path hierarchy (bike route, small paths, dirt trails, desire lines).

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Layers.Park;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests
{
    [Collection(TimingCollection.Name)]
    public class ParkPathHierarchyTests
    {
        private readonly ITestOutputHelper _out;
        public ParkPathHierarchyTests(ITestOutputHelper output) { _out = output; }

        private static float3[] L(float hectares) => TestKit.LShape(math.sqrt(hectares * 10000f / 0.6975f));

        private static SettingsBag Preset(string id) => GeneratorRegistry.Get("park").Presets.Single(p => p.Id == id).Values.Clone();

        private static SettingsBag Bag(SettingsBag b, params (string k, object v)[] kv)
        {
            var c = b?.Clone() ?? new SettingsBag();
            foreach (var (k, v) in kv) c.Values[k] = v;
            return c;
        }

        private static AreaPlan Plan(SettingsBag s, float3[] ring, uint seed = 3, ReservedSpace reserved = null)
        {
            var input = TestKit.Input(ring, s, seed);
            if (reserved != null) input.Reserved = reserved;
            return TestKit.Plan("park", input);
        }

        private static bool IsBike(PlannedNet n) => n.Asset.Fallbacks != null && n.Asset.Fallbacks.Contains("TwoWayBikePath");
        private static bool IsTrail(PlannedNet n) => n.Asset.Fallbacks != null && n.Asset.Fallbacks.Contains("Gravel Pathway");

        private static List<float2> Samples(PlannedNet n, int k = 16)
        {
            var r = new List<float2>();
            for (int i = 0; i <= k; i++) r.Add(Composer.Bezier(n.A, n.B, n.C, n.D, i / (float)k).xz);
            return r;
        }

        private static float Fact(AreaPlan p, string key) => p.Facts.TryGetValue(key, out var v) ? (float)v : 0f;

        [Fact]
        public void BikeRouteIsAWideThroughRouteBetweenItsOwnEntries()
        {
            var ring = L(6);
            var plan = TestKit.Plan("park", TestKit.Entered("park", TestKit.Input(ring, Bag(null, ("bikePath", true)), 3)));
            Assert.False(plan.HasErrors);
            var bike = plan.Nets.Where(IsBike).ToList();
            Assert.NotEmpty(bike);
            Assert.All(bike, n => Assert.Equal(NetRole.PathwayWide, n.Role));
            Assert.True(Fact(plan, "bikeLength") > 100f, "bike route only " + Fact(plan, "bikeLength") + " m");
            var r2 = ring.Select(p => p.xz).ToArray();
            foreach (var n in bike)
                foreach (var p in Samples(n))
                    Assert.True(Poly.Contains(r2, p) || Poly.DistanceToBoundary(r2, p) < 0.5f, "bike route leaves the park at " + p);
            // One connected route: exactly two loose ends, both on the outline (its entries).
            var ends = bike.SelectMany(n => new[] { n.A.xz, n.D.xz }).ToList();
            var loose = ends.Where(e => ends.Count(o => math.distancesq(o, e) < 1e-3f) == 1).ToList();
            Assert.Equal(2, loose.Count);
            Assert.All(loose, e => Assert.True(Poly.DistanceToBoundary(r2, e) < 0.5f, "bike end inside the park"));
        }

        [Fact]
        public void BikeRouteKeepsApartFromFootpathsAndCrossesThemSquareAtNodes()
        {
            var ring = L(6);
            var plan = Plan(Bag(null, ("bikePath", true), ("bikeGap", 10.0)), ring);
            var bike = plan.Nets.Where(IsBike).ToList();
            var foot = plan.Nets.Where(n => !IsBike(n) && n.Role != NetRole.Fence).ToList();
            Assert.NotEmpty(bike);
            int crossings = (int)Fact(plan, "bikeCrossings");
            if (crossings > 0) Assert.True(Fact(plan, "bikeMinCrossingAngle") >= 30f, "shallow crossing: " + Fact(plan, "bikeMinCrossingAngle") + "°");
            // Every crossing is at a shared node (no bike curve passes through a footpath mid-curve).
            foreach (var b in bike)
            {
                var bs = Samples(b);
                foreach (var f in foot)
                {
                    var fs = Samples(f);
                    for (int i = 1; i < bs.Count; i++)
                        for (int j = 1; j < fs.Count; j++)
                        {
                            if (!Poly.SegmentIntersection(bs[i - 1], bs[i], fs[j - 1], fs[j], out var x, out _, out _)) continue;
                            bool atNode = new[] { b.A.xz, b.D.xz, f.A.xz, f.D.xz }.Any(e => math.distance(e, x) < 2.5f);
                            Assert.True(atNode, "bike route crosses a footpath without a junction at " + x);
                        }
                }
            }
            // Separation: little of the route runs right beside a footpath (away from crossings and entries).
            var nodes = bike.SelectMany(n => new[] { n.A.xz, n.D.xz }).ToList();
            int near = 0, total = 0;
            foreach (var b in bike)
                foreach (var p in Samples(b))
                {
                    if (nodes.Any(e => math.distance(e, p) < 14f)) continue;
                    total++;
                    if (foot.Any(f => TestKit.DistanceToCurve(p, f) < 4f)) near++;
                }
            _out.WriteLine($"crossings {crossings}, near {near}/{total}");
            Assert.True(total == 0 || near <= total * 0.15f, $"{near} of {total} bike samples hug a footpath");
        }

        [Fact]
        public void BikeRouteNeedsTwoEntrancesAndSaysSo()
        {
            var plan = Plan(Bag(null, ("bikePath", true)), TestKit.Rect(40, 30));
            Assert.False(plan.HasErrors);
            Assert.DoesNotContain(plan.Nets, IsBike);
            Assert.Contains(plan.Issues, i => i.Key == "Citymaker.Park.Issue.BikeNeedsTwoEntrances" || i.Key == "Citymaker.Park.Issue.NoBikeEntrances" || i.Key == "Citymaker.Park.Issue.NoBikeRoute");
        }

        [Fact]
        public void SmallPathsTrailsAndPavedShortcutsAddNarrowJoinedPaths()
        {
            var ring = L(6);
            var basePlan = Plan(Bag(null, ("pathStyle", "curvy")), ring);
            var plan = Plan(Bag(null, ("pathStyle", "curvy"), ("smallPaths", 3.0), ("trails", 2.0), ("desireLines", "paved")), ring);
            Assert.False(plan.HasErrors);
            Assert.True(Fact(plan, "smallPaths") >= 1f, "no small path");
            Assert.True(Fact(plan, "trails") >= 1f, "no trail");
            Assert.True(plan.Nets.Count > basePlan.Nets.Count);
            Assert.Contains(plan.Nets, IsTrail);
            Assert.All(plan.Nets.Where(IsTrail), n => Assert.Equal(NetRole.Pathway, n.Role));
            TestKit.AssertObjectsClearOfNets(plan);
            TestKit.AssertObjectsInside(plan, ring);
            // The network stays connected: every net end meets another net or the outline (trails may fade out).
            var r2 = ring.Select(p => p.xz).ToArray();
            var paths = plan.Nets.Where(n => n.Role != NetRole.Fence && !IsTrail(n)).ToList();
            var ends = paths.SelectMany(n => new[] { n.A.xz, n.D.xz }).ToList();
            int loose = ends.Count(e => ends.Count(o => math.distancesq(o, e) < 0.01f) == 1 && Poly.DistanceToBoundary(r2, e) > Composer.DefaultHalfWidth(NetRole.PathwayWide) + 0.6f);
            Assert.True(loose <= 6, loose + " loose path ends");
        }

        [Fact]
        public void WornDesireLinesPaintTheGroundWithoutNets()
        {
            var ring = L(6);
            var plain = Plan(Bag(null, ("pathStyle", "auto")), ring, 5);
            var plan = Plan(Bag(null, ("pathStyle", "auto"), ("desireLines", "worn"), ("desireCount", 4.0)), ring, 5);
            Assert.False(plan.HasErrors);
            Assert.Equal(plain.Nets.Count, plan.Nets.Count);
            int lines = (int)Fact(plan, "desireLines");
            _out.WriteLine("desire lines: " + lines);
            if (lines > 0)
            {
                Assert.True(plan.Paint.Count > plain.Paint.Count);
                Assert.Contains(plan.Issues, i => i.Key == TerrainPaint.PermanentKey);
            }
            else Assert.Contains(plan.Issues, i => i.Key == "Citymaker.Park.Issue.NoDesireLines");
            TestKit.AssertObjectsClearOfNets(plan);
        }

        public static IEnumerable<object[]> ParkPresets() => GeneratorRegistry.Get("park").Presets.Select(p => new object[] { p.Id });

        [Theory]
        [MemberData(nameof(ParkPresets))]
        public void EveryParkPresetPlansWithTheHierarchy(string preset)
        {
            foreach (var ring in new[] { L(1), L(6) })
            {
                var plan = Plan(Preset(preset), ring);
                Assert.False(plan.HasErrors, preset);
                TestKit.AssertObjectsInside(plan, ring);
                TestKit.AssertObjectsClearOfNets(plan);
                TestKit.AssertSurfacesAccepted(plan);
            }
            Assert.Equal(TestKit.Fingerprint(Plan(Preset(preset), L(6), 9)), TestKit.Fingerprint(Plan(Preset(preset), L(6), 9)));
        }

        [Fact]
        public void GreenwayHasEveryTier()
        {
            var plan = Plan(Preset("greenway"), L(6));
            Assert.Contains(plan.Nets, IsBike);
            Assert.True(Fact(plan, "smallPaths") >= 1f);
            Assert.True(Fact(plan, "trails") + Fact(plan, "desireLines") >= 1f);
        }

        [Fact]
        public void TiersRespectReservedSpace()
        {
            var ring = L(6);
            var reserved = new ReservedSpace();
            var c = Poly.Centroid(ring.Select(p => p.xz).ToArray());
            reserved.AddCorridor(c + new float2(-60, 10), c + new float2(60, 10), 3f, "Pipe");
            reserved.AddDisc(c + new float2(20, -25), 10f, "Tree");
            var plan = Plan(Bag(Preset("greenway"), ("trails", 3.0)), ring, 3, reserved);
            Assert.False(plan.HasErrors);
            TestKit.AssertClearOfReserved(plan, reserved);
            foreach (var n in plan.Nets.Where(n => IsBike(n) || IsTrail(n)))
                foreach (var p in Samples(n))
                {
                    foreach (var rc in reserved.Corridors)
                        Assert.True(math.sqrt(SegmentIndex.DistSq(p, rc.A, rc.B)) > rc.HalfWidth, "tier path on a reserved corridor");
                }
        }

        [Fact]
        public void HierarchyIsFastOnFiveHectares()
        {
            var ring = L(5);
            var s = Bag(Preset("greenway"), ("trails", 2.0), ("smallPaths", 3.0));
            Plan(s, ring);   // JIT
            var sw = Stopwatch.StartNew();
            Plan(s, ring);
            sw.Stop();
            var off = Stopwatch.StartNew();
            Plan(Bag(s, ("bikePath", false), ("trails", 0.0), ("smallPaths", 0.0), ("desireLines", "none")), ring);
            off.Stop();
            _out.WriteLine($"greenway 5 ha: {sw.ElapsedMilliseconds} ms (tiers off: {off.ElapsedMilliseconds} ms)");
            Assert.True(sw.ElapsedMilliseconds - off.ElapsedMilliseconds < TimingBudget.Ms(250), $"tiers cost {sw.ElapsedMilliseconds - off.ElapsedMilliseconds} ms");
        }

        [Fact]
        public void BikeAndTrailCategoriesClassifyNetsByName()
        {
            var rule = new ParkPathCategoryRules();
            var r = new List<string>();
            rule.Classify("twowaybikepath", AssetTraits.Net | AssetTraits.Pathway, r);
            Assert.Contains(AssetCategories.BikePath, r);
            r.Clear();
            rule.Classify("gravel pathway", AssetTraits.Net | AssetTraits.Pathway, r);
            Assert.Contains(AssetCategories.TrailPath, r);
            r.Clear();
            rule.Classify("bikerack01", AssetTraits.StaticObject | AssetTraits.HasMesh, r);
            Assert.Empty(r);
        }
    }
}
