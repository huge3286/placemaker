// s58: the entrance rule of s41/s47 for EVERY type, not only the mall family: wherever a net of any layer attaches to an existing
// street (a lane, a drive connector, a path), it leaves the street at the street's own height (street on a fill, in a cut, on a
// cross slope), and the ground round the join has no hollow.

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
    public class EntranceAllTypesTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        public static IEnumerable<object[]> Cases()
        {
            foreach (var g in GeneratorRegistry.All)
            {
                if (GeneratorRegistry.IsHidden(g) || GeneratorRegistry.IsLoose(g.Id)) continue;
                foreach (var kind in new[] { "fill", "cut", "cross" })
                    yield return new object[] { g.Id, kind };
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void EveryNetAttachedToAStreetLeavesItFlushWithoutAHollow(string gen, string kind)
        {
            var ring = TestKit.Rect(140, 130);
            var site = ZoningCutoutTests.Streets(ring);
            Func<float2, float> ground = kind == "cross" ? LotGradingTests.Ground(kind) : (_ => 0f);
            float off = kind == "fill" ? 1.2f : kind == "cut" ? -1f : 0f;
            site.HeightAt = ground;
            for (int k = 0; k < site.NearbyNets.Count; k++)
            {
                var n = site.NearbyNets[k];
                n.A.y = ground(n.A.xz) + off; n.B.y = ground(n.B.xz) + off; n.C.y = ground(n.C.xz) + off; n.D.y = ground(n.D.xz) + off;
                site.NearbyNets[k] = n;
            }
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(gen), 5);
            // Types that take placed entrances (parking, rest area, transit plaza, ...): one on the south street, one on the west one.
            var gdef = GeneratorRegistry.Get(gen);
            if (gdef.Controls.Any(c => c.Kind == ControlKind.Markers && c.MarkerKinds != null && c.MarkerKinds.Contains("entrance")))
            {
                r.Root.Markers.Add(ParkingKit.Marker(ring, 0, 40));
                r.Root.Markers.Add(ParkingKit.Marker(ring, 3, 60));
            }
            uint q = 0;
            Editor.AddDefaultFeatures(r, () => ++q);
            ComposedPlan plan;
            try { plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None); }
            catch (Exception e) { throw new Exception($"{gen}/{kind}: {e.Message}", e); }
            string ctx = $"{gen}/{kind}";
            var final = LotGradingTests.Final(plan, ground);
            var joins = new List<float2>(); int ndead = 0;

            foreach (var l in plan.Layers.Where(l => l != null))
                foreach (var n in l.Nets)
                {
                    if (n.Role == NetRole.Fence) continue;
                    for (int end = 0; end < 2; end++)
                    {
                        long id = end == 0 ? n.StartAttach : n.EndAttach;
                        if (id < 0) continue;
                        var street = site.NearbyNets.FirstOrDefault(s => s.ExternalId == id);
                        if (street.Width <= 0f) continue;
                        float t = end == 0 ? n.StartAttachT : n.EndAttachT;
                        float hs = Composer.Bezier(street.A, street.B, street.C, street.D, t).y;
                        var p0 = end == 0 ? n.A.xz : n.D.xz;
                        var p1 = end == 0 ? n.D.xz : n.A.xz;
                        var d = math.normalizesafe(p1 - p0);
                        float edge = street.Width * 0.5f;
                        float atKerb = final(p0 + d * (edge + 0.5f));
                        Assert.True(math.abs(atKerb - hs) <= 0.35f, $"{ctx}: {n.Role} in {l.GeneratorId} at the kerb {atKerb:0.00} m, street {hs:0.00} m");
                        joins.Add(p0 + d * (edge + 1f));
                    }
                    // A dead end at the kerb (a plain entrance: the game connects it by LocalConnect) is a join as well; a 2 m apron ramps at most 0.45 m in its first half metre.
                    foreach (var pe in new[] { n.A, n.D })
                    {
                        if (n.Role != NetRole.DriveConnector) continue;
                        foreach (var s in site.NearbyNets)
                        {
                            float best = float.MaxValue, hy = 0; var at = float2.zero;
                            for (int k = 0; k <= 32; k++) { var c = Composer.Bezier(s.A, s.B, s.C, s.D, k / 32f); float dd = math.distance(c.xz, pe.xz); if (dd < best) { best = dd; hy = c.y; at = c.xz; } }
                            if (best > s.Width * 0.5f + 1.5f || best < 0.01f) continue;
                            var dir = math.normalizesafe(pe.xz - at);
                            float atK = final(at + dir * (s.Width * 0.5f + 0.5f));
                            Assert.True(math.abs(atK - hy) <= 0.45f, $"{ctx}: {n.Role} in {l.GeneratorId} ends at the kerb ({pe.xz}, {best:0.0} m from the street axis, w {s.Width}, net {n.A.xz}->{n.D.xz}) at {atK:0.00} m, street {hy:0.00} m; net y {n.A.y:0.00}..{n.D.y:0.00}; cover {EntranceDebug.Cover(plan, at + dir * (s.Width * 0.5f + 0.5f))}; inward at 0.5/2/4/8/14 m: {string.Join(" ", new[] {0.5f, 2f, 4f, 8f, 14f}.Select(o => final(at + dir * (s.Width * 0.5f + o)).ToString("0.00")))}");
                            joins.Add(pe.xz); ndead++;
                        }
                    }
                }

            if (new[] { "stripMall", "shoppingPlaza", "parking", "restArea" }.Contains(gen)) Assert.True(joins.Count > 0, ctx + $": no net attaches to a street (markers {r.Root.Markers.Count}, nets {plan.Layers.Sum(l => l?.Nets.Count ?? 0)}, issues {string.Join(",", plan.AllIssues().Select(i => i.Key))})");
            foreach (var j in joins)
                for (float dy = -10f; dy <= 10f; dy += 2f)
                    for (float dx = -10f; dx <= 10f; dx += 2f)
                    {
                        var p = j + new float2(dx, dy);
                        bool streetNear = false;
                        foreach (var s in site.NearbyNets)
                            if (math.sqrt(SegmentIndex.DistSq(p, s.A.xz, s.D.xz)) < s.Width * 0.5f + 0.5f) { streetNear = true; break; }
                        if (streetNear) continue;
                        float h = final(p), low = float.MaxValue;
                        for (int k = 0; k < 8; k++) { float a = k * math.PI / 4f; low = math.min(low, final(p + 3f * new float2(math.cos(a), math.sin(a)))); }
                        Assert.False(h < low - 0.25f, $"{ctx}: a hollow at {p} next to a street join ({low - h:0.00} m below its surroundings)");
                    }
        }
    }
}
namespace Citymaker.Tests
{
    public static class EntranceDebug
    {
        public static string Cover(Citymaker.Core.ComposedPlan plan, Unity.Mathematics.float2 p)
        {
            var sb = new System.Text.StringBuilder();
            for (int li = 0; li < plan.Layers.Count; li++)
            {
                var l = plan.Layers[li]; if (l == null) continue;
                for (int i = 0; i < l.Terraform.Count; i++)
                {
                    var t = l.Terraform[i];
                    if (t.Ring.Length >= 3 && Citymaker.Core.Geometry.Poly.Contains(Citymaker.Core.Geometry.Poly.To2D(t.Ring), p))
                        sb.Append($" [L{li}#{i} ring={string.Join(";", t.Ring.Select(v => v.x.ToString("0.0") + "," + v.z.ToString("0.0") + "," + v.y.ToString("0.00")))} sl={t.Sloped} ap={t.Apron} h={Citymaker.Core.Build.TerraformSurface.At(t.Ring, p):0.00}]");
                }
            }
            return sb.ToString();
        }
    }
}
