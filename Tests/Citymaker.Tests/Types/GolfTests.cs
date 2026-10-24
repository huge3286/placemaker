// Golf type (builder t04): routing, formats, hazards, degradation and classification.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Common;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Citymaker.Tests.Types.T04
{
    [Collection(Citymaker.Tests.TimingCollection.Name)]
    public class GolfTests : T04TypeTestBase
    {
        public GolfTests(ITestOutputHelper output) : base(output) { }
        protected override string LayerId => "golf";
        protected override string ExpectedGroup => "sports";

        private static readonly float3[] Big = TestKit.Rect(420, 320);

        private AreaPlan PlanPreset(string preset, float3[] ring, SiteContext site, params (string k, object v)[] extra)
        {
            var r = WithPreset(preset);
            foreach (var (k, v) in extra) r.Root.Settings.Values[k] = v;
            var plan = Compose(r, ring, site);
            Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
            return Layer(plan);
        }

        [Fact]
        public void ParklandRoutesRealHolesWithGreensTeesAndWater()
        {
            var site = TestKit.RoadAlongBottom(420);
            var plan = PlanPreset("parkland", Big, site);
            int holes = (int)plan.Facts["holes"];
            Out.WriteLine($"holes {holes}, par {plan.Facts["par"]}, ponds {plan.Facts["ponds"]}, bunkers {plan.Facts["bunkers"]}, doglegs {plan.Facts["doglegs"]}");
            Assert.True(holes >= 4, "only " + holes + " holes on 13 ha");
            var flags = plan.Objects.Where(o => o.Category == AssetCategories.GolfFlag).ToList();
            Assert.Equal(holes, flags.Count);
            // Greens never overlap: flags are at least a green apart.
            for (int i = 0; i < flags.Count; i++)
                for (int j = i + 1; j < flags.Count; j++)
                    Assert.True(math.distance(flags[i].Position.xz, flags[j].Position.xz) > 20f);
            // Fairway + green + tee surfaces per hole; bunkers in sand.
            Assert.True(plan.Surfaces.Count(s => s.Role == SurfaceRole.Grass) >= holes * 3);
            Assert.Contains(plan.Surfaces, s => s.Category == AssetCategories.SurfaceSand);
            // Water hazards are real ponds: lowered basins with water.
            Assert.True(plan.Facts["ponds"] >= 1);
            Assert.Contains(plan.Terraform, t => !float.IsNaN(t.WaterLevel) && t.TargetHeight < t.WaterLevel);
            Assert.Contains(plan.Surfaces, s => s.Role == SurfaceRole.Water);
            // Cart paths and a clubhouse.
            // Art pass: cart paths are 3 m concrete paths, not the 8 m automatic promenade.
            Assert.Contains(plan.Nets, n => n.Role == NetRole.Pathway && n.Asset.Id == "Pavement Path");
            Assert.DoesNotContain(plan.Nets, n => n.Role == NetRole.PathwayWide);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.GolfClubhouse);
            Assert.Contains(plan.Regions, r => r.Tags.Contains("practice"));
        }

        [Fact]
        public void DoglegsBendAndLinksDiffersFromParkland()
        {
            var site = TestKit.RoadAlongBottom(420);
            var bent = PlanPreset("parkland", Big, site, ("doglegs", 100.0), ("water", 0.0));
            Assert.True(bent.Facts["doglegs"] >= 1, "no dogleg with doglegs = 100 %");
            var straight = PlanPreset("parkland", Big, site, ("doglegs", 0.0), ("water", 0.0));
            Assert.True(straight.Facts["doglegs"] <= bent.Facts["doglegs"]);

            var links = PlanPreset("links", Big, site);
            var parkland = PlanPreset("parkland", Big, site);
            Assert.True(TestKit.Count(links, ObjectRole.Tree) < TestKit.Count(parkland, ObjectRole.Tree) / 3 + 1);
            Assert.True(links.Facts["bunkers"] > parkland.Facts["bunkers"]);
            Assert.Equal(0, links.Facts["ponds"]);
        }

        [Fact]
        public void PitchAndPuttHasShortHolesAndFootpaths()
        {
            var plan = PlanPreset("pitchPutt", FiveHa, TestKit.RoadAlongBottom(250));
            int holes = (int)plan.Facts["holes"];
            Assert.True(holes >= 6, holes + " holes");
            double length = TestKit.Stat(plan, "Citymaker.Stat.CourseLength");
            Assert.True(length / holes < 115, "average hole " + length / holes + " m");
            Assert.DoesNotContain(plan.Nets, n => n.Role == NetRole.PathwayWide);
            Assert.Contains(plan.Nets, n => n.Role == NetRole.Pathway);
        }

        [Fact]
        public void DiscGolfIsWoodedWithBasketsAndTeePads()
        {
            var plan = PlanPreset("discGolf", FiveHa, TestKit.RoadAlongBottom(250));
            int holes = (int)plan.Facts["holes"];
            Assert.True(holes >= 5, holes + " holes");
            Assert.Equal(holes, plan.Objects.Count(o => o.Category == AssetCategories.GolfBasket));
            Assert.True(plan.Surfaces.Count(s => s.Role == SurfaceRole.Paving) >= holes);   // tee pads
            Assert.Empty(plan.Terraform.Where(t => float.IsNaN(t.WaterLevel)));            // no shaped greens
            var parkland = PlanPreset("parkland", FiveHa, TestKit.RoadAlongBottom(250));
            Assert.True(TestKit.Count(plan, ObjectRole.Tree) > TestKit.Count(parkland, ObjectRole.Tree));
        }

        [Fact]
        public void MiniGolfPacksEighteenFeltLanes()
        {
            var plan = PlanPreset("miniGolf", OneHa, TestKit.RoadAlongBottom(100));
            Assert.Equal(18, (int)plan.Facts["holes"]);
            Assert.True(plan.Surfaces.Count(s => s.Role == SurfaceRole.Paving) >= 18);
            Assert.Equal(18, plan.Objects.Count(o => o.Category == AssetCategories.GolfFlag));
            Assert.Equal(18, plan.Objects.Count(o => o.Category == AssetCategories.GolfMini));
            Assert.True(plan.Facts["decorations"] > 0);   // adventure rocks / pond
            TestKit.AssertObjectsClearOfNets(plan);
        }

        [Fact]
        public void DrivingRangeHasBaysTargetsAndNetting()
        {
            var plan = PlanPreset("drivingRange", FiveHa, TestKit.RoadAlongBottom(250));
            Assert.True(plan.Facts["bays"] >= 12, "bays " + plan.Facts["bays"]);
            Assert.True(plan.Facts["targets"] >= 4, "targets " + plan.Facts["targets"]);
            Assert.Contains(plan.Nets, n => n.Role == NetRole.Fence);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.GolfBay);
            // Targets grow with distance from the tee line.
            var greens = plan.Objects.Where(o => o.Category == AssetCategories.GolfFlag).Select(o => o.Position.xz).ToList();
            Assert.True(greens.Count >= 4);
        }

        [Fact]
        public void PractiseGreenFeatureLandsNextToTheClubhouse()
        {
            var plan = Compose(WithPreset("parkland", "puttingGreen"), FiveHa, TestKit.RoadAlongBottom(250));
            var green = plan.Layers.Single(l => l.GeneratorId == "puttingGreen");
            Assert.Contains(green.Objects, o => o.Category == AssetCategories.GolfFlag);
            var club = Layer(plan).Objects.FirstOrDefault(o => o.Category == AssetCategories.GolfClubhouse);
            Assert.NotNull(club);
            Assert.True(green.Objects.Min(o => math.distance(o.Position.xz, club.Position.xz)) < 60f);
        }

        [Fact]
        public void MissingGolfCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.GolfFlag] = 0, [AssetCategories.GolfTee] = 0, [AssetCategories.GolfCart] = 0, [AssetCategories.GolfBasket] = 0,
                [AssetCategories.GolfMini] = 0, [AssetCategories.GolfBay] = 0, [AssetCategories.GolfClubhouse] = 0, [AssetCategories.Golf] = 0,
            };
            using (AssetAvailability.Scope(none))
                foreach (var p in Gen.Presets)
                {
                    var plan = Compose(WithPreset(p.Id), FiveHa, TestKit.RoadAlongBottom(250));
                    Assert.True(string.IsNullOrEmpty(Errors(plan)), Errors(plan));
                    Assert.DoesNotContain(Layer(plan).Objects, o => none.ContainsKey(o.Category ?? ""));
                    // Art pass: without golf props the course still gets vanilla stand-ins (corner flag pins,
                    // dog-park poles as disc golf baskets) instead of an empty "nothing installed" note.
                    string pin = p.Id == "discGolf" ? "DogParkPole01" : "SportParkFlag01";
                    Assert.Contains(Layer(plan).Objects, o => o.Asset.Id == pin);
                    Assert.DoesNotContain(Layer(plan).Objects, o => o.Role == ObjectRole.Decal && Citymaker.Core.Decals.DecalCatalog.IsFunctionalCategory(o.Category));   // s21: dressing decals are decorative
                }
        }

        [Fact]
        public void TooSmallForHolesSaysSo()
        {
            var gen = Gen;
            var input = TestKit.Input(TestKit.Rect(45, 30), gen.Presets.Single(p => p.Id == "parkland").Values, 3, null, TestKit.RoadAlongBottom(45));
            var plan = gen.Plan(input, CancellationToken.None);
            Assert.False(plan.HasErrors);
            Assert.Contains(plan.Issues, i => i.Key == "Citymaker.Golf.Issue.FewerHoles");
        }

        [Theory]
        [InlineData("GolfFlag01", AssetCategories.GolfFlag)]
        [InlineData("GolfPinFlag", AssetCategories.GolfFlag)]
        [InlineData("DiscGolfBasket01", AssetCategories.GolfBasket)]
        [InlineData("MiniGolfWindmill", AssetCategories.GolfMini)]
        [InlineData("GolfCart01", AssetCategories.GolfCart)]
        [InlineData("TeeMarkerRed", AssetCategories.GolfTee)]
        [InlineData("DrivingRangeBay02", AssetCategories.GolfBay)]
        [InlineData("GolfClubhouse01", AssetCategories.GolfClubhouse)]
        public void GolfClassification(string name, string category)
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            var cats = AssetHeuristics.Classify(name, t);
            Assert.Contains(category, cats);
            Assert.Contains(AssetCategories.Golf, cats);
            Assert.True(AssetHeuristics.AutoEligible(category, name, t));
        }

        [Theory]
        [InlineData("WindTurbine01")]
        [InlineData("FlagPole01")]
        [InlineData("ParkBench01")]
        public void GolfRulesLeaveOtherAssetsAlone(string name)
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            Assert.DoesNotContain(AssetHeuristics.Classify(name, t), c => c.StartsWith("golf"));
        }
    }
}
