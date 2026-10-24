// urbanFarm: shared farm checks plus what makes each subtype look like itself.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Layers.Types.UrbanFarm;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests.Types
{
    [Collection(Citymaker.Tests.TimingCollection.Name)]
    public class UrbanFarmTests : FarmTypeTestBase
    {
        public UrbanFarmTests(ITestOutputHelper output) : base(output) { }
        protected override string TypeId => UrbanFarmLayer.LayerId;
        protected override string Folder => "UrbanFarm";

        private AreaPlan Preset(string id, float3[] ring = null) =>
            PlanPreset(Gen.Presets.Single(p => p.Id == id), ring ?? FiveHa, 5, TestKit.RoadAlongBottom(300));

        [Fact]
        public void SubtypesPlaceTheirSignatureCrops()
        {
            var expect = new Dictionary<string, string>
            {
                ["marketGarden"] = AssetCategories.GardenCrop,
                ["vineyard"] = AssetCategories.CityFarmVine,
                ["greenhouses"] = AssetCategories.Glasshouse,
                ["lavenderField"] = AssetCategories.CityFarmLavender,
                ["rooftopBeds"] = AssetCategories.Planter,
            };
            foreach (var kv in expect)
            {
                var plan = Preset(kv.Key);
                int n = plan.Objects.Count(o => o.Category == kv.Value);
                Out.WriteLine($"{kv.Key}: {n} × {kv.Value}, {plan.Objects.Count} objects, {plan.Surfaces.Count} surfaces");
                Assert.True(n >= 10, kv.Key + " has only " + n + " " + kv.Value);
            }
            // The mixed city farm grows several crops side by side.
            var mixed = Preset("cityFarm");
            var kinds = new[] { AssetCategories.GardenCrop, AssetCategories.CityFarmVine, AssetCategories.Glasshouse, AssetCategories.CityFarmLavender };
            Assert.True(kinds.Count(k => mixed.Objects.Any(o => o.Category == k)) >= 3);
        }

        [Fact]
        public void FieldsAreRegionsAndCropsGrowInRows()
        {
            var plan = Preset("marketGarden");
            var fields = plan.Regions.Where(r => r.Tags.Contains(UrbanFarmLayer.FieldTag)).ToList();
            Assert.True(fields.Count >= 4);
            // Crop items sit inside the fields, oriented along the rows (all share one of two yaws).
            var crops = plan.Objects.Where(o => o.Category == AssetCategories.GardenCrop).ToList();
            var rings = fields.Select(f => Poly.To2D(f.Ring)).ToList();
            Assert.All(crops, c => Assert.Contains(rings, r => Poly.Contains(r, c.Position.xz)));
            Assert.True(crops.Select(c => math.round(math.abs(math.sin(c.Yaw)) * 100f)).Distinct().Count() <= 2);
            Assert.Contains(plan.Nets, n => n.Role == NetRole.Pathway);
            Assert.NotEmpty(plan.Paint);
        }

        [Fact]
        public void FarmyardHasBarnAndShopByTheEntrance()
        {
            var plan = Preset("marketGarden", TestKit.Rect(160, 120));
            Assert.Equal(1.0, plan.Facts["farmyard"]);
            var barn = plan.Objects.Single(o => o.Category == AssetCategories.CityFarmBarn);
            var shop = plan.Objects.First(o => o.Category == AssetCategories.MarketStall);
            Assert.True(barn.Position.z < 40f && shop.Position.z < 40f, "yard is not by the road-side entrance");
        }

        [Fact]
        public void VineyardTerracesAndGlasshousesLevel()
        {
            var site = new SiteContext { HeightAt = q => 0.05f * q.y };
            site.NearbyNets.AddRange(TestKit.RoadAlongBottom(250).NearbyNets);
            foreach (var id in new[] { "vineyard", "greenhouses" })
            {
                var plan = PlanPreset(Gen.Presets.Single(p => p.Id == id), FiveHa, 5, site);
                Assert.NotEmpty(plan.Terraform);
                AssertTerraformBlended(plan, FiveHa, site.HeightAt);
            }
        }

        [Fact]
        public void BoundaryOptionsChangeTheEdge()
        {
            var g = Gen;
            AreaPlan With(string boundary, bool windbreak)
            {
                var s = g.Defaults();
                s.Values["boundary"] = boundary; s.Values["windbreak"] = windbreak;
                return g.Plan(TestKit.Input(TestKit.Rect(160, 120), s, 3, null, TestKit.RoadAlongBottom(160)), CancellationToken.None);
            }
            Assert.Contains(With("fence", false).Nets, n => n.Role == NetRole.Fence);
            Assert.DoesNotContain(With("none", false).Nets, n => n.Role == NetRole.Fence);
            Assert.True(With("hedge", false).Objects.Count(o => o.Category == AssetCategories.Hedge) > 40);
            Assert.True(With("none", true).Objects.Count(o => o.Role == ObjectRole.Tree) > 20);
        }

        [Fact]
        public void BigFarmStaysInTheObjectBudget()
        {
            var s = Gen.Defaults();
            s.Values["density"] = 150.0;
            var plan = Gen.Plan(TestKit.Input(TestKit.Rect(500, 400), s, 1), CancellationToken.None);
            Assert.False(plan.HasErrors);
            Assert.True(plan.Facts["cropItems"] <= UrbanFarmLayer.MaxCropItems);
            Assert.True(plan.Facts["cropItems"] > UrbanFarmLayer.MaxCropItems * 0.6, "budget scaling thins far too much");
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.CityFarmVine] = 0, [AssetCategories.Hedge] = 0, [AssetCategories.Bush] = 0, [AssetCategories.CityFarmBarn] = 0,
                [AssetCategories.CityFarmScarecrow] = 0, [AssetCategories.CityFarmBeehive] = 0, [AssetCategories.Beehive] = 0, [AssetCategories.CityFarmTrellis] = 0,
            };
            using (AssetAvailability.Scope(none))
            {
                var plan = Compose(With(Citymaker.Core.Layers.Types.Farmstead.ApiaryAddOn.LayerId), FiveHa);   // honeyHives merged into apiary (g5)
                Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
                var s = Gen.Presets.Single(p => p.Id == "vineyard").Values.Clone();
                var vineyard = Gen.Plan(TestKit.Input(FiveHa, s, 2), CancellationToken.None);
                Assert.False(vineyard.HasErrors);
                Assert.Contains(vineyard.Issues, i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                Assert.DoesNotContain(vineyard.Objects, o => none.ContainsKey(o.Category ?? ""));
                Assert.Contains(plan.AllIssues(), i => i.Key == Keys.NoAssetsIssue);
            }
        }

        [Theory]
        [InlineData("GrapeVine01", AssetCategories.CityFarmVine)]
        [InlineData("VineyardRow02", AssetCategories.CityFarmVine)]
        [InlineData("LavenderBush01", AssetCategories.CityFarmLavender)]
        [InlineData("GardenTrellis01", AssetCategories.CityFarmTrellis)]
        [InlineData("SmallBarn01", AssetCategories.CityFarmBarn)]
        [InlineData("WoodenCrate03", AssetCategories.CityFarmProp)]
        [InlineData("TractorOld01", AssetCategories.CityFarmMachine)]
        [InlineData("Scarecrow01", AssetCategories.CityFarmScarecrow)]
        [InlineData("Beehive02", AssetCategories.CityFarmBeehive)]
        public void Classification(string name, string category)
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            Assert.Contains(category, AssetHeuristics.Classify(name, t));
            Assert.True(AssetHeuristics.AutoEligible(category, name, t), name + " should be automatic for " + category);
            Assert.DoesNotContain(category, AssetHeuristics.Classify("Billboard" + name, t).Where(c => AssetHeuristics.AutoEligible(c, "Billboard" + name, t)));
        }
    }
}
