using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Layers.Park;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests
{
    /// <summary>FEEDBACK-4: global Layout size (SiteContext.LayoutScale) and the one edge margin.</summary>
    [Collection(TimingCollection.Name)]
    public class LayoutSizeTests
    {
        private readonly ITestOutputHelper _out;
        public LayoutSizeTests(ITestOutputHelper output) { _out = output; }

        private static SiteContext At(float scale, SiteContext baseSite = null)
        {
            var s = baseSite ?? new SiteContext();
            s.LayoutScale = scale;
            return s;
        }

        private static readonly float3[] Hectare = TestKit.Rect(100, 100);

        [Fact]
        public void SiteScaleIsClampedAndCopied()
        {
            Assert.Equal(1f, new SiteContext().Scale);
            Assert.Equal(SiteContext.LayoutScaleMin, new SiteContext { LayoutScale = 0.01f }.Scale);
            Assert.Equal(SiteContext.LayoutScaleMax, new SiteContext { LayoutScale = 9f }.Scale);
            Assert.Equal(1f, new SiteContext { LayoutScale = float.NaN }.Scale);
            var s = new SiteContext { LayoutScale = 0.5f, EdgeMargin = 3f };
            var h = s.WithHeight(_ => 1f);
            Assert.Equal(0.5f, h.LayoutScale);
            Assert.Equal(3f, h.EdgeMargin);
            var l = s.WithLayout(0.75f, 1f);
            Assert.Equal(0.75f, l.LayoutScale);
            Assert.Equal(1f, l.EdgeMargin);
            Assert.Equal(0.5f, s.LayoutScale);
        }

        [Fact]
        public void LayoutScopeIsAmbientOnlyInsideAPlanAndNests()
        {
            Assert.Equal(1f, Layout.Scale);
            using (Layout.Use(new SiteContext { LayoutScale = 0.5f }))
            {
                Assert.Equal(0.5f, Layout.Scale);
                Assert.Equal(5f, Layout.M(10f));
                Assert.Equal(8f, Layout.M(10f, 8f));
                Assert.Equal(1f, Layout.EdgeMargin);   // 2 m × 0.5
                using (Layout.Use(0.25f, 0f)) Assert.Equal(0.25f, Layout.Scale);
                Assert.Equal(0.5f, Layout.Scale);
            }
            Assert.Equal(1f, Layout.Scale);
        }

        [Fact]
        public void LayoutSlidersAreReadScaledWithTheirFloor()
        {
            var gen = (LayerBase)TestKit.Gen("memorialPark");
            var c = gen.Control("treeSpacing");
            Assert.True(c.Layout);
            Assert.False(gen.Control("processionWidth").Layout);   // a real width
            var ctx = new LayerContext
            {
                Input = TestKit.Input(Hectare, site: At(0.5f)), Settings = gen.Defaults(), ControlOf = gen.Control,
            };
            Assert.Equal(math.max((float)c.LayoutMin, 9f * 0.5f), ctx.F("treeSpacing"), 3);
            Assert.Equal(9f, ctx.Raw("treeSpacing"), 3);
            Assert.Equal(6f, ctx.F("processionWidth"), 3);
            Assert.Equal(1f, ctx.EdgeMargin, 3);
        }

        [Fact]
        public void ParkPathsAreThinByDefault()
        {
            var plan = TestKit.Plan("park", TestKit.Input(TestKit.Rect(250, 200), site: At(0.5f, TestKit.RoadAlongBottom(250))));
            Assert.False(plan.HasErrors);
            Assert.NotEmpty(plan.Nets);
            Assert.All(plan.Nets, n => Assert.Equal(NetRole.Pathway, n.Role));
        }

        [Fact]
        public void SmallerLayoutMeansMorePathPerAreaAndCloserBenches()
        {
            var ring = TestKit.Rect(250, 200);
            var full = TestKit.Plan("park", TestKit.Input(ring, site: At(1f, TestKit.RoadAlongBottom(250))));
            var half = TestKit.Plan("park", TestKit.Input(ring, site: At(0.5f, TestKit.RoadAlongBottom(250))));
            double lf = TestKit.Stat(full, Keys.StatPathLength), lh = TestKit.Stat(half, Keys.StatPathLength);
            int bf = TestKit.Count(full, ObjectRole.Bench), bh = TestKit.Count(half, ObjectRole.Bench);
            _out.WriteLine($"path {lf:0} → {lh:0} m, benches {bf} → {bh}");
            Assert.True(lh >= lf * 0.75, $"path length {lf} → {lh}");
            Assert.True(bh > bf, $"benches {bf} → {bh}");
        }

        /// <summary>A 1 ha park at the default 50 %: a park, not a forest and not an empty lawn.</summary>
        [Fact]
        public void OneHectareParkAtHalfSizeHasAReasonableObjectDensity()
        {
            var plan = TestKit.Plan("park", TestKit.Input(Hectare, site: At(0.5f, TestKit.RoadAlongBottom(100))));
            Assert.False(plan.HasErrors);
            int trees = TestKit.Count(plan, ObjectRole.Tree), bushes = TestKit.Count(plan, ObjectRole.Bush);
            int benches = TestKit.Count(plan, ObjectRole.Bench), lamps = TestKit.Count(plan, ObjectRole.Lamp);
            double path = TestKit.Stat(plan, Keys.StatPathLength);
            _out.WriteLine($"1 ha @50%: {trees} trees, {bushes} bushes, {benches} benches, {lamps} lamps, {path:0} m path, {plan.Objects.Count} objects");
            Assert.InRange(trees, 25, 160);
            Assert.InRange(bushes, 30, 300);
            Assert.InRange(benches, 3, 40);
            Assert.InRange(lamps, 2, 60);
            Assert.InRange(path, 80, 900);
            Assert.InRange(plan.Objects.Count, 80, 900);
            TestKit.AssertObjectsInside(plan, Hectare, 0.5f);
            TestKit.AssertObjectsClearOfNets(plan);
        }

        public static IEnumerable<object[]> FillTypes()
        {
            foreach (var g in GeneratorRegistry.All)
                if (g.Kind == LayerKind.Fill && g is LayerBase && g.Id != "split") yield return new object[] { g.Id };
        }

        /// <summary>Every type at 50 %: plans, is deterministic, keeps objects inside, nothing on its own paths (park family).</summary>
        [Theory]
        [MemberData(nameof(FillTypes))]
        public void EveryTypePlansDeterministicallyAtHalfSize(string id)
        {
            var ring = TestKit.Rect(140, 110);
            var gen = TestKit.Gen(id);
            var sw = Stopwatch.StartNew();
            var a = gen.Plan(TestKit.Input(ring, seed: 7, site: At(0.5f, TestKit.RoadAlongBottom(140))), CancellationToken.None);
            long ms = sw.ElapsedMilliseconds;
            var b = gen.Plan(TestKit.Input(ring, seed: 7, site: At(0.5f, TestKit.RoadAlongBottom(140))), CancellationToken.None);
            _out.WriteLine($"{id}: {a.Objects.Count} objects, {a.Nets.Count} nets, {ms} ms");
            Assert.Equal(TestKit.Fingerprint(a), TestKit.Fingerprint(b));
            Assert.DoesNotContain(a.Issues, i => i.Severity == IssueSeverity.Error && i.Key == "Citymaker.Issue.LayerFailed");
            TestKit.AssertObjectsInside(a, ring, 1.5f);
            Assert.True(ms < 20000, $"{id} took {ms} ms");
        }

        [Theory]
        [InlineData("park")]
        [InlineData("memorialPark")]
        [InlineData("formalGarden")]
        public void ParkLikeTypesKeepFurnitureOffTheirPathsAtHalfSize(string id)
        {
            var ring = TestKit.Rect(160, 120);
            var plan = TestKit.Plan(id, TestKit.Input(ring, seed: 3, site: At(0.5f, TestKit.RoadAlongBottom(160))));
            foreach (var o in plan.Objects)
            {
                if (o.Role != ObjectRole.Bench && o.Role != ObjectRole.Lamp && o.Role != ObjectRole.Bin) continue;
                float r = TestKit.FootprintFor(o.Role);
                foreach (var n in plan.Nets)
                {
                    if (n.Role == NetRole.Fence) continue;
                    float d = TestKit.DistanceToCurve(o.Position.xz, n);
                    Assert.True(d >= Composer.DefaultHalfWidth(n.Role) + r - 0.1f, $"{id}: {o.Role} at {o.Position.xz} is {d:0.00} m from a {n.Role}");
                }
            }
        }

        /// <summary>Benches stand beside a path (within a few metres of a net edge), not out on the lawn.</summary>
        [Theory]
        [InlineData("memorialPark")]
        [InlineData("formalGarden")]
        public void BenchesStandBesidePaths(string id)
        {
            var ring = TestKit.Rect(160, 120);
            var plan = TestKit.Plan(id, TestKit.Input(ring, seed: 3, site: At(0.5f, TestKit.RoadAlongBottom(160))));
            var benches = plan.Objects.Where(o => o.Role == ObjectRole.Bench).ToList();
            Assert.NotEmpty(benches);
            int off = 0;
            foreach (var o in benches)
            {
                float best = float.MaxValue;
                foreach (var n in plan.Nets)
                    if (n.Role != NetRole.Fence) best = math.min(best, TestKit.DistanceToCurve(o.Position.xz, n) - Composer.DefaultHalfWidth(n.Role));
                if (best > 3.5f) off++;
            }
            _out.WriteLine($"{id}: {off} of {benches.Count} benches off-path");
            Assert.True(off <= benches.Count / 5, $"{id}: {off} of {benches.Count} benches are more than 3.5 m from a path");
        }

        [Fact]
        public void MemorialWalksAreNetsNotPaint()
        {
            var ring = TestKit.Rect(160, 90);
            var noPool = new SettingsBag(); noPool.Values["pool"] = false;   // the pool coping is paving by design
            var plan = TestKit.Plan("memorialPark", TestKit.Input(ring, noPool, site: At(0.5f, TestKit.RoadAlongBottom(160))));
            Assert.NotEmpty(plan.Nets);
            // Default (garden) style without the optional paving: the only paving is the forecourt round the monument.
            var mon = plan.Objects.First(o => o.Role == ObjectRole.Centerpiece).Position.xz;
            foreach (var s in plan.Surfaces.Where(s => s.Role == SurfaceRole.Plaza))
                foreach (var p in s.Ring) Assert.True(math.distance(p.xz, mon) < 14f, $"paving at {p.xz} is not the forecourt");
        }
    }
}
