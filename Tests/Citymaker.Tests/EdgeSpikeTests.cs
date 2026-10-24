// s9 playtest: "near the end it tries to find an entrance, but it's already so close to the edge, it has
// to spike out and go somewhere else". A path meets the outline only at an entrance, roughly
// perpendicular, or stops inside; never a leg sideways along the edge (PathQuality.EdgeSpikes). Axial
// layouts align with the outline's edges, and without a placed entrance no path reaches the outline.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests
{
    public class EdgeSpikeTests
    {
        private readonly ITestOutputHelper _out;
        public EdgeSpikeTests(ITestOutputHelper output) { _out = output; }

        /// <summary>Square site with a 12 m street just outside every side (the gallery showcase).</summary>
        private static float3[] Square(float area)
        {
            float a = math.sqrt(area);
            return new[] { new float3(0, 0, 0), new float3(a, 0, 0), new float3(a, 0, a), new float3(0, 0, a) };
        }

        private static SiteContext Streets(float3[] ring)
        {
            var site = new SiteContext { LayoutScale = SiteContext.DefaultLayoutScale };
            for (int i = 0; i < ring.Length; i++)
            {
                float3 p0 = ring[i], p1 = ring[(i + 1) % ring.Length];
                float3 d = math.normalize(p1 - p0);
                float3 n = new float3(d.z, 0, -d.x) * 8f;
                float3 a = p0 - d * 20f + n, b = p1 + d * 20f + n;
                site.NearbyNets.Add(new NearbyNet { ExternalId = 9000 + i, A = a, B = math.lerp(a, b, 1f / 3f), C = math.lerp(a, b, 2f / 3f), D = b, Width = 12f, Category = NetCategory.Road, HasSidewalk = true });
            }
            return site;
        }

        private static float AreaFor(IAreaGenerator g)
        {
            float min = g.MinArea > 0 ? g.MinArea : 100f, max = g.MaxArea > 0 ? g.MaxArea : 1e7f;
            return math.clamp(math.max(12000f, min * 1.3f), min, max);
        }

        public static IEnumerable<object[]> FillTypes()
        {
            foreach (var g in GeneratorRegistry.All)
                if (g.Kind == LayerKind.Fill && !GeneratorRegistry.IsHidden(g) && g.Id != "split") yield return new object[] { g.Id };
        }

        /// <summary>Default + every preset, as the player gets it after accepting the suggested entrances.</summary>
        private static IEnumerable<(string variant, Recipe recipe)> Variants(string id)
        {
            var gen = GeneratorRegistry.Get(id);
            var editor = new DesignEditor(GeneratorRegistry.Get);
            Recipe Make(Action<Recipe> edit)
            {
                var r = Recipe.Single(gen, 1);
                editor.ApplyDefaults(r);
                uint n = 0;
                editor.AddDefaultFeatures(r, () => ++n);
                edit?.Invoke(r);
                uint k = 0;
                foreach (var kv in RecipePaths.Walk(r)) kv.Value.Seed = 1000u + (++k);
                return r;
            }
            yield return ("default", Make(null));
            foreach (var p in gen.Presets ?? Array.Empty<PresetInfo>())
                if (p != null && !string.IsNullOrEmpty(p.Id)) yield return ("preset-" + p.Id, Make(r => editor.ApplyPreset(r, RecipePaths.Root, p.Id)));
        }

        [Theory]
        [MemberData(nameof(FillTypes))]
        public void NoPresetHasEdgeSpikes(string id)
        {
            var gen = GeneratorRegistry.Get(id);
            var ring = Square(AreaFor(gen));
            var bad = new List<string>();
            foreach (var (variant, recipe) in Variants(id))
            {
                var site = Streets(ring);
                TestKit.Entered(recipe, ring, site);
                ComposedPlan plan;
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                    plan = Composer.Compose(recipe, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, cts.Token);
                var spikes = PathQuality.EdgeSpikes(plan, ring);
                foreach (var s in spikes) bad.Add($"{id}/{variant}: {s.Length:0.0} m {(s.Outside ? "outside" : "along the edge")} {s.From} → {s.To}");
            }
            foreach (var b in bad) _out.WriteLine(b);
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }

        [Theory]
        [InlineData("park")]
        [InlineData("formalGarden")]
        [InlineData("memorialPark")]
        [InlineData("botanicalGarden")]
        [InlineData("picnicGrounds")]
        [InlineData("orchardPark")]
        [InlineData("sportsPark")]
        public void WithoutAPlacedEntranceNoPathReachesTheOutline(string id)
        {
            var ring = Square(20000f);
            var site = Streets(ring);
            var input = TestKit.Input(ring, null, 5, null, site);
            Assert.False(TestKit.Gen(id).CheckReady(input).Ready);
            var plan = TestKit.Plan(id, input);
            var region = LayerRegion.From(ring);
            var paths = plan.Nets.Where(PathQuality.IsWalk).ToList();
            Assert.NotEmpty(paths);
            foreach (var n in paths)
            {
                Assert.True(n.StartAttach < 0 && n.EndAttach < 0, $"{id}: a path attaches to a road with no entrance placed");
                foreach (var e in new[] { n.A.xz, n.D.xz })
                    Assert.True(region.BoundaryDistance(e, 3f) >= 2.5f, $"{id}: path end {e} on the outline with no entrance placed");
            }
            // Placing the suggested entrances connects them.
            var entered = TestKit.Plan(id, TestKit.Entered(id, TestKit.Input(ring, null, 5, null, site)));
            Assert.Contains(entered.Nets.Where(PathQuality.IsWalk), n => region.BoundaryDistance(n.A.xz, 1f) < 0.6f || region.BoundaryDistance(n.D.xz, 1f) < 0.6f);
        }

        [Theory]
        [InlineData("formalGarden")]
        [InlineData("memorialPark")]
        public void AxialLayoutsFollowTheEdgesOfASquare(string id)
        {
            var ring = Square(20000f);
            var site = Streets(ring);
            var plan = TestKit.Plan(id, TestKit.Entered(id, TestKit.Input(ring, null, 5, null, site)));
            // Every long straight path runs along x or z (no 45° axes on a square).
            int straight = 0;
            foreach (var n in plan.Nets.Where(PathQuality.IsWalk))
            {
                var d = n.D.xz - n.A.xz;
                if (math.length(d) < 15f) continue;
                var u = math.normalize(d);
                if (math.abs(u.x) > 0.995f || math.abs(u.y) > 0.995f) straight++;
                else Assert.True(math.min(math.abs(u.x), math.abs(u.y)) < 0.1f, $"{id}: path {n.A.xz} → {n.D.xz} is diagonal");
            }
            Assert.True(straight >= 2, id + ": no axis");
        }

        [Fact]
        public void PrincipalAxesOfASquareFollowItsEdges()
        {
            foreach (float deg in new[] { 0f, 17f, 30f, 45f, 62f })
            {
                float a = math.radians(deg);
                var u = new float2(math.cos(a), math.sin(a)); var v = new float2(-u.y, u.x);
                var ring = new[] { float2.zero, u * 100f, u * 100f + v * 100f, v * 100f };
                LayerRegion.From(ring).PrincipalAxes(out _, out var major, out _, out _);
                float along = math.max(math.abs(math.dot(major, u)), math.abs(math.dot(major, v)));
                Assert.True(along > 0.999f, $"square at {deg}°: axis {major} is not along an edge");
            }
        }

        [Fact]
        public void EdgeSpikeMetricFlagsASidewaysLegButNotAPerpendicularApproach()
        {
            var ring = Square(10000f);   // 100 × 100
            PlannedNet Net(float2 a, float2 b) => new PlannedNet { A = new float3(a.x, 0, a.y), B = new float3(math.lerp(a, b, 1f / 3f).x, 0, math.lerp(a, b, 1f / 3f).y), C = new float3(math.lerp(a, b, 2f / 3f).x, 0, math.lerp(a, b, 2f / 3f).y), D = new float3(b.x, 0, b.y), Role = NetRole.Pathway };
            // Perpendicular entrance approach + an internal walk: clean.
            var good = new List<PlannedNet> { Net(new float2(50, 0), new float2(50, 30)), Net(new float2(50, 30), new float2(80, 60)) };
            Assert.Empty(PathQuality.EdgeSpikes(good, ring));
            // A main path that runs out to the edge and then sideways 15 m along it to the entrance.
            var bad = new List<PlannedNet> { Net(new float2(30, 40), new float2(35, 2)), Net(new float2(35, 2), new float2(50, 1.5f)), Net(new float2(50, 1.5f), new float2(50, 0)) };
            Assert.Single(PathQuality.EdgeSpikes(bad, ring));
            // A walk out past the outline.
            var outside = new List<PlannedNet> { Net(new float2(20, 10), new float2(20, -8)) };
            Assert.Single(PathQuality.EdgeSpikes(outside, ring));
            // A designed perimeter walk (one long run) is exempt.
            var walk = new List<PlannedNet> { Net(new float2(5, 2.5f), new float2(95, 2.5f)) };
            Assert.Empty(PathQuality.EdgeSpikes(walk, ring));
        }
    }
}
