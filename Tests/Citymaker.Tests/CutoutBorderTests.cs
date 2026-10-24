// s24: the park's brick wall with railings ran round three sides of every zoning cutout (a walled pen around the
// shops). Borders — fence/wall nets, fence pieces, hedges, bollards — follow only the outer outline; nothing of
// the kind within ~1 m of a cutout edge unless that edge is also the outer outline.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class CutoutBorderTests
    {
        private static float2[] Rect(float x0, float z0, float x1, float z1) =>
            new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) };

        private static bool NearInnerCutEdge(float2 p, List<float2[]> cuts, float2[] outline, float reach) =>
            cuts.Any(c => Poly.DistanceToBoundary(c, p) < reach) && Poly.DistanceToBoundary(outline, p) >= reach;

        private static void AssertNoBorderOnCutouts(ComposedPlan plan, float2[] outline, string ctx)
        {
            var cuts = Cutouts.Of(plan);
            Assert.NotEmpty(cuts);
            foreach (var l in plan.Layers)
            {
                foreach (var n in l.Nets.Where(Cutouts.IsBorder))
                    for (int i = 0; i <= 48; i++)
                    {
                        var p = Composer.Bezier(n.A, n.B, n.C, n.D, i / 48f).xz;
                        // 0.9 m: the clip works on 1/48 samples, its cut ends sit a fraction of a sample inside the reach.
                        Assert.False(NearInnerCutEdge(p, cuts, outline, 0.9f), $"{ctx}: {l.GeneratorId} fence along a cutout edge at {p}");
                    }
                foreach (var o in l.Objects.Where(Cutouts.IsBorder))
                    Assert.False(NearInnerCutEdge(o.Position.xz, cuts, outline, 1f), $"{ctx}: {l.GeneratorId} {o.Role}/{o.Category} along a cutout edge at {o.Position.xz}");
            }
        }

        [Theory]
        [InlineData("band", StreetShops.Few)]
        [InlineData("row", StreetShops.Some)]
        [InlineData("row", StreetShops.Lots)]
        public void Park_fence_follows_the_outline_not_the_cutouts(string layout, string amount)
        {
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var ring = TestKit.Rect(140f, 120f);
            var site = ZoningCutoutTests.Streets(ring);
            var r = editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 9);
            editor.SetFeature(r, "0", LayerBase.StreetShopsLayer, true, 31);
            r.Root.Settings.Values[StreetShops.LayoutId] = layout;
            r.Root.Settings.Values[StreetShops.AmountId] = amount;
            r.Root.Settings.Values["fence"] = true;
            TestKit.Entered(r, ring, site);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            var outline = Poly.EnsureCcw(Poly.To2D(ring));
            AssertNoBorderOnCutouts(plan, outline, "park " + layout + " " + amount);
            // The fence still runs along the outline (the streets): a border was not simply dropped.
            var fence = plan.Layers.SelectMany(l => l.Nets).Where(Cutouts.IsBorder).ToList();
            Assert.NotEmpty(fence);
            float len = fence.Sum(n => math.distance(n.A.xz, n.D.xz));
            if (amount != StreetShops.Lots) Assert.True(len > 40f, $"fence {len:0} m of {Poly.Perimeter(outline):0} m outline, {Cutouts.Of(plan).Count} cutouts");
            Assert.All(fence, n => Assert.True(Poly.DistanceToBoundary(outline, Composer.Bezier(n.A, n.B, n.C, n.D, 0.5f).xz) < 1.5f, "fence away from the outline"));
        }

        [Fact]
        public void Clear_borders_on_a_hand_made_plan()
        {
            // Outline 0…100 × 0…80; a cutout 20…44 × −1…16 notched out of the south edge; the host's fence ran round the notch.
            var outline = Rect(0, 0, 100, 80);
            var cut = Rect(20, -1, 44, 16);
            var plan = new ComposedPlan();
            var park = new AreaPlan { GeneratorId = "park" };
            NetRole fence = NetRole.Fence;
            void Fence(float2 a, float2 b) => park.Nets.Add(new PlannedNet { Role = fence, A = new float3(a.x, 0, a.y), B = new float3(math.lerp(a, b, 1f / 3f).x, 0, math.lerp(a, b, 1f / 3f).y), C = new float3(math.lerp(a, b, 2f / 3f).x, 0, math.lerp(a, b, 2f / 3f).y), D = new float3(b.x, 0, b.y) });
            Fence(new float2(0.2f, 0.2f), new float2(20f, 0.2f));     // outline (keep)
            Fence(new float2(20f, 0.2f), new float2(20f, 16f));       // notch side (drop)
            Fence(new float2(20f, 16f), new float2(44f, 16f));        // notch back (drop)
            Fence(new float2(44f, 16f), new float2(44f, 0.2f));       // notch side (drop)
            Fence(new float2(44f, 0.2f), new float2(99.8f, 0.2f));    // outline (keep)
            park.Nets.Add(new PlannedNet { Role = NetRole.Pathway, A = new float3(30, 0, 30), B = new float3(30, 0, 25), C = new float3(30, 0, 20), D = new float3(30, 0, 16.5f) });
            park.Objects.Add(new PlannedObject { Role = ObjectRole.Bush, Category = AssetCategories.Hedge, Position = new float3(32, 0, 17) });   // hedge on the notch back
            park.Objects.Add(new PlannedObject { Role = ObjectRole.Bush, Category = AssetCategories.Hedge, Position = new float3(70, 0, 0.6f) }); // hedge on the outline
            park.Objects.Add(new PlannedObject { Role = ObjectRole.Bench, Position = new float3(32, 0, 18), AttachedNet = 5 });                  // not a border
            plan.Layers.Add(park);
            var cuts = new List<float2[]> { cut };
            Cutouts.ClearBorders(plan, cuts, new[] { outline });
            var fences = park.Nets.Where(Cutouts.IsBorder).ToList();
            Assert.Equal(2, fences.Count);
            Assert.All(fences, n => Assert.True(n.A.z < 1f && n.D.z < 1f));
            Assert.Single(park.Nets, n => n.Role == NetRole.Pathway);
            Assert.Equal(2, park.Objects.Count);
            Assert.Contains(park.Objects, o => o.Role == ObjectRole.Bench && o.AttachedNet == 0);   // index follows the path
            Assert.Contains(park.Objects, o => o.Position.x == 70f);
        }
    }
}
