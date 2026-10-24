using System.Collections.Generic;
using Citymaker.Core;
using Citymaker.Core.Build;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class BuildAutoAssetTests
    {
        private static readonly Dictionary<string, string[]> Pools = new Dictionary<string, string[]>
        {
            [AssetCategories.Tree] = new[] { "Oak", "Birch", "Pine" },
            [AssetCategories.Bench] = new[] { "Bench01", "Bench02" },
            [AssetCategories.Road] = new[] { "Small Road" },
        };

        private static IReadOnlyList<string> Pool(string c) => Pools.TryGetValue(c, out var p) ? p : new string[0];
        private static bool Exists(string id) => id == "Oak" || id == "Birch" || id == "Pine" || id == "Bench01" || id == "Bench02" || id == "Small Road" || id == "Linden";

        private static ComposedPlan Plan()
        {
            var l = new AreaPlan();
            for (uint i = 0; i < 30; i++) l.Objects.Add(new PlannedObject { Role = ObjectRole.Tree, Variation = i });
            l.Objects.Add(new PlannedObject { Role = ObjectRole.Bench, Variation = 7 });
            l.Objects.Add(new PlannedObject { Role = ObjectRole.Tree, Asset = new AssetRef("Linden") });
            l.Objects.Add(new PlannedObject { Role = ObjectRole.Bench, Asset = new AssetRef("Gone", "Bench02") });
            l.Nets.Add(new PlannedNet { Role = NetRole.Street, D = new float3(10, 0, 0) });
            var p = new ComposedPlan();
            p.Layers.Add(l);
            return p;
        }

        [Fact]
        public void ResolvesDeterministicallyAndKeepsExplicitIds()
        {
            var a = Plan(); var b = Plan();
            AutoAssets.Resolve(a, Pool, Exists);
            AutoAssets.Resolve(b, Pool, Exists);
            var objs = a.Layers[0].Objects;
            for (int i = 0; i < objs.Count; i++) Assert.Equal(objs[i].Asset.Id, b.Layers[0].Objects[i].Asset.Id);
            var trees = new HashSet<string>();
            for (int i = 0; i < 30; i++) trees.Add(objs[i].Asset.Id);
            Assert.True(trees.Count > 1);                    // variety
            Assert.Equal("Bench01", objs[30].Asset.Id);      // functional objects: default
            Assert.Equal("Linden", objs[31].Asset.Id);       // explicit kept
            Assert.Equal("Bench02", objs[32].Asset.Id);      // missing explicit → loaded fallback
            Assert.Equal("Small Road", a.Layers[0].Nets[0].Asset.Id);
            Assert.Equal(0, AutoAssets.Resolve(a, Pool, Exists));   // idempotent
        }

        [Fact]
        public void PricingUsesRoundedNetCells()
        {
            Assert.Equal(8.0, Pricing.BillableNetLength(2.0));
            Assert.Equal(16.0, Pricing.BillableNetLength(13.0));
        }
    }
}
