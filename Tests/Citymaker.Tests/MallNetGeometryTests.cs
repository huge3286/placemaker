// s47: the game rejected a shopping plaza's lane pieces ("Alley" x6) and lot connectors ("Invisible Road Path - 2xTwoway" x2) with
// InvalidShape (Game.Net.ValidationHelpers.ValidateEdge: the left/right edge curves fold when a node's geometry is too tight for the
// widths: each edge end is cut back at a node by the crossing of its edge with the neighbour's — (h_other + h_self cos φ) / sin φ —
// and the two cut-backs of one edge must fit its length). Model: no piece may be shorter than its two cut-backs.
// Cause found: an alley entrance from a lot onto a lane. The lot edge touches the lane edge, so the stub is exactly the lane's half
// width long (4 m) while the T needs 4 m + the turn into the lot's connector at its other end: the stub, the 4 lane pieces on the
// two T nodes (6 "Alley") and the 2 connectors (the turn) all fold.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class MallNetGeometryTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);
        public static readonly string[] Types = { "shoppingPlaza", "outdoorMall", "mallPark", "mallDistrict", "retailPark", "highStreet", "shopsPark", "stripMall" };

        /// <summary>Half width (m) of a planned net's asset, as the planners know it.</summary>
        public static float Half(PlannedNet n)
        {
            string id = n.Asset.Id ?? "";
            if (n.Role == NetRole.Pathway || id.Contains("Pedestrian Path")) return 1f;
            if (id.StartsWith("Alley")) return 4f;
            if (id.Contains("2xTwoway")) return id.Contains("Car Path") ? 3f : 3.5f;
            if (id.Contains("1xTwoway")) return id.Contains("Car Path") ? 1.5f : 2f;
            if (id.Contains("1xOneway")) return 2f;
            if (id.StartsWith("Pedestrian Street")) return id.EndsWith("Small") ? 3f : 4f;
            return 3f;
        }

        /// <summary>Cut-back along ray i caused by ray j at a node (rays as unit directions away from the node).</summary>
        public static float Cutback(float2 di, float hi, float2 dj, float hj)
        {
            float cos = math.clamp(math.dot(di, dj), -1f, 1f);
            if (cos < -0.9659f) return 0f;                                   // ≥ 165°: a continuation
            float sin = math.sqrt(math.max(1e-6f, 1f - cos * cos));
            return math.max(0f, (hj + hi * cos) / sin);
        }

        /// <summary>Pieces whose two cut-backs do not fit their length (nets of one plan, joined at shared end points).</summary>
        public static List<string> Violations(ComposedPlan plan, SiteContext site)
        {
            var list = new List<(PlannedNet n, bool start)>();
            var all = new List<PlannedNet>();
            foreach (var l in plan.Layers) if (l != null) foreach (var n in l.Nets) if (n.Role != NetRole.Fence && n.Role != NetRole.Pathway || n.Asset.Id != null && n.Asset.Id.Contains("Path")) all.Add(n);
            var bad = new List<string>();
            float Cut(PlannedNet n, bool start)
            {
                var p = start ? n.A : n.D; var q = start ? n.B : n.C;
                var di = math.normalizesafe((q - p).xz); float hi = Half(n); float cut = 0f;
                foreach (var m in all)
                {
                    if (ReferenceEquals(m, n)) continue;
                    foreach (bool ms in new[] { true, false })
                    {
                        var mp = ms ? m.A : m.D; var mq = ms ? m.B : m.C;
                        if (math.distance(p.xz, mp.xz) > 0.06f) continue;
                        cut = math.max(cut, Cutback(di, hi, math.normalizesafe((mq - mp).xz), Half(m)));
                    }
                }
                long id = start ? n.StartAttach : n.EndAttach;
                if (id >= 0 && site?.NearbyNets != null)
                    foreach (var s in site.NearbyNets)
                        if (s.ExternalId == id)
                        {
                            float t = start ? n.StartAttachT : n.EndAttachT;
                            var a = Composer.Bezier(s.A, s.B, s.C, s.D, math.saturate(t - 0.01f)).xz; var b = Composer.Bezier(s.A, s.B, s.C, s.D, math.saturate(t + 0.01f)).xz;
                            var tan = math.normalizesafe(b - a);
                            cut = math.max(cut, math.max(Cutback(di, hi, tan, s.Width * 0.5f), Cutback(di, hi, -tan, s.Width * 0.5f)));
                        }
                return cut;
            }
            foreach (var n in all)
            {
                if (n.Role != NetRole.Alley && n.Role != NetRole.Street && n.Role != NetRole.DriveConnector) continue;   // the lanes and the lot connectors the game rejected
                float len = math.distance(n.A.xz, n.D.xz);
                float need = Cut(n, true) + Cut(n, false);
                if (len < need - 0.05f) bad.Add($"{n.Role} '{n.Asset.Id}' ({n.A.x:0.0},{n.A.z:0.0})->({n.D.x:0.0},{n.D.z:0.0}) is {len:0.0} m, its node cut-backs take {need:0.0} m");
            }
            return bad;
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (var t in Types)
                foreach (var p in new[] { "" }.Concat(GeneratorRegistry.Get(t).Presets.Select(x => x.Id)))
                    yield return new object[] { t, p };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void NoPieceIsTooShortForItsNodes(string type, string preset)
        {
            var ring = TestKit.Rect(115, 115);
            var site = ZoningCutoutTests.Streets(ring);
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(type), 5);
            if (preset != "") Editor.ApplyPreset(r, RecipePaths.Root, preset);
            uint q = 0;
            Editor.AddDefaultFeatures(r, () => ++q);
            ShopTestEntrances.Place(r, ring, site, footpath: true, driveway: true);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            Assert.False(plan.HasErrors, $"{type}/{preset}");
            var bad = Violations(plan, site);
            Assert.True(bad.Count == 0, $"{type}/{preset}: " + string.Join("; ", bad));
        }
    }
}
