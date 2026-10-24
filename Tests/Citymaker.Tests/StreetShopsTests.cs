// s12/s14: the Zoning feature on parks, plazas… (Core/Layers/Common/StreetShops.cs): plain cutouts in a row or
// speckled; amount none/few/some/lots; per-type defaults. s12's alley layouts live on in the Commercial
// "Shops & park" type (CommercialTests); their saved values plan as a row here.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class StreetShopsTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);
        private const float Side = 120f;

        /// <summary>A square block with a 12 m street on every side (centre 8 m off the outline), as the gallery showcase.</summary>
        private static SiteContext Streets(float3[] ring)
        {
            var site = new SiteContext();
            for (int i = 0; i < ring.Length; i++)
            {
                float3 p0 = ring[i], p1 = ring[(i + 1) % ring.Length];
                float3 d = math.normalize(p1 - p0);
                float3 n = new float3(d.z, 0, -d.x) * 8f;
                float3 a = p0 - d * 20f + n, b = p1 + d * 20f + n;
                site.NearbyNets.Add(new NearbyNet { ExternalId = 500 + i, A = a, B = math.lerp(a, b, 1f / 3f), C = math.lerp(a, b, 2f / 3f), D = b, Width = 12f, Category = NetCategory.Road, HasSidewalk = true });
            }
            return site;
        }

        private static ComposedPlan Compose(string host, string layout, string amount, float3[] ring, SiteContext site, bool entrances = true)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(host), 7);
            Assert.True(Editor.SetFeature(r, "0", LayerBase.StreetShopsLayer, true, 31), host + " has no street shops");
            if (layout != null) r.Root.Settings.Values[StreetShops.LayoutId] = layout;
            if (amount != null) r.Root.Settings.Values[StreetShops.AmountId] = amount;
            if (entrances) TestKit.Entered(r, ring, site);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        private static List<float2[]> ShopRegions(AreaPlan host) =>
            host.Regions.Where(g => g.Tags.Contains(StreetFrontage.ShopsTag)).Select(g => Poly.To2D(g.Ring)).ToList();

        private static float2[] Inset(float2[] q, float by)
        {
            var c = Poly.Centroid(q);
            return q.Select(p => p + math.normalizesafe(c - p) * by).ToArray();
        }

        private static IEnumerable<float2> Samples(PlannedNet n, int k = 24)
        {
            for (int i = 0; i <= k; i++) yield return Composer.Bezier(n.A, n.B, n.C, n.D, i / (float)k).xz;
        }

        /// <summary>Road centre lines a shop may face: existing streets and the alleys the host planned.</summary>
        private static List<(float2 a, float2 b, float hw)> RoadLines(SiteContext site, AreaPlan host)
        {
            var lines = new List<(float2, float2, float)>();
            foreach (var n in site.NearbyNets)
            {
                float3 prev = n.A;
                for (int i = 1; i <= 16; i++) { var p = Composer.Bezier(n.A, n.B, n.C, n.D, i / 16f); lines.Add((prev.xz, p.xz, n.Width * 0.5f)); prev = p; }
            }
            foreach (var n in host.Nets.Where(n => n.Role == NetRole.Alley)) lines.Add((n.A.xz, n.D.xz, Composer.DefaultHalfWidth(NetRole.Alley)));
            return lines;
        }

        private static bool FacesRoad(float2[] q, List<(float2 a, float2 b, float hw)> roads)
        {
            for (int i = 0; i < q.Length; i++)
            {
                var m = 0.5f * (q[i] + q[(i + 1) % q.Length]);
                foreach (var (a, b, hw) in roads)
                    if (math.sqrt(SegmentIndex.DistSq(m, a, b)) <= hw + StreetFrontage.Tolerance + 0.01f) return true;
            }
            return false;
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (var host in new[] { "park", "plaza", "marketSquare" })
                foreach (var layout in StreetShops.FeatureLayouts)
                    foreach (var amount in new[] { StreetShops.Some, StreetShops.Lots })
                        yield return new object[] { host, layout, amount };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void EveryLayoutZonesShopsThatFaceARoadAndThePlaceWeavesAroundThem(string hostId, string layout, string amount)
        {
            var ring = TestKit.Rect(Side, Side);
            var site = Streets(ring);
            var plan = Compose(hostId, layout, amount, ring, site);
            Assert.False(plan.HasErrors, string.Join("; ", plan.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key)));
            var host = plan.Layers.First(l => l.GeneratorId == hostId);
            var shops = ShopRegions(host);
            Assert.NotEmpty(shops);
            var zoning = plan.Layers.Where(l => l.GeneratorId == LayerBase.StreetShopsLayer).SelectMany(l => l.Zoning).ToList();
            Assert.NotEmpty(zoning);

            // Zoning only grows next to a road: every shop region faces a street or a planned alley.
            var roads = RoadLines(site, host);
            foreach (var q in shops) Assert.True(FacesRoad(q, roads), $"{layout}: shop region at {Poly.Centroid(q)} faces no road");
            // Every zoned quad lies on shop land.
            foreach (var z in zoning)
            {
                var c = 0.25f * (z.A.xz + z.B.xz + z.C.xz + z.D.xz);
                Assert.Contains(shops, q => Poly.Contains(q, c) || Poly.DistanceToBoundary(q, c) < 0.5f);
            }
            // Shop regions never overlap each other.
            for (int i = 0; i < shops.Count; i++)
                for (int k = i + 1; k < shops.Count; k++)
                    Assert.False(StreetShops.ConvexOverlap(shops[i], shops[k], 0.05f), $"{layout}: shop regions {i} and {k} overlap");

            // Plain cutouts: the feature plans no alleys.
            Assert.DoesNotContain(host.Nets, n => n.Role == NetRole.Alley);

            // The place plans around the shops: no object of any layer but the zoning stands on shop land, and
            // no path runs over it.
            foreach (var l in plan.Layers.Where(l => l.GeneratorId != LayerBase.StreetShopsLayer))
            {
                foreach (var o in l.Objects.Where(o => o.Role != ObjectRole.Decal))
                    Assert.False(shops.Any(q => Poly.Contains(q, o.Position.xz)), $"{l.GeneratorId}: {o.Role}/{o.Category} at {o.Position.xz} stands on shop land");
                foreach (var n in l.Nets.Where(PathJoins.IsPath))
                    foreach (var p in Samples(n))
                        Assert.False(shops.Any(q => Poly.Contains(Inset(q, 0.3f), p)), $"{l.GeneratorId}: path over shop land at {p}");
            }
            // ...and its ground (lawn, paving) stays off it.
            foreach (var s in host.Surfaces)
            {
                var ring2 = Poly.To2D(s.Ring);
                var common = Poly.Boolean(new[] { (IReadOnlyList<float2>)ring2 }, shops.Select(q => (IReadOnlyList<float2>)q), BoolOp.Intersection);
                float area = common.Sum(g => Poly.Area(g.Outer) - g.Holes.Sum(h => Poly.Area(h)));
                Assert.True(area < 2f, $"{s.Role} surface covers {area:0.0} m² of shop land");
            }
        }

        [Theory]
        [InlineData("park")]
        [InlineData("plaza")]
        public void NoneMeansNoShops(string hostId)
        {
            var ring = TestKit.Rect(Side, Side);
            var site = Streets(ring);
            var plan = Compose(hostId, StreetShops.Row, StreetShops.None, ring, site);
            var host = plan.Layers.First(l => l.GeneratorId == hostId);
            Assert.Empty(ShopRegions(host));
            Assert.Empty(plan.Layers.SelectMany(l => l.Zoning));
            Assert.DoesNotContain(host.Nets, n => n.Role == NetRole.Alley);
        }

        [Fact]
        public void MoreShopsZoneMoreLand()
        {
            var ring = TestKit.Rect(Side, Side);
            var site = Streets(ring);
            double Area(string layout, string amount) => ShopRegions(Compose("park", layout, amount, ring, site).Layers.First(l => l.GeneratorId == "park")).Sum(q => (double)Poly.Area(q));
            foreach (var layout in new[] { StreetShops.Row, StreetShops.Speckled })
                Assert.True(Area(layout, StreetShops.Few) < Area(layout, StreetShops.Lots), layout);
            Assert.True(Area(StreetShops.Speckled, StreetShops.Some) < Area(StreetShops.Row, StreetShops.Lots));
        }

        [Theory]
        [InlineData("band")]
        [InlineData("pads")]
        [InlineData("retiredValue")]
        [InlineData("clusters")]
        [InlineData("inward")]
        [InlineData("lane")]
        public void OldSavedLayoutsStillPlan(string saved)
        {
            // Pre-s12 recipes have shopLayout band|pads and no shopAmount; anything unknown plans as a row.
            var ring = TestKit.Rect(200, 150);
            var site = TestKit.RoadAlongBottom(200);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 11);
            Assert.True(Editor.SetFeature(r, "0", LayerBase.StreetShopsLayer, true, 3));
            r.Root.Settings.Values.Remove(StreetShops.AmountId);
            r.Root.Settings.Values[StreetShops.LayoutId] = saved;
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            Assert.False(plan.HasErrors);
            Assert.NotEmpty(plan.Layers.SelectMany(l => l.Zoning));
            Assert.DoesNotContain(plan.Layers.SelectMany(l => l.Nets), n => n.Role == NetRole.Alley);
            Assert.Equal("band", StreetShops.NormalizeLayout("retiredValue"));
            // s14: retired feature layouts (clusters, facing in, alley) load as Row, in the plan and in the editor.
            var park = (LayerBase)GeneratorRegistry.Get("park");
            string expect = saved == "pads" ? "pads" : "band";
            Assert.Equal(expect, park.Merge(r.Root.Settings).S(StreetShops.LayoutId, ""));
            var loaded = r.Clone();
            Editor.ApplyDefaults(loaded);
            Assert.Equal(expect, loaded.Root.Settings.S(StreetShops.LayoutId, ""));
        }

        [Fact]
        public void AlleyLayoutsFallBackToARowWithoutAStreetToJoin()
        {
            // A street the parent planned (reserved corridor, no existing net): no mid-segment junction possible.
            var ring = TestKit.Rect(Side, Side);
            var input = TestKit.Input(ring);
            input.Reserved.AddCorridor(new float2(-20, -8), new float2(Side + 20, -8), 8f, nameof(NetRole.Street));
            var region = LayerRegion.From(Poly.To2D(ring));
            foreach (var layout in new[] { StreetShops.Inward, StreetShops.Lane })
            {
                var res = StreetShops.Plan(region, input, new List<float2>(), layout, StreetShops.Some, 24f, 5);
                Assert.True(res.FellBack, layout);
                Assert.Empty(res.Alleys);
                Assert.NotEmpty(res.Shops);
            }
        }

        [Fact]
        public void UrbanPlacesStartWithShopsNatureAndMemorialPlacesDoNot()
        {
            bool On(string id) => Features.Of(GeneratorRegistry.Get(id)).Any(f => f.Layer == LayerBase.StreetShopsLayer && f.Target == StreetFrontage.ShopsTag && f.DefaultOn);
            foreach (var id in new[] { "waterfront" }) Assert.True(On(id), id);
            // rf-squares: squares and venues start without street shops (docs/refactor/squares.md).
            foreach (var id in new[] { "plaza", "marketSquare", "amphitheatre", "fairground" }) Assert.False(On(id), id);
            // rf-parks: a park's street shops are off by default (they left empty plots round natural and garden parks); the urban
            // pocket style turns them on.
            foreach (var id in new[] { "park", "natureReserve", "hikingTrail", "quarry", "landfill", "farmstead", "golf", "cemeteryGrounds", "memorialPark", "beach", "wetland", "zenGarden" })
                Assert.False(On(id), id);
            // Every listed default names a type that really hosts the feature.
            foreach (var kv in StreetShops.Defaults)
                Assert.Contains(Features.Of(GeneratorRegistry.Get(kv.Key)), f => f.Layer == LayerBase.StreetShopsLayer && f.Target == StreetFrontage.ShopsTag);
            // The host control defaults follow the table.
            var plaza = GeneratorRegistry.Get("plaza").Defaults();
            Assert.Equal(StreetShops.Row, plaza.S(StreetShops.LayoutId, ""));
            Assert.Equal(StreetShops.Some, plaza.S(StreetShops.AmountId, ""));
            Assert.Equal(StreetShops.Speckled, GeneratorRegistry.Get("park").Defaults().S(StreetShops.LayoutId, ""));
        }
    }
}
