using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Common;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class AddOnTests
    {
        public static readonly string[] AddOns = { "fountain", "playground", "restrooms", "picnic", "bigtrees", "flowerbeds" };
        public static IEnumerable<object[]> Ids() => AddOns.Select(a => new object[] { a });

        /// <summary>A lawn crossed by a path and with a few trees, as a park would leave it.</summary>
        private static AreaInput Lawn(string id, uint seed = 1)
        {
            var input = TestKit.Input(TestKit.Rect(120, 90), seed: seed);
            input.Reserved.AddCorridor(new float2(0, 45), new float2(120, 45), 1.5f, nameof(NetRole.Pathway));
            input.Reserved.AddDisc(new float2(20, 20), 2.5f, "Tree");
            input.Reserved.AddDisc(new float2(90, 70), 2.5f, "Tree");
            input.Reserved.AddDisc(new float2(60, 20), 6f, "Centerpiece");
            return input;
        }

        [Theory]
        [MemberData(nameof(Ids))]
        public void PlacesSomethingInsideAndClearOfReserved(string id)
        {
            var input = Lawn(id);
            var plan = TestKit.Plan(id, input);
            Assert.False(plan.HasErrors);
            Assert.NotEmpty(plan.Objects);
            Assert.Equal(LayerKind.AddOn, TestKit.Gen(id).Kind);
            TestKit.AssertObjectsInside(plan, input.Shape.Ring);
            TestKit.AssertClearOfReserved(plan, input.Reserved);
            TestKit.AssertSurfacesAccepted(plan);
        }

        [Theory]
        [MemberData(nameof(Ids))]
        public void Deterministic(string id)
        {
            Assert.Equal(TestKit.Fingerprint(TestKit.Plan(id, Lawn(id, 9))), TestKit.Fingerprint(TestKit.Plan(id, Lawn(id, 9))));
        }

        [Theory]
        [MemberData(nameof(Ids))]
        public void FullRegionReportsNoSpaceInsteadOfOverlapping(string id)
        {
            var input = TestKit.Input(TestKit.Rect(40, 30));
            // Everything is taken.
            for (float x = 0; x <= 40; x += 3) for (float z = 0; z <= 30; z += 3) input.Reserved.AddDisc(new float2(x, z), 2.5f, "Tree");
            var plan = TestKit.Plan(id, input);
            Assert.Empty(plan.Objects);
            Assert.False(plan.HasErrors);
            Assert.Contains(plan.Issues, i => i.Key == Keys.NoSpace);
        }

        [Theory]
        [InlineData("fountain", AssetCategories.Centerpiece)]
        [InlineData("playground", AssetCategories.Playground)]
        [InlineData("restrooms", AssetCategories.Restroom)]
        [InlineData("picnic", AssetCategories.Picnic)]
        [InlineData("bigtrees", AssetCategories.Tree)]
        [InlineData("flowerbeds", AssetCategories.Flowerbed)]
        public void MissingCategoryMeansInfoAndNothingPlaced(string id, string category)
        {
            using (AssetAvailability.Scope(new Dictionary<string, int> { [category] = 0 }))
            {
                var plan = TestKit.Plan(id, Lawn(id));
                Assert.Empty(plan.Objects);
                var issue = Assert.Single(plan.Issues, i => i.Key == Keys.NoAssetsIssue);
                Assert.Equal(IssueSeverity.Info, issue.Severity);
                Assert.Equal(Keys.Category(category), issue.Args[0]);
            }
        }

        [Fact]
        public void BiggerFountainTakesMoreRoom()
        {
            var small = new SettingsBag(); small.Values["size"] = "small";
            var grand = new SettingsBag(); grand.Values["size"] = "grand";
            var a = TestKit.Plan("fountain", TestKit.Input(TestKit.Rect(120, 120), small));
            var b = TestKit.Plan("fountain", TestKit.Input(TestKit.Rect(120, 120), grand));
            Assert.True(b.Facts["fountainRadius"] > a.Facts["fountainRadius"] * 2);
            Assert.True(Poly.Area(Poly.To2D(b.Surfaces[0].Ring)) > Poly.Area(Poly.To2D(a.Surfaces[0].Ring)) * 3);
            // A grand fountain in a small square shrinks rather than failing.
            var c = TestKit.Plan("fountain", TestKit.Input(TestKit.Rect(30, 30), grand));
            Assert.Equal(1, TestKit.Count(c, ObjectRole.Centerpiece));
            Assert.Contains(c.Issues, i => i.Key == "Citymaker.AddOn.Fountain.Issue.Smaller");
        }

        [Fact]
        public void PlaygroundSitsNearThePath()
        {
            var plan = TestKit.Plan("playground", Lawn("playground"));
            var items = plan.Objects.Where(o => o.Role == ObjectRole.Furniture).ToList();
            Assert.InRange(items.Count, 3, 8);
            var centre = items.Aggregate(float2.zero, (s, o) => s + o.Position.xz) / items.Count;
            Assert.True(math.abs(centre.y - 45f) < 30f, "playground far from the path: " + centre);
        }

        [Fact]
        public void BigTreesKeepTheirSpacing()
        {
            var s = new SettingsBag(); s.Values["density"] = 10.0; s.Values["spacing"] = 20.0;
            var plan = TestKit.Plan("bigtrees", TestKit.Input(TestKit.Rect(200, 200), s));
            var trees = plan.Objects.Where(o => o.Role == ObjectRole.Tree).ToList();
            Assert.True(trees.Count >= 20);
            for (int i = 0; i < trees.Count; i++)
                for (int j = i + 1; j < trees.Count; j++)
                    Assert.True(math.distance(trees[i].Position.xz, trees[j].Position.xz) >= 20f - 1e-3f);
            Assert.All(trees, t => Assert.Equal(3, t.Age));
        }

        [Fact]
        public void ParkWithPlaygroundRecipeEndToEnd()
        {
            var recipe = TestKit.LoadRecipe("park-with-playground.json");
            var ring = TestKit.Rect(220, 160);
            var plan = Composer.Compose(recipe, new AreaShape { Ring = ring }, TestKit.RoadAlongBottom(220), CancellationToken.None);
            Assert.False(plan.HasErrors);
            var park = plan.Layers.Single(l => l.GeneratorId == "park");
            var playground = plan.Layers.Single(l => l.GeneratorId == "playground");
            var restrooms = plan.Layers.Single(l => l.GeneratorId == "restrooms");
            Assert.EndsWith("@green:0", playground.StepPath);
            Assert.NotEmpty(playground.Objects);
            Assert.NotEmpty(restrooms.Objects);
            // Add-ons respect everything the park built (paths, trees, furniture).
            var parkReserved = new ReservedSpace();
            Composer.Reserve(park, parkReserved);
            TestKit.AssertClearOfReserved(playground, parkReserved);
            TestKit.AssertClearOfReserved(restrooms, parkReserved);
            // … and restrooms (a later sibling) respect the playground.
            var playReserved = new ReservedSpace();
            Composer.Reserve(playground, playReserved);
            TestKit.AssertClearOfReserved(restrooms, playReserved);
        }

        [Fact]
        public void CampgroundRecipeEndToEnd()
        {
            var recipe = TestKit.LoadRecipe("campground.json");
            var ring = TestKit.Rect(200, 150);
            var plan = Composer.Compose(recipe, new AreaShape { Ring = ring }, TestKit.RoadAlongBottom(200), CancellationToken.None);
            Assert.False(plan.HasErrors);
            var camp = plan.Layers.Single(l => l.GeneratorId == "campground");
            Assert.True(TestKit.Stat(camp, Keys.StatPitches) >= 10, "pitches " + TestKit.Stat(camp, Keys.StatPitches));
            Assert.True(TestKit.Stat(camp, Keys.StatRestrooms) >= 1);
            Assert.NotEmpty(camp.Nets);
            TestKit.AssertObjectsInside(camp, ring);
            TestKit.AssertObjectsClearOfNets(camp);
            TestKit.AssertSurfacesAccepted(camp);
            // (rf-nature) A single type: the shipped recipe has no child steps, so the Make row offers it (picnic is an
            // optional feature of the campground; pitches have their own tables).
            Assert.Empty(recipe.Root.Children);
            Assert.True(Citymaker.Core.Tool.Features.ShowInMakeRow(recipe, false, GeneratorRegistry.Get));
        }

        [Fact]
        public void RecipeOfAddOnsOnAPlaza()
        {
            // plaza → fountain in the core (the plaza centre is empty) + flowerbeds along its edges.
            var recipe = new Recipe
            {
                Root = new RecipeStep
                {
                    Layer = "plaza", Settings = Settings(("centerMode", "none")),
                    Children =
                    {
                        new RecipeStep { Layer = "fountain", Target = "core", Settings = Settings(("size", "large")) },
                        new RecipeStep { Layer = "flowerbeds", Target = "edge", Settings = Settings(("nearPaths", false)) },
                    },
                },
            };
            var plan = Composer.Compose(recipe, new AreaShape { Ring = TestKit.Rect(90, 70) }, new SiteContext(), CancellationToken.None);
            Assert.False(plan.HasErrors);
            Assert.Equal(1, plan.Layers.Where(l => l.GeneratorId == "fountain").Sum(l => TestKit.Count(l, ObjectRole.Centerpiece)));
            Assert.True(plan.Layers.Where(l => l.GeneratorId == "flowerbeds").Sum(l => l.Objects.Count) > 0);
        }

        private static SettingsBag Settings(params (string k, object v)[] values)
        {
            var b = new SettingsBag();
            foreach (var (k, v) in values) b.Values[k] = v;
            return b;
        }
    }
}
