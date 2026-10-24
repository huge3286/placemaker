// s29: a parking lot between existing lanes that sit on their own levels, over a bowl in the ground (playtest 53:
// the asphalt draped into a deep bowl, the lot met the lanes with torn edges, and the build logged "terraform reached
// its target in 0 pass(es)" because 2 % of the texels being off — the bowl — counted as done whatever they were off by).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class LotTieTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        /// <summary>Gentle slope with a 3 m deep bowl in the middle of the site.</summary>
        private static float Ground(float2 p) => 0.03f * p.y + 0.01f * p.x - 3f * math.exp(-math.lengthsq(p - new float2(50, 40)) / (2f * 9f * 9f));

        /// <summary>Streets all round; the south one raised 1.2 m on a fill, the north one cut 0.8 m in, the others at grade.</summary>
        private static SiteContext Site(float3[] ring, out Func<float2, float> roadHeight)
        {
            var site = ZoningCutoutTests.Streets(ring);
            site.HeightAt = Ground;
            var offsets = new Dictionary<long, float>();
            for (int k = 0; k < site.NearbyNets.Count; k++)
            {
                var n = site.NearbyNets[k];
                float mid = 0.5f * (n.A.z + n.D.z);
                float off = math.abs(n.A.z - n.D.z) < 1f ? (mid < 40f ? 1.2f : -0.8f) : 0f;
                offsets[n.ExternalId] = off;
                n.A.y = Ground(n.A.xz) + off; n.B.y = Ground(n.B.xz) + off; n.C.y = Ground(n.C.xz) + off; n.D.y = Ground(n.D.xz) + off;
                site.NearbyNets[k] = n;
            }
            var nets = site.NearbyNets.ToList();
            roadHeight = p =>
            {
                float best = float.MaxValue, h = float.NaN;
                foreach (var n in nets)
                    for (int i = 0; i < 32; i++)
                    {
                        var a = Composer.Bezier(n.A, n.B, n.C, n.D, i / 32f); var b = Composer.Bezier(n.A, n.B, n.C, n.D, (i + 1) / 32f);
                        var ab = b.xz - a.xz; float t = math.saturate(math.dot(p - a.xz, ab) / math.max(1e-6f, math.lengthsq(ab)));
                        float d = math.distance(p, a.xz + ab * t);
                        if (d < best) { best = d; h = math.lerp(a.y, b.y, t); }
                    }
                return h;
            };
            return site;
        }

        private static ComposedPlan Compose(float3[] ring, SiteContext site)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("parking"), 5);
            var input = new AreaInput { Shape = new AreaShape { Ring = ring }, Site = site, Settings = r.Root.Settings, Markers = r.Root.Markers };
            r.Root.Markers.AddRange(GeneratorRegistry.Get("parking").SuggestMarkers(input));
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        [Fact]
        public void APassCheckNeverCallsADeepHoleDone()
        {
            // A level 100 × 100 m target over ground with a 3 m bowl covering ~1 % of it.
            var ring = new[] { new float2(0, 0), new float2(100, 0), new float2(100, 100), new float2(0, 100) };
            var raster = TerraformRaster.Build(new List<(float2[], float, int)> { (ring, 0f, 0) }, 1.75f, 4f);
            Func<float2, float> live = p => math.distance(p, new float2(50, 50)) < 5f ? -3f : 0f;
            raster.Deltas(live, 0.3f, out var stats);
            Assert.True(stats.InsideOff > 0 && stats.InsideOff < 0.02f * stats.Inside, $"{stats.InsideOff}/{stats.Inside}");
            Assert.False(stats.Done(0.3f, 0.02f), "a 3 m hole is not done");
            // A few texels just over the tolerance (raster noise along an edge) still are.
            Func<float2, float> noisy = p => math.distance(p, new float2(50, 50)) < 5f ? -0.5f : 0f;
            raster.Deltas(noisy, 0.3f, out stats);
            Assert.True(stats.Done(0.3f, 0.02f));
            // And the live ground is what is measured: once the hole is filled it is done.
            raster.Deltas(_ => 0f, 0.3f, out stats);
            Assert.True(stats.Done(0.3f, 0.02f) && stats.InsideMaxAbs < 1e-4f);
        }

        [Fact]
        public void TheLotTiesIntoTheRoadsAndFillsTheBowl()
        {
            var ring = TestKit.Rect(100, 80);
            var site = Site(ring, out var roadHeight);
            var plan = Compose(ring, site);
            var lot = plan.Layers.First(l => l.GeneratorId == "parking");
            Assert.False(plan.HasErrors);
            Assert.NotEmpty(lot.Terraform);
            Assert.True(lot.Facts["gradeTies"] > 50, "the outline along the streets is tied");
            var final = LotGradingTests.Final(plan, Ground);
            var outline = Poly.To2D(ring);

            // Edges: the outline meets the road beside it at the road's height (±0.3 m).
            for (int e = 0; e < outline.Length; e++)
            {
                var a = outline[e]; var b = outline[(e + 1) % outline.Length];
                var inward = math.normalizesafe(new float2(-(b - a).y, (b - a).x));
                for (float s = 0.1f; s < 0.9f; s += 0.05f)
                {
                    var p = math.lerp(a, b, s) + inward * 0.3f;
                    float d = final(p) - roadHeight(p);
                    Assert.True(math.abs(d) <= 0.3f, $"edge at {p}: ground {final(p):0.00}, road {roadHeight(p):0.00}");
                }
            }

            // No bowl: no point of the lot is a local low (planned levels are planes and linear ramps).
            var reg = LayerRegion.From(outline);
            for (float y = 3f; y < 78f; y += 2.5f)
                for (float x = 3f; x < 98f; x += 2.5f)
                {
                    var p = new float2(x, y);
                    if (reg.BoundaryDistance(p, 4f) < 3f) continue;
                    float h = final(p), low = float.MaxValue;
                    for (int k = 0; k < 8; k++) { float t = k * math.PI / 4f; low = math.min(low, final(p + 3f * new float2(math.cos(t), math.sin(t)))); }
                    Assert.False(h < low - 0.05f, $"a hollow at {p} ({low - h:0.00} m below its surroundings)");
                }
            // The bowl in the ground is filled (its centre comes up to the lot, not the lot down to it).
            Assert.True(final(new float2(50, 40)) - Ground(new float2(50, 40)) > 2f);

            // Every stall stands on the graded ground; no fence anywhere on a parking lot.
            foreach (var o in lot.Objects.Where(o => o.Role == ObjectRole.ParkingBay))
                Assert.True(math.abs(o.Position.y - final(o.Position.xz)) < 0.3f, $"stall at {o.Position.xz}");
            Assert.DoesNotContain(plan.Layers.SelectMany(l => l.Nets), n => n.Role == NetRole.Fence);
            Assert.DoesNotContain(plan.Layers.SelectMany(l => l.Objects), o => o.Role == ObjectRole.FencePiece);
        }
    }
}
