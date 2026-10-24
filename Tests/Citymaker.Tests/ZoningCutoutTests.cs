// s14: the Zoning feature is plain cutouts — land along the street left empty for zoned cells. Nothing
// of any layer (the host, any of its features) may stand on, run over or pave a cutout (playtest: picnic
// tables and a tent prop sat in the zoning strip).

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
    public class ZoningCutoutTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);
        private const float Side = 130f;

        internal static SiteContext Streets(float3[] ring)
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

        /// <summary>Every type offering the Zoning feature.</summary>
        public static IEnumerable<object[]> Hosts() =>
            GeneratorRegistry.All.Where(g => !GeneratorRegistry.IsHidden(g)
                    && Features.Of(g).Any(f => f.Layer == LayerBase.StreetShopsLayer && f.Target == StreetFrontage.ShopsTag))
                .Select(g => new object[] { g.Id });

        private static float2[] Inset(float2[] q, float by)
        {
            var c = Poly.Centroid(q);
            return q.Select(p => p + math.normalizesafe(c - p) * by).ToArray();
        }

        /// <summary>
        /// Composes <paramref name="host"/> with the Zoning feature (row, lots) and <paramref name="others"/> turned on,
        /// then checks that nothing but the zoning itself touches a cutout.
        /// </summary>
        private static void AssertCutoutsEmpty(string host, IEnumerable<string> others, string layout)
        {
            var ring = TestKit.Rect(Side, Side);
            var site = Streets(ring);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(host), 9);
            Assert.True(Editor.SetFeature(r, "0", LayerBase.StreetShopsLayer, true, 31) || Features.ChildIndex(r.Root, Features.Find(GeneratorRegistry.Get(host), LayerBase.StreetShopsLayer)) >= 0);
            uint seed = 40;
            foreach (var f in others) Editor.SetFeature(r, "0", f, true, seed++);
            r.Root.Settings.Values[StreetShops.LayoutId] = layout;
            r.Root.Settings.Values[StreetShops.AmountId] = StreetShops.Lots;
            TestKit.Entered(r, ring, site);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            var hostPlan = plan.Layers.First(l => l.GeneratorId == host);
            var cut = hostPlan.Regions.Where(g => g.Tags.Contains(StreetFrontage.ShopsTag)).Select(g => Inset(Poly.To2D(g.Ring), 0.25f)).ToList();
            if (cut.Count == 0) return;   // a type that leaves no frontage (e.g. its own layout needs it) has nothing to keep empty
            string ctx = host + " +" + string.Join(",", others) + " " + layout;
            foreach (var l in plan.Layers.Where(l => l.GeneratorId != LayerBase.StreetShopsLayer))
            {
                foreach (var o in l.Objects)
                {
                    float rr = o.Role == ObjectRole.Tree ? 1.5f : o.Role == ObjectRole.Decal ? 0f : 0.3f;
                    Assert.False(cut.Any(q => Poly.Contains(q, o.Position.xz) || (rr > 0f && Poly.DistanceToBoundary(q, o.Position.xz) < rr)),
                        $"{ctx}: {l.GeneratorId} {o.Role}/{o.Category} {o.Asset.Id} at {o.Position.xz} is in a zoning cutout");
                }
                foreach (var n in l.Nets)
                    for (int i = 0; i <= 24; i++)
                    {
                        var p = Composer.Bezier(n.A, n.B, n.C, n.D, i / 24f).xz;
                        Assert.False(cut.Any(q => Poly.Contains(q, p)), $"{ctx}: {l.GeneratorId} {n.Role} runs through a zoning cutout at {p}");
                    }
                foreach (var s in l.Surfaces.Select(s => s.Ring).Concat(l.Areas.Select(a => a.Ring)))
                {
                    var common = Poly.Boolean(new[] { (IReadOnlyList<float2>)Poly.To2D(s) }, cut.Select(q => (IReadOnlyList<float2>)q), BoolOp.Intersection);
                    float area = common.Sum(g => Poly.Area(g.Outer) - g.Holes.Sum(h => Poly.Area(h)));
                    Assert.True(area < 2f, $"{ctx}: {l.GeneratorId} ground covers {area:0.0} m² of a zoning cutout");
                }
            }
        }

        [Theory]
        [MemberData(nameof(Hosts))]
        public void NothingOfAnyFeatureLandsInACutout(string host)
        {
            var gen = GeneratorRegistry.Get(host);
            var others = Features.Of(gen).Where(f => f.Layer != LayerBase.StreetShopsLayer).Select(f => f.Layer).Distinct().ToList();
            // Each feature alone (its own placement rules), then all of them at once (competing for room).
            foreach (var f in others) AssertCutoutsEmpty(host, new[] { f }, StreetShops.Row);
            AssertCutoutsEmpty(host, others, StreetShops.Row);
            AssertCutoutsEmpty(host, others, StreetShops.Speckled);
        }
    }
}
