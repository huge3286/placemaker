// Type wave builder 13: orchard park (group "parks"). Shared checks live in FarmTypeChecks.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests.Types
{
    [Collection(TimingCollection.Name)]
    public class OrchardParkTests
    {
        private const string Id = "orchardPark";
        private readonly ITestOutputHelper _out;
        public OrchardParkTests(ITestOutputHelper output) { _out = output; }

        [Fact] public void PresetsPlanOnOneAndSixHectares() => FarmTypeChecks.PresetsPlanEverywhere(Id);
        [Fact] public void PresetsLookDifferent() => FarmTypeChecks.PresetsLookDifferent(Id);
        [Fact] public void Deterministic() => FarmTypeChecks.Deterministic(Id);
        [Fact] public void FastOnFiveHectares() => FarmTypeChecks.Fast(Id, _out);
        [Fact] public void FeaturesCompose() => FarmTypeChecks.FeaturesCompose(Id);
        [Fact] public void EveryKeyHasEnglishText() => FarmTypeChecks.EveryKeyHasEnglishText(Id, "OrchardPark");
        [Fact] public void RecipeShipsWithIcon() => FarmTypeChecks.RecipeIconExists(Id);
        [Fact] public void TerraformBlendedAtEdge() => FarmTypeChecks.TerraformBlendedAtEdge(Id);

        [Fact]
        public void TerracedOliveGroveIsBlendedAtTheEdge()
        {
            // Terraces come from the terrain feature: compose it on a slope and check the outline.
            var r = FarmTypeChecks.With(Id, "terrain");
            r.Root.Settings.Values["style"] = "olive";
            var site = TestKit.RoadAlongBottom(250);
            site.HeightAt = p => 0.05f * p.y;
            var ring = FarmTypeChecks.FiveHa;
            var plan = FarmTypeChecks.Compose(r, ring, site);
            Assert.True(string.IsNullOrEmpty(FarmTypeChecks.Errors(plan)), FarmTypeChecks.Errors(plan));
            // (s31) The terrain feature's change is part of the one graded field the site harmony pass emits (on the type's own plan).
            var all = plan.Layers.SelectMany(l => l.Terraform).Where(t => !math.isfinite(t.WaterLevel)).ToList();
            Assert.NotEmpty(all);
            var r2 = Poly.To2D(ring);
            foreach (var t in all)
            {
                var c = Poly.Centroid(Poly.To2D(t.Ring));
                float d = Poly.DistanceToBoundary(r2, c);
                if (d < 6f) Assert.True(math.abs(t.TargetHeight - site.HeightAt(c)) < 0.2f * math.max(1f, d) + 0.3f, $"step at the outline ({d:0.0} m)");
            }
        }

        [Fact]
        public void IsInTheParksGroupWithDeepTidyOptions()
        {
            var g = TestKit.Gen(Id);
            Assert.Equal("parks", GeneratorGroups.Of(g));
            Assert.InRange(g.Controls.Count(c => !Citymaker.Core.Decals.DressingControls.IsDressingId(c.Id)), 15, 40);
            Assert.InRange(g.Controls.Where(c => !Citymaker.Core.Decals.DressingControls.IsDressingId(c.Id)).Select(c => c.Group).Distinct().Count(), 4, 9);
            Assert.Equal("identity", g.Controls[0].Group);
            Assert.Contains(Features.Of(g), f => f.Layer == "apiary" && f.DefaultOn);
            Assert.Contains(Features.Of(g), f => f.Layer == "farmStand" && f.DefaultOn);
        }

        private static AreaPlan PlanPreset(string preset, float3[] ring = null)
        {
            var g = TestKit.Gen(Id);
            var p = g.Presets.Single(x => x.Id == preset);
            return g.Plan(TestKit.Input(ring ?? FarmTypeChecks.FiveHa, p.Values, 9, null, TestKit.RoadAlongBottom(250)), CancellationToken.None);
        }

        [Fact]
        public void OrchardIsFruitTreesInRowsWithPathsGladeAndPicnics()
        {
            var plan = PlanPreset("apple");
            Assert.True(plan.Facts["fruitTrees"] > 300, "fruit trees " + plan.Facts["fruitTrees"]);
            Assert.True(plan.Objects.Count(o => o.Category == AssetCategories.FruitTree) > 300);
            Assert.True(plan.Facts["aisles"] > 10);
            Assert.True(plan.Facts["meadows"] > 0);
            Assert.Equal(1.0, plan.Facts["glade"]);
            Assert.Contains(plan.Regions, r => r.Key == "glade" && r.Tags.Contains(RegionTags.Core));
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.Picnic);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.FruitCrate);
            Assert.True(plan.Nets.Count(n => n.Role == NetRole.Pathway) >= 4);
            // Trees are on a lattice: most nearest-neighbour distances equal the spacing.
            var trees = plan.Objects.Where(o => o.Category == AssetCategories.FruitTree).Select(o => o.Position.xz).ToList();
            int onGrid = 0;
            foreach (var t in trees.Take(200))
            {
                float best = trees.Where(q => !q.Equals(t)).Min(q => math.distance(q, t));
                if (math.abs(best - 7f) < 0.2f) onGrid++;
            }
            Assert.True(onGrid > 150, "trees not on a 7 m grid: " + onGrid);
        }

        [Fact]
        public void CherryAvenueIsLinedOnBothSides()
        {
            var plan = PlanPreset("cherryBlossom");
            var avenue = plan.Nets.Where(n => n.Role == NetRole.PathwayWide).ToList();
            Assert.NotEmpty(avenue);
            int lined = plan.Objects.Count(o => o.Role == ObjectRole.Tree && avenue.Any(n => math.abs(TestKit.DistanceToCurve(o.Position.xz, n) - (Composer.DefaultHalfWidth(NetRole.PathwayWide) + 3f)) < 0.1f));
            Assert.True(lined > 20, "avenue trees " + lined);
            // s33: the orchard's lawn is painted terrain by default (no grass surface).
            Assert.DoesNotContain(plan.Surfaces, s => s.Role == SurfaceRole.Grass && Poly.Area(Poly.To2D(s.Ring)) > 5000f);

            Assert.True(TestKit.Stat(plan, Keys.StatLamps) > 0);
        }

        [Fact]
        public void CitrusHasAWindbreakInsideTheHedge()
        {
            var plan = PlanPreset("citrus");
            Assert.True(plan.Facts["windbreak"] > 10, "windbreak " + plan.Facts["windbreak"]);
            Assert.True(plan.Facts["hedge"] > 50, "hedge " + plan.Facts["hedge"]);
        }

        [Fact]
        public void VarietyBlocksShareOneVariation()
        {
            var plan = PlanPreset("community");
            var trees = plan.Objects.Where(o => o.Category == AssetCategories.FruitTree).ToList();
            int varieties = trees.Select(t => t.Variation).Distinct().Count();
            Assert.True(varieties >= 4 && varieties <= trees.Count / 5, varieties + " varieties for " + trees.Count + " trees");
        }

        [Fact]
        public void MissingFruitTreesDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.FruitTree] = 0, [AssetCategories.FruitCrate] = 0, [AssetCategories.FarmHay] = 0, [AssetCategories.Picnic] = 0,
                [AssetCategories.Beehive] = 0, [AssetCategories.MarketStall] = 0, [AssetCategories.Kiosk] = 0,
            };
            using (AssetAvailability.Scope(none))
            {
                var plan = FarmTypeChecks.Compose(FarmTypeChecks.With(Id), FarmTypeChecks.FiveHa, TestKit.RoadAlongBottom(250));
                Assert.True(string.IsNullOrEmpty(FarmTypeChecks.Errors(plan)), FarmTypeChecks.Errors(plan));
                Assert.Contains(plan.AllIssues(), i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                foreach (var l in plan.Layers) Assert.DoesNotContain(l.Objects, o => none.ContainsKey(o.Category ?? ""));
                // Still an orchard of ordinary trees in rows (the category falls back to "tree").
                Assert.True(plan.Layers[0].Facts["fruitTrees"] > 100);
            }
        }
    }
}
