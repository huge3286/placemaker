// Zen garden (type wave 6b): presets on 1 ha and a 6 ha L-shape, determinism, reserved space, speed,
// features, keys, and the garden's own promises — odd rock groupings inside the gravel, raked strokes
// inside the field, bridges over a permanent pond, a tea house setting, lanterns, stepping stones.

using System.Collections.Generic;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Layers.Types.ZenGarden;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;
using static Citymaker.Tests.Types.TypeWaveKit06;

namespace Citymaker.Tests.Types
{
    [Collection(Citymaker.Tests.TimingCollection.Name)]
    public class ZenGardenTests
    {
        private const string Id = "zenGarden";
        private readonly ITestOutputHelper _out;
        public ZenGardenTests(ITestOutputHelper output) { _out = output; }

        public static IEnumerable<object[]> Presets() => ZenGardenLayer.Styles.Select(s => new object[] { s });

        [Theory]
        [MemberData(nameof(Presets))]
        public void PresetPlansOneHectareCleanly(string preset) => AssertPresetClean(Id, preset, OneHa, TestKit.RoadAlongBottom(100));

        [Theory]
        [MemberData(nameof(Presets))]
        public void PresetPlansSixHectareLShapeCleanly(string preset)
        {
            var plan = AssertPresetClean(Id, preset, SixHaL, TestKit.RoadAlongBottom(293));
            _out.WriteLine($"{preset}: gravel {plan.Facts["gravelArea"]:0} m², groups {plan.Facts["rockGroups"]}, bridges {plan.Facts["bridges"]}, " +
                           $"lanterns {plan.Facts["lanterns"]}, stones {plan.Facts["steppingStones"]}, water {plan.Facts["waterArea"]:0} m², objects {plan.Objects.Count}");
        }

        [Theory]
        [MemberData(nameof(Presets))]
        public void PresetIsDeterministic(string preset) => AssertDeterministic(Id, preset, SixHaL, TestKit.RoadAlongBottom(293));

        [Theory]
        [MemberData(nameof(Presets))]
        public void PresetKeepsOutOfReservedSpace(string preset) => AssertRespectsReserved(Id, preset);

        [Theory]
        [MemberData(nameof(Presets))]
        public void PresetIsFastOnFiveHectares(string preset)
        {
            long ms = TimeFiveHa(Id, preset);
            _out.WriteLine($"{preset}: {ms} ms");
            Assert.True(ms < TimingBudget.Ms(250), $"{preset} took {ms} ms for 5 ha");
        }

        [Fact]
        public void FeaturesCompose() => AssertFeaturesCompose(Id);

        [Fact]
        public void EveryKeyHasEnglishText() => AssertKeys(Id, "ZenGarden", "zenGarden.json",
            AssetCategories.ZenLantern, AssetCategories.ZenTree, AssetCategories.ZenBamboo, AssetCategories.ZenSteppingStone,
            AssetCategories.ZenTeaHouse, AssetCategories.ZenBasin);

        [Fact]
        public void SchemaIsDeepButTidy() => AssertSchemaShape(Id, "style", "gardens");

        [Fact]
        public void DryGardenHasOddRockGroupsInRakedGravel()
        {
            // rf-parks: the dry garden style lays its field as a gravel surface; the painted raking is the "gravel surface: off" look.
            var dry = PlanPreset(Id, "karesansui", FiveHa, TestKit.RoadAlongBottom(250));
            Assert.Equal(5, (int)dry.Facts["fieldGroups"]);
            Assert.Contains(dry.Surfaces, s => s.Role == SurfaceRole.Ground);
            var preset = TestKit.Gen(Id).Presets.Single(p => p.Id == "karesansui");
            var values = preset.Values.Clone();
            values.Values["gravelSurface"] = false;
            values.Values["fieldShape"] = "rect";
            var plan = TestKit.Gen(Id).Plan(TestKit.Input(FiveHa, values, 7, null, TestKit.RoadAlongBottom(250)), System.Threading.CancellationToken.None);
            Assert.Equal(5, (int)plan.Facts["fieldGroups"]);
            Assert.True(plan.Facts["gravelArea"] > 2000);
            Assert.True(plan.Facts["rakeStrokes"] > 30, "strokes " + plan.Facts["rakeStrokes"]);
            Assert.Equal(1, (int)plan.Facts["veranda"]);
            var field = Poly.To2D(plan.Regions.Single(r => r.Tags.Contains(ZenGardenLayer.GravelPlace)).Ring);
            var rocks = plan.Objects.Where(o => o.Category == AssetCategories.Rock && Poly.Contains(field, o.Position.xz)).ToList();
            Assert.NotEmpty(rocks);
            // Every group has an odd number of stones; the classic 5-3-3-1-3 rhythm gives 15 in the field.
            Assert.Equal(0, (int)plan.Facts["evenGroups"]);
            Assert.True(rocks.Count % 2 == 1, rocks.Count + " stones in the field");
            // Raked strokes stay inside the field.
            var strokes = plan.Paint.Skip(0).Where(p => p.Layer == 3 && p.Strength > 0.85f && p.Strength < 0.95f).ToList();
            Assert.NotEmpty(strokes);
            foreach (var s in strokes)
                foreach (var v in s.Ring) Assert.True(Poly.Contains(field, v.xz) || Poly.DistanceToBoundary(field, v.xz) < 0.3f);
            // Rocks are scaled: a dominant stone in each group.
            Assert.Contains(rocks, o => o.Scale > 1.2f);
        }

        private static List<int> ClusterSizes(List<float2> pts, float link)
        {
            var group = Enumerable.Range(0, pts.Count).ToArray();
            int Find(int i) => group[i] == i ? i : group[i] = Find(group[i]);
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                    if (math.distance(pts[i], pts[j]) < link) group[Find(i)] = Find(j);
            return Enumerable.Range(0, pts.Count).GroupBy(Find).Select(g => g.Count()).ToList();
        }

        [Fact]
        public void StrollGardenBridgesAPermanentPond()
        {
            var plan = PlanPreset(Id, "stroll", SixHaL, TestKit.RoadAlongBottom(293));
            Assert.True(plan.Facts["waterArea"] > 2000, "water " + plan.Facts["waterArea"]);
            Assert.True(plan.Facts["bridges"] >= 1);
            Assert.Contains(plan.Issues, i => i.Key == ZenGardenLayer.PondPermanentKey && i.Severity == IssueSeverity.Warning);
            Assert.Contains(plan.Terraform, t => !float.IsNaN(t.WaterLevel));
            Assert.True(plan.Facts["lanterns"] >= 6);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.ZenLantern);
            // Lanterns and trees never stand in the water.
            var ponds = plan.Regions.Where(r => r.Tags.Contains(ZenGardenLayer.WaterPlace)).Select(r => Poly.To2D(r.Ring)).ToList();
            Assert.NotEmpty(ponds);
            foreach (var o in plan.Objects.Where(o => o.Role == ObjectRole.Lamp || o.Role == ObjectRole.Bench))
                Assert.DoesNotContain(ponds, p => Poly.Contains(p, o.Position.xz));
        }

        [Fact]
        public void TeaGardenHasTheRojiSetting()
        {
            var plan = PlanPreset(Id, "tea", SixHaL, TestKit.RoadAlongBottom(293));
            Assert.Equal(1, (int)plan.Facts["teaHouse"]);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.ZenTeaHouse);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.ZenBasin);
            Assert.True(plan.Facts["steppingStones"] > 20, "stones " + plan.Facts["steppingStones"]);
            Assert.Contains(plan.Objects, o => o.Category == AssetCategories.ZenBamboo);
        }

        [Fact]
        public void StylesAreVisiblyDifferent()
        {
            var plans = ZenGardenLayer.Styles.ToDictionary(s => s, s => PlanPreset(Id, s, SixHaL, TestKit.RoadAlongBottom(293)));
            Assert.Equal(plans.Count, plans.Values.Select(TestKit.Fingerprint).Distinct().Count());
            int Bamboo(AreaPlan p) => p.Objects.Count(o => o.Category == AssetCategories.ZenBamboo);
            // The grove is massed with a leaf-litter floor surface plus clumps (ground before props), the screen is a border.
            Assert.True(Bamboo(plans["bamboo"]) > Bamboo(plans["karesansui"]));
            Assert.Contains(plans["bamboo"].Surfaces, s => s.Category == Citymaker.Core.Build.SurfaceCategories.Bark);
            Assert.True(Bamboo(plans["bamboo"]) <= 300 * 6, "grove clumps " + Bamboo(plans["bamboo"]));
            Assert.True(plans["karesansui"].Facts["gravelArea"] > 3 * plans["stroll"].Facts["gravelArea"]);
            Assert.Equal(0, plans["bamboo"].Facts["waterArea"]);
            Assert.True(plans["moss"].Objects.Count(o => o.Role == ObjectRole.Tree) > plans["karesansui"].Objects.Count(o => o.Role == ObjectRole.Tree) * 2);
            Assert.Contains(plans["bamboo"].Nets, n => n.Role == NetRole.Pathway);   // thin by default (FEEDBACK-4)
        }

        [Fact]
        public void MissingCategoriesDegradeToInfo()
        {
            var none = new Dictionary<string, int>
            {
                [AssetCategories.ZenLantern] = 0, [AssetCategories.Lamp] = 0, [AssetCategories.ZenBasin] = 0, [AssetCategories.ZenTeaHouse] = 0,
                [AssetCategories.Shelter] = 0, [AssetCategories.ZenSteppingStone] = 0, [AssetCategories.Rock] = 0, [AssetCategories.ZenBamboo] = 0,
                [AssetCategories.Bush] = 0,
            };
            using (AssetAvailability.Scope(none))
            {
                var plan = PlanPreset(Id, "tea", FiveHa, TestKit.RoadAlongBottom(250));
                Assert.False(plan.HasErrors, Errors(plan.Issues));
                Assert.Contains(plan.Issues, i => i.Key == Keys.NoAssetsIssue && i.Severity == IssueSeverity.Info);
                // rf-parks: the rock groups use named vanilla boulders while the rock control is automatic (resolved by name, like the
                // memorial's curated pieces), so only rocks other than those may not appear.
                Assert.DoesNotContain(plan.Objects, o => (none.ContainsKey(o.Category ?? "") && !(o.Category == AssetCategories.Rock && (o.Asset.Id ?? "").StartsWith("Boulder")))
                    || o.Role == ObjectRole.Lamp || o.Role == ObjectRole.Bush);
                Assert.NotEmpty(plan.Nets);
            }
        }

        [Fact]
        public void TeaHouseFeatureWorksInAPark()
        {
            var recipe = With("park");
            var gen = TestKit.Gen(TeaHouseAddOn.LayerId);
            recipe.Root.Children.Add(new RecipeStep { Layer = gen.Id, Settings = gen.Defaults(), Seed = 5 });
            var plan = Compose(recipe, FiveHa, TestKit.RoadAlongBottom(250));
            Assert.True(string.IsNullOrEmpty(Errors(plan.AllIssues())), Errors(plan.AllIssues()));
            var tea = plan.Layers.Single(l => l.GeneratorId == TeaHouseAddOn.LayerId);
            Assert.Contains(tea.Objects, o => o.Category == AssetCategories.ZenTeaHouse);
        }

        [Theory]
        [InlineData("StoneLantern01", AssetCategories.ZenLantern)]
        [InlineData("JapaneseLantern02", AssetCategories.ZenLantern)]
        [InlineData("SteppingStone01", AssetCategories.ZenSteppingStone)]
        [InlineData("TeaHouse01", AssetCategories.ZenTeaHouse)]
        [InlineData("Tsukubai01", AssetCategories.ZenBasin)]
        [InlineData("BambooClump01", AssetCategories.ZenBamboo)]
        public void Classification(string name, string category)
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            Assert.Contains(category, AssetHeuristics.Classify(name, t));
            Assert.True(AssetHeuristics.AutoEligible(category, name, t), name + " should be automatic for " + category);
        }

        [Fact]
        public void LanternsArePlainLampsTooAndBambooFencesStayFences()
        {
            var t = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon;
            Assert.Contains(AssetCategories.Lamp, AssetHeuristics.Classify("StoneLantern01", t));
            Assert.DoesNotContain(AssetCategories.ZenBamboo, AssetHeuristics.Classify("BambooFence01", t));
            var tree = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.Plant | AssetTraits.Tree;
            var pine = AssetHeuristics.Classify("JapanesePine01", tree);
            Assert.Contains(AssetCategories.ZenTree, pine);
            Assert.Contains(AssetCategories.Tree, pine);
        }
    }
}
