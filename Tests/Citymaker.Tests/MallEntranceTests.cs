// s47: the same for every type with MallKit lanes (s41 did the strip mall).
// s41: where a strip mall's lane leaves the existing street (screenshots 71/72: "Pine Lane" off Chester Lane, "Rosewood
// Lane" off Caraway Street) the ground dipped into a bowl: the lane's attached end was pinned to the raw terrain under a
// street on a fill or in a cut, and the strip between the site outline and the kerb was never graded. The lane must leave
// the street flush and ramp (≤ 8 %) to the lot; nowhere a hollow; no fence across the lane.

using System;
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
    public class MallEntranceTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        public static IEnumerable<object[]> Cases()
        {
            foreach (var gen in new[] { "shoppingPlaza", "outdoorMall", "mallPark", "mallDistrict", "retailPark", "highStreet", "shopsPark" })
            {
                var presets = new List<string> { "" };
                presets.AddRange(GeneratorRegistry.Get(gen).Presets.Select(p => p.Id));
                foreach (var p in presets)
                    foreach (var g in new[] { "fill", "cut", "cross", "steep", "gentle" })
                        yield return new object[] { gen, p, g };
            }
        }

        /// <summary>Flat ground with the streets on a 1.2 m fill / in a 1 m cut, or the s25 6 % cross slope with streets at grade.</summary>
        private static (SiteContext site, Func<float2, float> ground) Site(float3[] ring, string kind)
        {
            var site = ZoningCutoutTests.Streets(ring);
            Func<float2, float> g = kind == "cross" || kind == "steep" || kind == "gentle" ? LotGradingTests.Ground(kind) : (_ => 0f);
            float off = kind == "fill" ? 1.2f : kind == "cut" ? -1f : 0f;
            site.HeightAt = g;
            for (int k = 0; k < site.NearbyNets.Count; k++)
            {
                var n = site.NearbyNets[k];
                n.A.y = g(n.A.xz) + off; n.B.y = g(n.B.xz) + off; n.C.y = g(n.C.xz) + off; n.D.y = g(n.D.xz) + off;
                site.NearbyNets[k] = n;
            }
            return (site, g);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void TheLaneLeavesTheStreetFlushAndRampsWithoutAHollow(string gen, string preset, string kind)
        {
            var ring = TestKit.Rect(140, 130);
            var (site, ground) = Site(ring, kind);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(gen), 5);
            if (preset != "") Editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint q = 0;
            Editor.AddDefaultFeatures(r, () => ++q);
            // (rf-shops3) Entrance types: a car entrance mid main street and a driveway (a real junction: a lane leaves the street).
            ShopTestEntrances.Place(r, ring, site, driveway: true);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            string ctx = $"{gen}/{preset}/{kind}";
            Assert.False(plan.HasErrors, ctx);
            var final = LotGradingTests.Final(plan, ground);
            var host = plan.Layers.First(l => l.GeneratorId == gen);
            var lanes = host.Nets.Where(n => n.Role == NetRole.Alley || n.Role == NetRole.Street).ToList();
            var region = LayerRegion.From(Poly.To2D(ring));
            var corridors = new List<float2[]>();

            foreach (var n in lanes)
                for (int end = 0; end < 2; end++)
                {
                    long id = end == 0 ? n.StartAttach : n.EndAttach;
                    if (id < 0) continue;
                    var street = site.NearbyNets.First(s => s.ExternalId == id);
                    float t = end == 0 ? n.StartAttachT : n.EndAttachT;
                    float hs = Composer.Bezier(street.A, street.B, street.C, street.D, t).y;
                    var p0 = end == 0 ? n.A.xz : n.D.xz;
                    var p1 = end == 0 ? n.D.xz : n.A.xz;
                    var d = math.normalizesafe(p1 - p0);
                    float edge = street.Width * 0.5f;
                    // Flush at the kerb: the lane meets the street node at the street's own height.
                    float atKerb = final(p0 + d * (edge + 0.5f));
                    Assert.True(math.abs(atKerb - hs) <= 0.3f, $"{ctx}: lane at the kerb {atKerb:0.00} m, street {hs:0.00} m");
                    // Then one smooth ramp, ≤ 8 % (+ raster tolerance), over the stub (the first 15 m; beyond, the lane runs along the lot:
                    // LotGradingTests).
                    float len = math.min(math.distance(p0, p1) - 1f, edge + 15f);
                    for (float s = edge + 0.5f; s + 2f <= len; s += 1f)
                    {
                        float g0 = final(p0 + d * s), g1 = final(p0 + d * (s + 2f));
                        Assert.True(math.abs(g1 - g0) / 2f <= 0.12f, $"{ctx}: lane {s - edge:0} m from the kerb climbs {math.abs(g1 - g0) / 2f:P0}");
                    }
                    var nrm = new float2(-d.y, d.x);
                    corridors.Add(new[] { p0 + d * edge - nrm * 4.5f, p0 + d * len - nrm * 4.5f, p0 + d * len + nrm * 4.5f, p0 + d * edge + nrm * 4.5f });
                }
            if (gen != "retailPark" && gen != "shopsPark") Assert.NotEmpty(corridors);   // those two plan no lane off the street

            // No hollow anywhere on the site or in the lanes' approaches (3 m neighbourhood). 22 cm: the sculpt is emitted as sloped quads merged within 4-12 cm on a 1.75 m raster with a 4 m bank, so a street corner on a fill keeps up to ~0.2 m of unevenness (s58: 0.11-0.20 m measured; the build's pit guard acts at 0.35 m, a real bowl is metres).
            bool Covered(float2 p) => region.Contains(p) || corridors.Any(c => Poly.Contains(c, p));
            for (float y = -6f; y < 134f; y += 2f)
                for (float x = -6f; x < 146f; x += 2f)
                {
                    var p = new float2(x, y);
                    if (!Covered(p)) continue;
                    bool streetNear = false;
                    foreach (var s in site.NearbyNets)
                        if (math.sqrt(SegmentIndex.DistSq(p, s.A.xz, s.D.xz)) < s.Width * 0.5f + 0.5f) { streetNear = true; break; }
                    if (streetNear) continue;
                    float h = final(p), low = float.MaxValue;
                    for (int k = 0; k < 8; k++) { float a = k * math.PI / 4f; low = math.min(low, final(p + 3f * new float2(math.cos(a), math.sin(a)))); }
                    Assert.False(h < low - 0.22f, $"{ctx}: a hollow at {p} ({low - h:0.00} m below its surroundings)");
                }

            // No fence across or along a lane (a fence piece within the lane's width + 0.5 m).
            foreach (var l in plan.Layers.Where(l => l != null))
            {
                foreach (var f in l.Nets.Where(n => n.Role == NetRole.Fence))
                    for (int i = 0; i <= 8; i++)
                    {
                        var p = Composer.Bezier(f.A, f.B, f.C, f.D, i / 8f).xz;
                        foreach (var n in lanes)
                            Assert.False(math.sqrt(SegmentIndex.DistSq(p, n.A.xz, n.D.xz)) < 4.5f, $"{ctx}: fence across the lane at {p}");
                    }
                foreach (var o in l.Objects.Where(o => o.Role == ObjectRole.FencePiece))
                    foreach (var n in lanes)
                        Assert.False(math.sqrt(SegmentIndex.DistSq(o.Position.xz, n.A.xz, n.D.xz)) < 4.5f, $"{ctx}: fence piece on the lane at {o.Position.xz}");
            }
        }
    }
}
