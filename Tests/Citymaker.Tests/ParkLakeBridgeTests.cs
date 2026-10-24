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
    /// <summary>Art pass a1 (in-game bug): the park lake's bridge to the island must be reachable from the park's walks.</summary>
    public class ParkLakeBridgeTests
    {
        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);

        private static ComposedPlan ParkWithIslandLake(uint seed)
        {
            var r = Editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), seed);
            r.Root.Settings.Values["lakeSize"] = 100.0;
            Assert.True(Editor.SetFeature(r, "0", "pond", true, seed + 1));
            var pond = r.Root.Children.Single(c => c.Layer == "pond");
            pond.Settings.Values["islands"] = 1.0;
            pond.Settings.Values["bridges"] = true;
            return Composer.Compose(r, new AreaShape { Ring = TestKit.Rect(240, 200), Source = AreaSource.Drawn }, new SiteContext(), CancellationToken.None);
        }

        private static float DistToNet(PlannedNet n, float2 p)
        {
            float best = float.MaxValue;
            var prev = n.A.xz;
            for (int i = 1; i <= 16; i++)
            {
                var q = Composer.Bezier(n.A, n.B, n.C, n.D, i / 16f).xz;
                best = math.min(best, math.distance(p, SegmentIndex.ClosestPoint(p, prev, q)));
                prev = q;
            }
            return best;
        }

        [Theory]
        [InlineData(11u)]
        [InlineData(23u)]
        [InlineData(37u)]
        public void BridgeJoinsTheParkWalksAndTheIslandHasALoop(uint seed)
        {
            var plan = ParkWithIslandLake(seed);
            var park = plan.Layers.Single(l => l.GeneratorId == "park");
            var pond = plan.Layers.Single(l => l.GeneratorId == "pond");
            Assert.True(pond.Facts.TryGetValue("bridges", out var bridges) && bridges >= 1, "an island with a bridge fits a 4.8 ha park");
            Assert.Equal(1, pond.Facts["bridgeLinked"]);
            // The shore end of each deck reaches a park walk: directly or through one link path.
            var decks = pond.Nets.Where(n => !n.FollowTerrain && math.abs(n.A.y - n.D.y) > 0.01f || !n.FollowTerrain && n.B.y > n.A.y + 0.3f).ToList();
            Assert.NotEmpty(decks);
            foreach (var d in decks)
            {
                var land = d.D.xz;
                bool onPark = park.Nets.Any(n => DistToNet(n, land) < 0.6f);
                bool viaLink = pond.Nets.Any(n => n.FollowTerrain && math.distance(n.A.xz, land) < 0.1f && park.Nets.Any(pn => DistToNet(pn, n.D.xz) < 0.6f));
                Assert.True(onPark || viaLink, "bridge landing " + land + " leads nowhere");
                Assert.False(d.FollowTerrain);
                Assert.True(Composer.Bezier(d.A, d.B, d.C, d.D, 0.5f).y > pond.Terraform[0].WaterLevel + 1.2f, "deck clears the water");
            }
            if (pond.Facts.TryGetValue("islandPaths", out var loops)) Assert.True(loops >= 1);
        }
    }
}
