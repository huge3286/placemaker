// s8 "ground before props": farm ground categories (surface.crop.*, decal.farm), the GroundCover helper,
// the per-plan ObjectBudget, and the object counts of the reworked heavy types on a 1.2 ha site.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Layers.Common;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests
{
    public class GroundCoverTests
    {
        private readonly ITestOutputHelper _out;
        public GroundCoverTests(ITestOutputHelper output) { _out = output; }

        private const AssetTraits Mod = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;

        [Theory]
        [InlineData("Agriculture Surface 01", AssetCategories.SurfaceCrop)]
        [InlineData("Farming Surfaces & Decals Ground Haristess - 1-1 - MudSoil1 Surface", AssetCategories.SurfaceCropSoil)]
        [InlineData("Farming Surfaces & Decals Ground Haristess - 1-2 - MudPlowed1 Surface", AssetCategories.SurfaceCropPlowed)]
        [InlineData("Farming Surfaces & Decals Ground Haristess - 4-1 - CropRows Surface", AssetCategories.SurfaceCropRows)]
        [InlineData("Farming Surfaces & Decals Ground Haristess - 5-1 - GrassCrop1 Green Surface", AssetCategories.SurfaceCropGreen)]
        [InlineData("Farming Surfaces & Decals Ground Haristess - 2-1 - StrawFlat1 Golden Surface", AssetCategories.SurfaceCropStraw)]
        [InlineData("Farming Surfaces & Decals Ground Haristess - 5-3 - GrassCrop1 Blue Surface", AssetCategories.SurfaceCropLavender)]
        [InlineData("Farming Surfaces & Decals Ground Haristess - 5-3 - GrassCrop1 Yellow Surface", AssetCategories.SurfaceCropYellow)]
        public void FarmSurfacesAreClassified(string name, string category)
        {
            var cats = AssetHeuristics.Classify(name, AssetTraits.Surface);
            Assert.Contains(category, cats);
            Assert.Contains(AssetCategories.SurfaceCrop, cats);
        }

        [Theory]
        [InlineData("Farming Surfaces & Decals Ground Haristess - 5-4 - GrassCrop1 Green Overlay Surface")]
        [InlineData("Citymaker Overlay (Agriculture Surface 01)")]
        [InlineData("PLT Vorflaeche (Agriculture Surface 01, -90)")]
        [InlineData("Grass Surface 01")]
        [InlineData("Pavement Surface 01")]
        public void OverlaysClonesAndOtherSurfacesAreNotFields(string name)
        {
            Assert.DoesNotContain(AssetHeuristics.Classify(name, AssetTraits.Surface), c => c.StartsWith(AssetCategories.SurfaceCrop));
        }

        [Fact]
        public void FarmTrackDecalsStraightPiecesOnlyAreAutomatic()
        {
            const string straight = "Farming Surfaces & Decals RoadMarkings Haristess - 4-1 - FieldTracks Straight1 Decal";
            const string corner = "Farming Surfaces & Decals RoadMarkings Haristess - 2-4 - TractorTracks Dkr LCorner 45 Short Decal";
            var t = Mod | AssetTraits.DecalMesh;
            Assert.Contains(AssetCategories.DecalFarm, AssetHeuristics.Classify(straight, t));
            Assert.True(AssetHeuristics.AutoEligible(AssetCategories.DecalFarm, straight, t));
            Assert.False(AssetHeuristics.AutoEligible(AssetCategories.DecalFarm, corner, t));
        }

        [Fact]
        public void CropCategoriesFallBackToAgricultureThenDirt()
        {
            var chain = CategoryFallbacks.Chain(AssetCategories.SurfaceCropLavender).ToList();
            Assert.Equal(new[] { AssetCategories.SurfaceCropLavender, AssetCategories.SurfaceCropFlower, AssetCategories.SurfaceCropGreen,
                AssetCategories.SurfaceCrop, AssetCategories.SurfaceDirt, AssetCategories.SurfaceNatural }, chain);
            // Without the farming pack only the vanilla Agriculture Surface (surface.crop) is left.
            var counts = new Dictionary<string, int>();
            foreach (var c in chain) counts[c] = 0;
            counts[AssetCategories.SurfaceCrop] = 1;
            using (AssetAvailability.Scope(counts)) Assert.Equal(AssetCategories.SurfaceCrop, GroundCover.Resolve(AssetCategories.SurfaceCropLavender));
            foreach (var c in chain) counts[c] = 0;
            using (AssetAvailability.Scope(counts)) Assert.Null(GroundCover.Resolve(AssetCategories.SurfaceCropLavender));
        }

        [Fact]
        public void FieldsBecomePaintWhenNoFarmlandSurfaceExists()
        {
            var counts = new Dictionary<string, int>();
            foreach (var k in new[] { GroundCover.Soil, GroundCover.Plowed, GroundCover.Rows, GroundCover.Green, GroundCover.Straw, GroundCover.Lavender, GroundCover.Yellow })
                foreach (var c in CategoryFallbacks.Chain(GroundCover.CategoryFor(k))) counts[c] = 0;
            using (AssetAvailability.Scope(counts))
            {
                var gen = TestKit.Gen("farmstead");
                var s = gen.Presets.Single(p => p.Id == "arable").Values.Clone();
                var plan = gen.Plan(TestKit.Input(TestKit.Rect(250, 200), s, 3, null, TestKit.RoadAlongBottom(250)), CancellationToken.None);
                Assert.False(plan.HasErrors);
                Assert.DoesNotContain(plan.Surfaces, x => (x.Category ?? "").StartsWith(AssetCategories.SurfaceCrop));
                Assert.NotEmpty(plan.Paint);
            }
        }

        [Fact]
        public void RowLinesCoverTheFieldAlongTheDirection()
        {
            var field = new[] { new float2(0, 0), new float2(40, 0), new float2(40, 20), new float2(0, 20) };
            var rows = GroundCover.RowLines(field, new float2(1, 0), 2f, 0.5f);
            Assert.Equal(10, rows.Count);
            Assert.All(rows, r => Assert.True(math.abs(r.a.y - r.b.y) < 1e-3f && math.distance(r.a, r.b) > 38f));
        }

        [Fact]
        public void ObjectBudgetThinsTheBiggestRepetitiveGroupsOnly()
        {
            var plan = new AreaPlan();
            for (int i = 0; i < 2000; i++) plan.Objects.Add(new PlannedObject { Role = ObjectRole.Furniture, Category = AssetCategories.GardenCrop, Position = new float3(i, 0, 0) });
            for (int i = 0; i < 100; i++) plan.Objects.Add(new PlannedObject { Role = ObjectRole.Bush, Category = AssetCategories.Hedge });
            for (int i = 0; i < 20; i++) plan.Objects.Add(new PlannedObject { Role = ObjectRole.Bench });
            plan.Objects.Add(new PlannedObject { Role = ObjectRole.Furniture, Category = AssetCategories.CityFarmBarn });
            int removed = ObjectBudget.Enforce(plan, 12000f, ObjectBudget.DefaultPerHectare);
            int cap = ObjectBudget.Cap(12000f, ObjectBudget.DefaultPerHectare);
            Assert.True(removed > 0);
            Assert.True(plan.Objects.Count <= cap + 1, plan.Objects.Count + " > " + cap);
            Assert.Equal(100, plan.Objects.Count(o => o.Category == AssetCategories.Hedge));   // rows stay closed
            Assert.Equal(20, plan.Objects.Count(o => o.Role == ObjectRole.Bench));
            Assert.Single(plan.Objects, o => o.Category == AssetCategories.CityFarmBarn);
            Assert.Contains(plan.Issues, i => i.Key == ObjectBudget.IssueKey && i.Severity == IssueSeverity.Warning);
            // Even stride: the kept crops still span the whole row.
            var xs = plan.Objects.Where(o => o.Category == AssetCategories.GardenCrop).Select(o => o.Position.x).ToList();
            Assert.True(xs.Min() < 10f && xs.Max() > 1990f);
            // Small plans are untouched.
            var small = new AreaPlan();
            for (int i = 0; i < 200; i++) small.Objects.Add(new PlannedObject { Role = ObjectRole.Bush });
            Assert.Equal(0, ObjectBudget.Enforce(small, 12000f, ObjectBudget.DefaultPerHectare));
        }

        [Theory]
        [InlineData("urbanFarm")]
        [InlineData("farmstead")]
        [InlineData("communityGarden")]
        [InlineData("formalGarden")]
        [InlineData("zenGarden")]
        [InlineData("logisticsYard")]
        public void HeavyTypesStayLightOnAOneHectareSite(string id)
        {
            var gen = TestKit.Gen(id);
            var ring = TestKit.Rect(110, 110);
            var variants = new List<(string name, SettingsBag s)> { ("default", gen.Defaults()) };
            foreach (var p in gen.Presets) variants.Add((p.Id, p.Values.Clone()));
            foreach (var (name, s) in variants)
            {
                var plan = gen.Plan(TestKit.Input(ring, s, 7, null, TestKit.RoadAlongBottom(110)), CancellationToken.None);
                _out.WriteLine($"{id}/{name}: {plan.Objects.Count} objects, {plan.Surfaces.Count} surfaces");
                Assert.False(plan.HasErrors);
                Assert.True(plan.Objects.Count <= 650, $"{id}/{name}: {plan.Objects.Count} objects on 1.2 ha");
                Assert.DoesNotContain(plan.Issues, i => i.Key == ObjectBudget.IssueKey);   // fits by design, not by thinning
            }
        }
    }
}
