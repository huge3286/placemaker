// s55: lots entered from a lane the build itself plans (alley kind: the lot's connector joins the mall's lane, not an existing street).
// Playtest 91: a lot in a shop area dropped into a deep bowl where its entrance lane met the street. The plan must be flush there:
// the connector ends on the lane at the lane's own height, the ground at the join is the lane's level, and no hollow lies round it.

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
    public class LotFromLaneTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        public static IEnumerable<object[]> Cases()
        {
            foreach (var gen in new[] { "shoppingPlaza", "outdoorMall", "mallPark", "mallDistrict", "retailPark", "highStreet", "shopsPark", "stripMall" })
                foreach (var g in new[] { "fill", "cut", "cross", "steep", "gentle" })
                    foreach (uint seed in new uint[] { 5, 9 })
                        yield return new object[] { gen, g, seed };
        }

        private static float YAt(PlannedNet n, float2 p)
        {
            float best = float.MaxValue, y = 0;
            for (int i = 0; i <= 32; i++)
            {
                var q = Composer.Bezier(n.A, n.B, n.C, n.D, i / 32f);
                float d = math.distancesq(q.xz, p);
                if (d < best) { best = d; y = q.y; }
            }
            return y;
        }

        private static float DistTo(PlannedNet n, float2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i <= 32; i++) best = math.min(best, math.distancesq(Composer.Bezier(n.A, n.B, n.C, n.D, i / 32f).xz, p));
            return math.sqrt(best);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void ALotEnteredFromAPlannedLaneIsFlushWithItAndHasNoHollow(string gen, string kind, uint seed)
        {
            var ring = TestKit.Rect(140, 130);
            var site = ZoningCutoutTests.Streets(ring);
            Func<float2, float> ground = kind == "cross" || kind == "steep" || kind == "gentle" ? LotGradingTests.Ground(kind) : (_ => 0f);
            float off = kind == "fill" ? 1.2f : kind == "cut" ? -1f : 0f;
            site.HeightAt = ground;
            for (int k = 0; k < site.NearbyNets.Count; k++)
            {
                var n = site.NearbyNets[k];
                n.A.y = ground(n.A.xz) + off; n.B.y = ground(n.B.xz) + off; n.C.y = ground(n.C.xz) + off; n.D.y = ground(n.D.xz) + off;
                site.NearbyNets[k] = n;
            }
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get(gen), seed);
            uint q = 0;
            Editor.AddDefaultFeatures(r, () => ++q);
            ShopTestEntrances.Place(r, ring, site, footpath: true, driveway: true);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            string ctx = $"{gen}/{kind}/{seed}";
            Assert.False(plan.HasErrors, ctx);
            var final = LotGradingTests.Final(plan, ground);
            var nets = plan.Layers.Where(l => l != null).SelectMany(l => l.Nets).ToList();
            var lanes = nets.Where(n => n.Role == NetRole.Alley || n.Role == NetRole.Street).ToList();
            var stubs = nets.Where(n => n.Role == NetRole.DriveConnector || n.Role == NetRole.DriveAisle).ToList();
            int joins = 0;   // (some types plan no lane-fed lot connector: then nothing to check)
            foreach (var s in stubs)
                foreach (var end in new[] { s.A, s.D })
                    foreach (var lane in lanes)
                    {
                        if (DistTo(lane, end.xz) > 0.6f) continue;
                        joins++;
                        float laneY = YAt(lane, end.xz);
                        // The lot's connector meets the lane at the lane's own height.
                        Assert.True(math.abs(end.y - laneY) <= 0.35f, $"{ctx}: connector end at {end.xz} is {end.y:0.00} m, its lane {laneY:0.00} m");
                        // The ground there is the lane's level, and nothing hollow lies round the join.
                        Assert.True(math.abs(final(end.xz) - laneY) <= 0.4f, $"{ctx}: ground at the join {end.xz} is {final(end.xz):0.00} m, the lane {laneY:0.00} m");
                        for (float rad = 3f; rad <= 9f; rad += 3f)
                            for (int k = 0; k < 12; k++)
                            {
                                var p = end.xz + rad * new float2(math.cos(k * math.PI / 6f), math.sin(k * math.PI / 6f));
                                if (lanes.Any(l => DistTo(l, p) < 5f)) continue;   // on the lane itself
                                float h = final(p), low = float.MaxValue;
                                for (int j = 0; j < 8; j++) low = math.min(low, final(p + 3f * new float2(math.cos(j * math.PI / 4f), math.sin(j * math.PI / 4f))));
                                Assert.False(h < low - 0.2f, $"{ctx}: a hollow at {p} next to the join {end.xz} ({low - h:0.00} m)");
                            }
                    }
        }
    }
}
