// s45: the Review cost bar by friendly category (Core/Build/CostBuckets) adds up to the plan's cost.
using System.Collections.Generic;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Build;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class CostBucketsTests
    {
        private static float3[] Square(float s) => new[] { new float3(0, 0, 0), new float3(s, 0, 0), new float3(s, 0, s), new float3(0, 0, s) };

        [Fact]
        public void Elements_GoToFriendlyBuckets()
        {
            Assert.Equal(CostBuckets.Planting, CostBuckets.Of(new PlannedObject { Role = ObjectRole.Tree }));
            Assert.Equal(CostBuckets.Shops, CostBuckets.Of(new PlannedObject { Role = ObjectRole.Furniture, Category = AssetCategories.Kiosk }));
            Assert.Equal(CostBuckets.Water, CostBuckets.Of(new PlannedObject { Role = ObjectRole.Centerpiece, Category = AssetCategories.CenterpieceFountain }));
            Assert.Equal(CostBuckets.Play, CostBuckets.Of(new PlannedObject { Role = ObjectRole.Furniture, Category = AssetCategories.Playground }));
            Assert.Equal(CostBuckets.Parking, CostBuckets.Of(new PlannedObject { Role = ObjectRole.ParkingBay }));
            Assert.Equal(CostBuckets.Extras, CostBuckets.Of(new PlannedObject { Role = ObjectRole.Bench }));
            Assert.Equal(CostBuckets.Paths, CostBuckets.Of(new PlannedNet { Role = NetRole.Pathway }));
            Assert.Equal(CostBuckets.Parking, CostBuckets.Of(new PlannedNet { Role = NetRole.DriveAisle }));
            Assert.Equal(CostBuckets.Roads, CostBuckets.Of(new PlannedNet { Role = NetRole.Street }));
            Assert.Equal(CostBuckets.Planting, CostBuckets.Of(new PlannedSurface { Role = SurfaceRole.Grass }));
            Assert.Equal(CostBuckets.Parking, CostBuckets.Of(new PlannedSurface { Role = SurfaceRole.Asphalt }));
            Assert.Equal(CostBuckets.Paths, CostBuckets.Of(new PlannedSurface { Role = SurfaceRole.Paving }));
        }

        [Fact]
        public void Buckets_AddUpToPricing()
        {
            var layer = new AreaPlan { GeneratorId = "outdoorMall" };
            layer.Objects.Add(new PlannedObject { Role = ObjectRole.Tree, Asset = new AssetRef("tree") });
            layer.Objects.Add(new PlannedObject { Role = ObjectRole.Tree, Asset = new AssetRef("tree") });
            layer.Objects.Add(new PlannedObject { Role = ObjectRole.Furniture, Category = AssetCategories.Kiosk, Asset = new AssetRef("kiosk") });
            layer.Surfaces.Add(new PlannedSurface { Role = SurfaceRole.Paving, Ring = Square(10), Asset = new AssetRef("paving") });
            layer.Surfaces.Add(new PlannedSurface { Role = SurfaceRole.Asphalt, Ring = Square(5), Asset = new AssetRef("asphalt") });
            var plan = new ComposedPlan();
            plan.Layers.Add(layer);
            var assets = new Dictionary<string, AssetInfo>
            {
                ["tree"] = new AssetInfo { Cost = 100, Upkeep = 1 },
                ["kiosk"] = new AssetInfo { Cost = 500, Upkeep = 5 },
                ["paving"] = new AssetInfo { Cost = 2, Upkeep = 0.01f },
                ["asphalt"] = new AssetInfo { Cost = 4, Upkeep = 0.02f },
            };
            var buckets = CostBuckets.Sum(plan, assets);
            Assert.Equal(new[] { CostBuckets.Shops, CostBuckets.Paths, CostBuckets.Planting, CostBuckets.Parking }, buckets.Select(b => b.Id).ToArray());
            Assert.Equal(Pricing.Of(plan, assets).Cost, buckets.Sum(b => b.Cost), 6);
            Assert.Equal(Pricing.Of(plan, assets).Upkeep, buckets.Sum(b => b.Upkeep), 6);
        }
    }
}
