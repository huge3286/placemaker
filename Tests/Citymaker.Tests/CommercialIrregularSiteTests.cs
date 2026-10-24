// s46: playtest — a strip mall on an irregular ~3 ha block (curved and angled streets) planned almost only a park.
// Every Commercial type x preset on such a block, with the streets' zone blocks captured (as in the game): the layout
// must stay a commercial one (plots + lot + lanes carry most of the block), not collapse to a park.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Layers.Commercial;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests
{
    public class CommercialIrregularSiteTests
    {
        private readonly ITestOutputHelper _out;
        public CommercialIrregularSiteTests(ITestOutputHelper o) { _out = o; }
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static NearbyNet Net(long id, float2 a, float2 b, float2 c, float2 d, float w) => new NearbyNet
        {
            ExternalId = id, A = new float3(a.x, 0, a.y), B = new float3(b.x, 0, b.y), C = new float3(c.x, 0, c.y), D = new float3(d.x, 0, d.y),
            Width = w, Category = NetCategory.Road, HasSidewalk = true,
        };

        private static float2 Pol(float2 c, float r, float deg) => c + new float2(math.cos(math.radians(deg)), math.sin(math.radians(deg))) * r;

        /// <summary>Circular-arc street as cubic pieces (about 25 degrees each).</summary>
        private static void Arc(SiteContext site, ref long id, float2 c, float r, float a0, float a1, float w)
        {
            int n = math.max(1, (int)math.ceil(math.abs(a1 - a0) / 25f));
            for (int i = 0; i < n; i++)
            {
                float s = math.lerp(a0, a1, (float)i / n), e = math.lerp(a0, a1, (float)(i + 1) / n);
                float k = 4f / 3f * math.tan(math.radians(e - s) / 4f) * r;
                float2 p0 = Pol(c, r, s), p3 = Pol(c, r, e);
                float2 t0 = new float2(-math.sin(math.radians(s)), math.cos(math.radians(s))), t1 = new float2(-math.sin(math.radians(e)), math.cos(math.radians(e)));
                site.NearbyNets.Add(Net(id++, p0, p0 + t0 * k, p3 - t1 * k, p3, w));
            }
        }

        private static void Line(SiteContext site, ref long id, float2 a, float2 b, float w) =>
            site.NearbyNets.Add(Net(id++, a, math.lerp(a, b, 1f / 3f), math.lerp(a, b, 2f / 3f), b, w));

        /// <summary>
        /// An irregular block of about 3 ha: an angled straight street on the south and the west, a convex curved street on
        /// the east (R 150) and a concave one on the north (R 190); the outline sits 2 m off the kerbs.
        /// </summary>
        public static float3[] Block(out SiteContext site, float step = 6f)
        {
            site = new SiteContext();
            long id = 900;
            float w = 12f, off = w * 0.5f + 2f;
            // Street centre lines (counter-clockwise round the block): south, east arc, north arc, west.
            float2 sw = new float2(-100, -70), se = new float2(5, -88);
            float2 c1 = new float2(-60, 0); float r1 = 150f;              // east arc: centre west of the block, convex outwards
            float2 c2 = new float2(30, 250f); float r2 = 190f;            // north arc: centre far north, concave side faces the block
            float2 nw = new float2(-105, 78);
            Line(site, ref id, sw - math.normalize(se - sw) * 25f, se, w);
            float a0 = math.degrees(math.atan2(se.y - c1.y, se.x - c1.x));
            float2 ne = Pol(c1, r1, 30f);
            Arc(site, ref id, c1, r1, a0, 30f, w);
            // North street from ne towards nw as an arc about c2 (approximated through its end points).
            float aN0 = math.degrees(math.atan2(ne.y - c2.y, ne.x - c2.x)), aN1 = math.degrees(math.atan2(nw.y - c2.y, nw.x - c2.x));
            if (aN1 < aN0) aN1 += 360f;
            // aN0 > aN1 as seen from c2 (the street runs west); use a descending sweep.
            aN1 = math.degrees(math.atan2(nw.y - c2.y, nw.x - c2.x));
            Arc(site, ref id, c2, math.distance(ne, c2), aN0, aN1, w);
            Line(site, ref id, nw, sw - math.normalize(nw - sw) * 25f, w);
            // The outline: inset the street polygon by `off`.
            var pts = new List<float2>();
            pts.Add(sw);
            for (float a = a0; a <= 30.01f; a += step) pts.Add(Pol(c1, r1, a));
            float rn = math.distance(ne, c2);
            int nn = (int)(36f / step); for (int i = 1; i <= nn; i++) pts.Add(Pol(c2, rn, math.lerp(aN0, aN1, (float)i / nn)));
            pts.Add(nw);
            var poly = Poly.EnsureCcw(pts.ToArray());
            var inset = Poly.Offset(poly, off).OrderByDescending(q => Poly.Area(q)).First();
            var ring = inset.Select(p => new float3(p.x, 0, p.y)).ToArray();
            foreach (var g in ZoneLots.SiteGrids(site, null)) { var c = g; c.Predicted = false; c.Existing = false; site.ZoneGrids.Add(c); }
            return ring;
        }

        /// <summary>
        /// A fully curved irregular block (~2.9 ha, like the playtest's between Edgewater and Honeysuckle Street): the
        /// outline is a smooth closed curve sampled every ~<paramref name="spacing"/> m, ringed by streets 8 m outside it
        /// (each a chain of cubic pieces through the curve, 20 m apart).
        /// </summary>
        public static float3[] Round(out SiteContext site, float spacing = 5f)
        {
            site = new SiteContext();
            float R(float t) => 95f + 22f * math.cos(2f * t) + 11f * math.sin(3f * t + 0.6f);
            float2 P(float t, float off) => new float2(math.cos(t), math.sin(t)) * (R(t) + off) + new float2(5f, -4f);
            var ring = new List<float3>();
            float perim = 0f, tt = 0f; float2 prev = P(0, 0);
            for (int i = 1; i <= 720; i++) { float t = i * math.PI * 2f / 720f; var q = P(t, 0); perim += math.distance(prev, q); prev = q; }
            int n = (int)(perim / spacing);
            for (int i = 0; i < n; i++) { var p = P(i * math.PI * 2f / n, 0); ring.Add(new float3(p.x, 0, p.y)); }
            long id = 950;
            int pieces = 24;
            float2 Tan(float t) => (P(t + 0.001f, 8f) - P(t - 0.001f, 8f)) / 0.002f;
            for (int i = 0; i < pieces; i++)
            {
                float t0 = i * math.PI * 2f / pieces, t1 = (i + 1) * math.PI * 2f / pieces;
                float2 p0 = P(t0, 8f), p3 = P(t1, 8f);
                float dt = t1 - t0;
                site.NearbyNets.Add(Net(id++, p0, p0 + Tan(t0) * dt / 3f, p3 - Tan(t1) * dt / 3f, p3, 12f));
            }
            foreach (var g in ZoneLots.SiteGrids(site, null)) { var c = g; c.Predicted = false; c.Existing = false; site.ZoneGrids.Add(c); }
            return ring.ToArray();
        }

        /// <summary>Site kinds: 6 = half curved (the angled / concave / convex block), 0 = fully curved, -1 = square 2.9 ha, -2 = square 1.5 ha, -3 = square 4 ha.</summary>
        public static readonly float[] SiteKinds = { 6f, 0f, -1f, -2f, -3f };

        public static string SiteName(float kind) => kind == 6f ? "halfcurved" : kind == 0f ? "curved" : kind == -1f ? "sq2.9" : kind == -2f ? "sq1.5" : "sq4";

        public static IEnumerable<object[]> Cases()
        {
            foreach (var t in CommercialTests.Types)
                foreach (var p in new[] { "" }.Concat(GeneratorRegistry.Get(t).Presets.Select(x => x.Id)))
                    foreach (var st in SiteKinds) yield return new object[] { t, p, st };
        }

        private static ComposedPlan Run(string type, string preset, float step, out float area, out SiteContext site)
        {
            float3[] ring;
            if (step < 0f)
            {
                float side = step == -1f ? 170f : step == -2f ? 122f : 200f;
                ring = TestKit.Rect(side, side);
                site = ZoningCutoutTests.Streets(ring);
                foreach (var g in ZoneLots.SiteGrids(site, null)) { var c = g; c.Predicted = false; c.Existing = false; site.ZoneGrids.Add(c); }
            }
            else ring = step == 0f ? Round(out site) : Block(out site, step);
            area = Poly.Area(Poly.To2D(ring));
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(type), 5);
            if (preset != "") Editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint n = 0;
            Editor.AddDefaultFeatures(r, () => ++n);
            // (rf-shops3) What a player places on a block: a car entrance mid main street and a footpath beside it (every gate through a
            // row of shops costs that row a plot; the driveway is exercised by the connectivity tests).
            ShopTestEntrances.Place(r, ring, site, footpath: true);
            return Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
        }

        private static float UnionArea(List<float2[]> rings, float2[] clipTo)
        {
            if (rings.Count == 0) return 0f;
            var union = Poly.UnionAll(rings.Select(q => (IReadOnlyList<float2>)Poly.EnsureCcw(q)));
            var inside = Poly.Boolean(union, new[] { new Region(Poly.EnsureCcw(clipTo)) }, BoolOp.Intersection);
            float a = 0f;
            foreach (var g in inside) { a += Poly.Area(g.Outer); foreach (var h in g.Holes) a -= Poly.Area(h); }
            return a;
        }

        public sealed class Share
        {
            public float Plots, Lots, Lanes, Paved, Commercial, Grass, Edges;
            public int NPlots, Trees;
        }

        /// <summary>
        /// Plots (zoning cutouts) + lots (their whole ring: islands, edges and planted strips are the lot's own) + lane corridors
        /// (8 m), as a union clipped to the site — nothing counted twice — as a share of the block.
        /// </summary>
        public static Share Measure(ComposedPlan plan, float2[] site, bool countPaved = true)
        {
            var host = plan.Layers.First();
            var plots = host.Regions.Where(Cutouts.IsCutout).Select(g => Poly.To2D(g.Ring)).ToList();
            var lots = host.Areas.Where(a => a.Role == AreaRole.Lot).Select(a => Poly.To2D(a.Ring)).ToList();
            var lanes = new List<float2[]>();
            foreach (var n in host.Nets)
                if (n.Role == NetRole.Alley || n.Role == NetRole.Street)
                {
                    float2 prev = n.A.xz;
                    for (int i = 1; i <= 8; i++)
                    {
                        var q = Composer.Bezier(n.A, n.B, n.C, n.D, i / 8f).xz;
                        var d = math.normalizesafe(q - prev); var nr = new float2(-d.y, d.x) * 4f;
                        if (math.distance(prev, q) > 0.01f) lanes.Add(new[] { prev - nr, q - nr, q + nr, prev + nr });
                        prev = q;
                    }
                }
            var s = new Share { NPlots = plots.Count, Trees = host.Objects.Count(o => o.Role == ObjectRole.Tree) };
            s.Plots = UnionArea(plots, site); s.Lots = UnionArea(lots, site); s.Lanes = UnionArea(lanes, site);
            // Paved squares, paseos and forecourts are the shopping area's public realm (pedestrian street, plaza); lawns are not.
            var paved = host.Surfaces.Where(x => countPaved && (x.Role == SurfaceRole.Plaza || x.Role == SurfaceRole.Paving)).Select(x => Poly.To2D(x.Ring)).ToList();
            s.Paved = UnionArea(paved, site);
            var all = new List<float2[]>(plots); all.AddRange(lots); all.AddRange(lanes); all.AddRange(paved);
            s.Commercial = UnionArea(all, site) / Poly.Area(site);
            s.Edges = host.Facts.TryGetValue("lotEdges", out var le) ? (float)le : 0f;
            s.Grass = host.Surfaces.Where(x => x.Role == SurfaceRole.Grass).Sum(x => Poly.Area(Poly.To2D(x.Ring)));
            return s;
        }

        /// <summary>
        /// (shops) The least commercial share (plots, lots, lanes, paved squares) a type must reach on a site. The s50 "lot first" targets
        /// (0.70-0.75) are gone: leftover lots stop at the parking budget (CommercialLayer.LotShare; the user: "the parking lots look
        /// weird") and the rest is planted. What stays is the guard against a composition collapsing into a park or a lawn: every
        /// type keeps at least 12 % of the block commercial (measured after the change: 14-94 %, park-led types lowest).
        /// </summary>
        public static float Target(string type, string preset, float step = 4f) => type == "shopsPark" ? 0.06f : 0.12f;   // shops & park (hidden) is a park with shops

        /// <summary>(shops) Most of the block all lots together may cover: the budget (40 %, retail park 60 %) plus the composition's own lots.</summary>
        /// (shops2) The strip mall is mostly parking (the user: "basically all parking"): rows of big stores round lots.
        public static float LotCap(string type) => type == "retailPark" || type == "stripMall" ? 0.85f : 0.6f;

        [Theory]
        [MemberData(nameof(Cases))]
        public void Block_stays_commercial(string type, string preset, float step)
        {
            var plan = Run(type, preset, step, out float area, out var site);
            var ringXz = Poly.To2D(step < 0f ? TestKit.Rect(step == -1f ? 170f : step == -2f ? 122f : 200f, step == -1f ? 170f : step == -2f ? 122f : 200f) : step == 0f ? Round(out _) : Block(out _, step));
            var m = Measure(plan, ringXz, type != "shopsPark" && type != "mallPark");
            _out.WriteLine($"SHARE {type}/{preset} @ {SiteName(step)}: area {area / 10000f:0.00} ha, plots {m.NPlots} ({m.Plots:0}), lots {m.Lots:0}, lanes {m.Lanes:0}, paved {m.Paved:0}, lot edges {m.Edges:0} => {m.Commercial:P0} (need {Target(type, preset, step):P0}), grass {m.Grass:0}, trees {m.Trees}; issues {string.Join(",", plan.AllIssues().Select(i => i.Key.Substring(i.Key.LastIndexOf((char)46) + 1)).Distinct())}");
            Assert.False(plan.HasErrors);
            Assert.DoesNotContain(plan.AllIssues(), i => i.Key == CommercialLayer.NoStreetKey);
            // (shops) A strip mall needs a straight street of 50 m; the fully curved block has none, so only its fallback row is planned.
            // (shops2) The strip mall's stores are big plots (four cells wide, five deep): two on a small block.
            // (big box: one big store on a small block)
            Assert.True(m.NPlots >= (type == "stripMall" ? (preset == "bigBox" ? 1 : 2) : 3), type + "/" + preset + ": " + m.NPlots + " plots");
            Assert.True(m.Lots <= LotCap(type) * area, $"{type}/{preset} @ {SiteName(step)}: lots cover {m.Lots / area:P0} of the block (cap {LotCap(type):P0})");
            Assert.True(m.Commercial >= Target(type, preset, step), $"{type}/{preset} @ {SiteName(step)}: only {m.Commercial:P0} commercial (need {Target(type, preset, step):P0})");
        }
    }
}
