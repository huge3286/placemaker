// s40: zoning first. Playtest (image 70): shop plots and the cells the game zones did not match — paving notches, grass
// leftovers, cells poking out of plots, plots without cells. Now every street plot IS the cells the build zones for it
// (Build/ZoneLots → ZoneGuard), so: plot region == union of its zoned cells, no zoned cell outside a plot, no host
// content on a cell. Checked for every type hosting the Zoning feature and every commercial preset, on straight,
// angled and curved sites, with the streets' blocks captured (as in the game).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class ZoningFirstTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        /// <summary>Streets around the ring with their zone blocks "captured" (predicted like the game makes them).</summary>
        internal static SiteContext Captured(float3[] ring)
        {
            var site = ZoningCutoutTests.Streets(ring);
            foreach (var g in ZoneLots.SiteGrids(site, null))
            {
                var c = g;
                c.Predicted = false; c.Existing = false;
                site.ZoneGrids.Add(c);
            }
            return site;
        }

        private static float3[] Rotate(float3[] ring, float deg)
        {
            float a = math.radians(deg), cs = math.cos(a), sn = math.sin(a);
            return ring.Select(p => new float3(p.x * cs - p.z * sn, 0, p.x * sn + p.z * cs)).ToArray();
        }

        public static float3[] Site(string kind) => kind switch
        {
            "angled" => Rotate(TestKit.Rect(140, 120), 23f),
            _ => TestKit.Rect(140, 120),
        };

        private static float2 Centre(ZoneCellMark c) => 0.25f * (c.A + c.B + c.C + c.D);

        /// <summary>The checks: every street plot is exactly its zoned cells; every zoned cell lies in a plot; nothing of the host on a cell.</summary>
        private static void AssertPlotsAreCells(ComposedPlan plan, string host, string ctx, bool lanePlotsExempt, float3[] siteRing)
        {
            var hostPlan = plan.Layers.First(l => l.GeneratorId == host);
            var lanes = plan.Layers.SelectMany(l => l.Nets).Where(n => ZoneBlockPredictor.Zones(n.Role)).ToList();
            bool FacesLane(float2[] q) => lanes.Any(n => q.Any(p => Enumerable.Range(0, 9).Any(i => math.distance(Composer.Bezier(n.A, n.B, n.C, n.D, i / 8f).xz, p) < 10f)));
            var plots = plan.Layers.SelectMany(l => l.Regions).Where(Cutouts.IsCutout).Select(g => Poly.To2D(g.Ring)).ToList();
            var zoned = plan.ZoneCells.Where(c => c.State == ZoneCellState.Zoned).ToList();
            Assert.NotEmpty(plots);
            bool padded = plan.AllIssues().Any(i => i.Key == Citymaker.Core.Layers.Commercial.CommercialLayer.PadKey);
            var cellPlots = plan.Layers.SelectMany(l => l.Regions).Where(g => Cutouts.IsCutout(g) && g.Tags.Contains(ZoneLots.CellsTag)).Select(g => Poly.Centroid(Poly.To2D(g.Ring))).ToList();
            foreach (var q in plots)
            {
                if (lanePlotsExempt && FacesLane(q)) continue;
                // (s46) A plot no cell could be zoned for stays a shop pad (warned): not cells by design.
                if (padded && !cellPlots.Any(c => math.distance(c, Poly.Centroid(q)) < 0.5f)) continue;
                var mine = zoned.Where(c => Poly.Contains(q, Centre(c))).ToList();
                // (s58) A plot the guard reported as not zonable (ZoneGuard.NotZonableKey, its quad dropped) keeps its cutout but has no cells: warned, not a defect.
                if (mine.Count == 0 && plan.AllIssues().Any(i => i.Key == ZoneGuard.NotZonableKey && math.distance(i.At.Value.xz, Poly.Centroid(q)) < 14f)) continue;
                Assert.True(mine.Count >= 2, $"{ctx}: plot at {Poly.Centroid(q)} has {mine.Count} cells");
                float cells = mine.Count * ZoneGuard.Cell * ZoneGuard.Cell;
                Assert.True(math.abs(cells - Poly.Area(q)) < 1f, $"{ctx}: plot at {Poly.Centroid(q)} is {Poly.Area(q):0} m², its cells {cells:0} m²");
                // Every corner of every cell inside the plot (cells never poke out).
                foreach (var c in mine)
                    foreach (var p in new[] { c.A, c.B, c.C, c.D })
                        Assert.True(Poly.Contains(q, p) || Poly.DistanceToBoundary(q, p) < 0.05f, $"{ctx}: cell at {Centre(c)} pokes out of its plot");
            }
            AssertSnug(plots, lanePlotsExempt ? FacesLane : (Func<float2[], bool>)(_ => false), plan, ctx, Poly.To2D(siteRing));
            // No zoned cell outside the plots.
            // (Plots facing a lane the build adds keep their rows — their cells are snapped by ZoneGuard at the end and may
            // reach a little past them.)
            foreach (var c in zoned)
                Assert.True(plots.Any(q => Poly.Contains(q, Centre(c)) || (lanePlotsExempt && FacesLane(q) && Poly.DistanceToBoundary(q, Centre(c)) < 4.5f)),
                    $"{ctx}: zoned cell at {Centre(c)} lies in no plot");
            // Nothing of the host on a zoned cell (decals may lie anywhere; objects of other layers are cleared too) but the plot planting
            // (rf-shops3: trees and shrubs at the back of a plot, tree.plot / bush.plot; the growing building clears what is in its way).
            foreach (var l in plan.Layers.Where(l => l.GeneratorId != Cutouts.ZoningLayer))
            {
                foreach (var o in l.Objects.Where(o => o.Role != ObjectRole.Decal && !Cutouts.KeepsOnPlot(o)))
                    Assert.False(zoned.Any(c => Poly.Contains(Shrink(new[] { c.A, c.B, c.C, c.D }, 0.3f), o.Position.xz)), $"{ctx}: {l.GeneratorId} {o.Role} on a zoned cell at {o.Position.xz}");
                foreach (var n in l.Nets.Where(n => !ZoneBlockPredictor.Zones(n.Role)))
                    for (int i = 0; i <= 16; i++)
                    {
                        var p = Composer.Bezier(n.A, n.B, n.C, n.D, i / 16f).xz;
                        Assert.False(zoned.Any(c => Poly.Contains(Shrink(new[] { c.A, c.B, c.C, c.D }, 0.5f), p)), $"{ctx}: {l.GeneratorId} {n.Role} crosses a zoned cell at {p}");
                    }
            }
        }

        /// <summary>s48: host surfaces and paths meet the cell blocks flush: where the host has content within 3 m of a plot side, it is within 0.5 m.</summary>
        private static void AssertSnug(List<float2[]> plots, Func<float2[], bool> facesLane, ComposedPlan plan, string ctx, float2[] site)
        {
            var rings = plan.Layers.Where(l => l.GeneratorId != Cutouts.ZoningLayer)
                .SelectMany(l => l.Surfaces.Where(s => s.Role != SurfaceRole.Water).Select(s => Poly.To2D(s.Ring))).ToList();
            var paths = plan.Layers.Where(l => l.GeneratorId != Cutouts.ZoningLayer)
                .SelectMany(l => l.Nets.Where(n => n.Role == NetRole.Pathway || n.Role == NetRole.PathwayWide)).ToList();
            // A surface must start within 0.5 m of the cell edge; a walk (modelled 1 m / 1.5 m half-width) within 1 m: its dead end keeps ~1.5 m
            // centre-to-cell, the least the game accepts before a walk edge closes the cell column.
            float Near(float2 p, bool walks)
            {
                float d = 99f;
                if (!walks) foreach (var r in rings) d = math.min(d, Poly.Contains(r, p) ? 0f : Poly.DistanceToBoundary(r, p));
                else foreach (var n in paths)
                    for (int i = 0; i <= 16; i++)
                        d = math.min(d, math.distance(Composer.Bezier(n.A, n.B, n.C, n.D, i / 16f).xz, p) - (n.Role == NetRole.PathwayWide ? 1.5f : 1.0f));
                return math.max(0f, d);
            }
            foreach (var q0 in plots)
            {
                if (facesLane(q0)) continue;
                if (!plan.ZoneCells.Any(c => c.State == ZoneCellState.Zoned && Poly.Contains(q0, 0.25f * (c.A + c.B + c.C + c.D)))) continue;   // s58: a reported not-zonable plot has no cell block to be snug to
                var q = Poly.EnsureCcw((float2[])q0.Clone());
                for (int e = 0; e < q.Length; e++)
                {
                    var a = q[e]; var b = q[(e + 1) % q.Length];
                    var n = math.normalizesafe(new float2(b.y - a.y, -(b.x - a.x)));   // outward of a CCW ring
                    for (int k = 1; k < 4; k++)
                    {
                        var p = math.lerp(a, b, k / 4f) + n * 0.05f;
                        if (!Poly.Contains(site, p) || plots.Any(o => !ReferenceEquals(o, q0) && Poly.Contains(o, p))) continue;   // street side / a neighbour plot
                        // The first host content straight out from the edge: none within 3 m = nothing there to be snug to.
                        float d = -1f;
                        for (float t = 0f; t <= 3f; t += 0.1f) if (Near(p + n * t, false) <= 0.05f) { d = t; break; }
                        float dw = -1f;
                        for (float t = 0f; t <= 3f; t += 0.1f) if (Near(p + n * t, true) <= 0.05f) { dw = t; break; }
                        Assert.True(dw <= 1.0f, $"{ctx}: a walk starts {dw:0.0} m from the cell block edge at {p} (leftover strip)");
                        Assert.True(d <= 0.5f, $"{ctx}: host content starts {d:0.0} m from the cell block edge at {p} (leftover strip)");
                    }
                }
            }
        }

        private static float2[] Shrink(float2[] q, float by)
        {
            var c = Poly.Centroid(q);
            return q.Select(p => p + math.normalizesafe(c - p) * by).ToArray();
        }

        public static IEnumerable<object[]> FeatureCases()
        {
            foreach (var h in new[] { "plaza", "marketSquare", "park" })
                foreach (var s in new[] { "straight", "angled" })
                    yield return new object[] { h, s };
        }

        [Theory]
        [MemberData(nameof(FeatureCases))]
        public void Zoning_feature_plots_are_exactly_their_cells(string host, string siteKind)
        {
            var ring = Site(siteKind);
            var site = Captured(ring);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(host), 9);
            Assert.True(Editor.SetFeature(r, "0", LayerBase.StreetShopsLayer, true, 31) || r.Root.Children.Any(c => c.Layer == LayerBase.StreetShopsLayer));
            r.Root.Settings.Values[StreetShops.LayoutId] = StreetShops.Row;
            r.Root.Settings.Values[StreetShops.AmountId] = StreetShops.Some;
            TestKit.Entered(r, ring, site);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            AssertPlotsAreCells(plan, host, $"{host}/{siteKind}", false, ring);
            Assert.DoesNotContain(plan.AllIssues(), i => i.Key == ZoneGuard.NotZonableKey);
        }

        public static IEnumerable<object[]> CommercialCases()
        {
            foreach (var t in CommercialTests.Types)
                foreach (var p in new[] { "" }.Concat(GeneratorRegistry.Get(t).Presets.Select(x => x.Id)))
                    yield return new object[] { t, p };
        }

        [Theory]
        [MemberData(nameof(CommercialCases))]
        public void Commercial_street_plots_are_exactly_their_cells(string type, string preset)
        {
            var ring = TestKit.Rect(160, 160);
            var site = Captured(ring);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(type), 5);
            if (preset != "") Editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            ShopTestEntrances.Place(r, ring, site, footpath: true, driveway: true);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            if (!plan.Layers.SelectMany(l => l.Regions).Any(Cutouts.IsCutout)) return;   // a layout without shops on this site
            AssertPlotsAreCells(plan, type, $"{type}/{preset}", true, ring);
        }

        [Fact]
        public void Curved_street_plaza_plots_are_exactly_their_cells()
        {
            // A plaza on the outer side of a curved four-lane street (R 320 m; the outline follows it in 24 m chords).
            float R = 320f;
            var ring = new List<float3>();
            for (float a = -16f; a <= 16.01f; a += 4f)
            {
                var p = new float2(math.cos(math.radians(a)), math.sin(math.radians(a))) * (R + 13f);
                ring.Add(new float3(p.x, 0, p.y));
            }
            ring.Add(new float3((R + 110f) * math.cos(math.radians(16f)), 0, (R + 110f) * math.sin(math.radians(16f))));
            ring.Add(new float3((R + 110f) * math.cos(math.radians(-16f)), 0, (R + 110f) * math.sin(math.radians(-16f))));
            var rr = ring.ToArray();
            if (!Poly.IsCcw(Poly.To2D(rr))) System.Array.Reverse(rr);
            var site = new SiteContext();
            for (int i = 0; i < 2; i++)
            {
                float a0 = -20f + 20f * i, a1 = a0 + 20f;
                float k = 4f / 3f * math.tan(math.radians(20f) / 4f) * R;
                float2 p0 = new float2(math.cos(math.radians(a0)), math.sin(math.radians(a0))) * R, p3 = new float2(math.cos(math.radians(a1)), math.sin(math.radians(a1))) * R;
                float2 t0 = new float2(-math.sin(math.radians(a0)), math.cos(math.radians(a0))), t1 = new float2(-math.sin(math.radians(a1)), math.cos(math.radians(a1)));
                site.NearbyNets.Add(new NearbyNet { ExternalId = 800 + i, A = new float3(p0.x, 0, p0.y), B = new float3((p0 + t0 * k).x, 0, (p0 + t0 * k).y), C = new float3((p3 - t1 * k).x, 0, (p3 - t1 * k).y), D = new float3(p3.x, 0, p3.y), Width = 24f, Category = NetCategory.Road, HasSidewalk = true });
            }
            foreach (var g in ZoneLots.SiteGrids(site, null)) { var c = g; c.Predicted = false; c.Existing = false; site.ZoneGrids.Add(c); }
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("plaza"), 9);
            Assert.True(Editor.SetFeature(r, "0", LayerBase.StreetShopsLayer, true, 31) || r.Root.Children.Any(c => c.Layer == LayerBase.StreetShopsLayer));
            r.Root.Settings.Values[StreetShops.LayoutId] = StreetShops.Row;
            r.Root.Settings.Values[StreetShops.AmountId] = StreetShops.Some;
            TestKit.Entered(r, rr, site);
            var plan = Composer.Compose(r, new AreaShape { Ring = rr, Source = AreaSource.Drawn }, site, CancellationToken.None);
            AssertPlotsAreCells(plan, "plaza", "plaza/curved", false, rr);
        }
    }
}

