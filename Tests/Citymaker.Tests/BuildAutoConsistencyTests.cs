using System.Collections.Generic;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Layers.Common;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    /// <summary>FEEDBACK-2 #1: consistent automatic assets, curated automatic pools, joined fences.</summary>
    public class BuildAutoConsistencyTests
    {
        private static readonly Dictionary<string, string[]> Pools = new Dictionary<string, string[]>
        {
            [AssetCategories.Tree] = new[] { "Oak", "Birch", "Pine", "Maple" },
            [AssetCategories.Bench] = new[] { "GardenBench01", "GardenBench02" },
            [AssetCategories.Lamp] = new[] { "LightpolePark01" },
            [AssetCategories.Fence] = new[] { "FenceNet", "FencePiece01", "FencePiece02" },
            [AssetCategories.Playground] = new[] { "Swing", "Slide", "Seesaw" },
            [AssetCategories.Decoration] = new[] { "Boulder01" },
        };

        private static readonly Dictionary<string, Dictionary<string, string[]>> PaletteData = new Dictionary<string, Dictionary<string, string[]>>
        {
            ["palette:CityPark01"] = new Dictionary<string, string[]>
            {
                [AssetCategories.Tree] = new[] { "Linden", "Elm", "Ash" },
                [AssetCategories.Bench] = new[] { "CityBench" },
            },
            ["palette:PocketPark02"] = new Dictionary<string, string[]>
            {
                [AssetCategories.Tree] = new[] { "Cherry" },
                [AssetCategories.Bench] = new[] { "PocketBench" },
            },
        };

        private static AutoAssetSource Source(bool palettes) => new AutoAssetSource
        {
            Pool = c => Pools.TryGetValue(c, out var p) ? p : new string[0],
            Palettes = palettes ? PaletteData.Keys.OrderBy(k => k, System.StringComparer.Ordinal).ToList() : null,
            PaletteItems = (p, c) => PaletteData.TryGetValue(p, out var d) && d.TryGetValue(c, out var l) ? l : new string[0],
        };

        private static AreaPlan Layer(string path, uint seed, string gen = "park")
        {
            var l = new AreaPlan { StepPath = path, Seed = seed, GeneratorId = gen };
            for (uint i = 0; i < 40; i++) l.Objects.Add(new PlannedObject { Role = ObjectRole.Tree, Variation = i * 7919u });
            for (uint i = 0; i < 20; i++) l.Objects.Add(new PlannedObject { Role = ObjectRole.Bench, Variation = i * 31u });
            for (uint i = 0; i < 10; i++) l.Objects.Add(new PlannedObject { Role = ObjectRole.Lamp, Variation = i });
            for (int i = 0; i < 10; i++) l.Nets.Add(new PlannedNet { Role = NetRole.Fence, D = new float3(i, 0, 0) });
            return l;
        }

        private static ComposedPlan Plan(params AreaPlan[] layers)
        {
            var p = new ComposedPlan();
            p.Layers.AddRange(layers);
            return p;
        }

        [Fact]
        public void UniformCategoriesGetOneAssetPerBuild()
        {
            var plan = Plan(Layer("0@root", 5));
            AutoAssets.Resolve(plan, Source(false));
            var l = plan.Layers[0];
            Assert.Single(l.Objects.Where(o => o.Role == ObjectRole.Bench).Select(o => o.Asset.Id).Distinct());
            Assert.Single(l.Objects.Where(o => o.Role == ObjectRole.Lamp).Select(o => o.Asset.Id).Distinct());
            Assert.Equal(new[] { "FenceNet" }, l.Nets.Select(n => n.Asset.Id).Distinct());
            Assert.True(l.Objects.Where(o => o.Role == ObjectRole.Tree).Select(o => o.Asset.Id).Distinct().Count() > 1);   // vegetation varies
        }

        [Fact]
        public void PaletteDrivesFurnitureAndVegetationPerStep()
        {
            var plan = Plan(Layer("0@root", 5));
            AutoAssets.Resolve(plan, Source(true));
            var palette = AutoAssets.PickPalette(PaletteData.Keys.OrderBy(k => k, System.StringComparer.Ordinal).ToList(), 5);
            var data = PaletteData[palette];
            var l = plan.Layers[0];
            Assert.All(l.Objects.Where(o => o.Role == ObjectRole.Tree), o => Assert.Contains(o.Asset.Id, data[AssetCategories.Tree]));
            Assert.All(l.Objects.Where(o => o.Role == ObjectRole.Bench), o => Assert.Equal(data[AssetCategories.Bench][0], o.Asset.Id));
            // The palette has no lamps: the pool default.
            Assert.All(l.Objects.Where(o => o.Role == ObjectRole.Lamp), o => Assert.Equal("LightpolePark01", o.Asset.Id));
            // Fences never come from a palette (network fence preferred).
            Assert.All(l.Nets, n => Assert.Equal("FenceNet", n.Asset.Id));
        }

        [Fact]
        public void ResolutionIsDeterministicAndPaletteFollowsTheSeed()
        {
            string Benches(uint seed)
            {
                var plan = Plan(Layer("0@root", seed));
                AutoAssets.Resolve(plan, Source(true));
                return string.Join(",", plan.Layers[0].Objects.Select(o => o.Asset.Id));
            }
            Assert.Equal(Benches(3), Benches(3));
            var seen = new HashSet<string>();
            for (uint s = 0; s < 20; s++) seen.Add(Benches(s));
            Assert.True(seen.Count > 1, "different seeds should reach different palettes");
        }

        [Fact]
        public void AddOnsShareTheirParentsStepButSplitChildrenDoNot()
        {
            var keys = AutoAssets.StepKeys(Plan(
                new AreaPlan { StepPath = "0@root", GeneratorId = "park" },
                new AreaPlan { StepPath = "0/0@green:0", GeneratorId = "playground" },
                new AreaPlan { StepPath = "0/1@green:1", GeneratorId = "picnic" }));
            Assert.Equal(new[] { "0", "0", "0" }, keys);

            keys = AutoAssets.StepKeys(Plan(
                new AreaPlan { StepPath = "0@root", GeneratorId = "split" },
                new AreaPlan { StepPath = "0/0@part:0", GeneratorId = "park" },
                new AreaPlan { StepPath = "0/0/0@green:0", GeneratorId = "playground" },
                new AreaPlan { StepPath = "0/1@part:1", GeneratorId = "plaza" }));
            Assert.Equal(new[] { "0", "0/0", "0/0", "0/1" }, keys);

            // An add-on uses its park's palette and the park's single bench.
            var park = Layer("0@root", 11);
            var addOn = Layer("0/0@green:0", 999, "playground");
            var plan = Plan(park, addOn);
            AutoAssets.Resolve(plan, Source(true));
            Assert.Single(plan.Layers.SelectMany(l => l.Objects).Where(o => o.Role == ObjectRole.Bench).Select(o => o.Asset.Id).Distinct());
        }

        [Fact]
        public void FurnitureUsesItsDeclaredCategoryNotDecoration()
        {
            var l = new AreaPlan { StepPath = "0@root" };
            for (uint i = 0; i < 12; i++) l.Objects.Add(new PlannedObject { Role = ObjectRole.Furniture, Category = AssetCategories.Playground, Variation = i * 977u });
            l.Objects.Add(new PlannedObject { Role = ObjectRole.Furniture, Variation = 1 });
            var plan = Plan(l);
            AutoAssets.Resolve(plan, Source(false));
            var play = l.Objects.Take(12).Select(o => o.Asset.Id).ToList();
            Assert.All(play, id => Assert.Contains(id, Pools[AssetCategories.Playground]));
            Assert.True(play.Distinct().Count() > 1);              // equipment is a set
            Assert.Equal("Boulder01", l.Objects[12].Asset.Id);     // uncategorised furniture: decoration
        }

        [Theory]
        [InlineData("playground")]
        [InlineData("picnic")]
        [InlineData("restrooms")]
        [InlineData("campground")]
        public void LayersNeverEmitUncategorisedFurniture(string id)
        {
            var settings = TestKit.Gen(id).Defaults();
            settings.Values["kiosk"] = true;
            settings.Values["fence"] = true;
            var input = TestKit.Input(TestKit.Rect(160, 120), settings);
            var plan = TestKit.Plan(id, input);
            Assert.All(plan.Objects.Where(o => o.Role == ObjectRole.Furniture), o => Assert.False(string.IsNullOrEmpty(o.Category)));
        }

        [Fact]
        public void PlaygroundEquipmentIsPlaygroundCategory()
        {
            var plan = TestKit.Plan("playground", TestKit.Input(TestKit.Rect(120, 90)));
            Assert.Contains(plan.Objects, o => o.Role == ObjectRole.Furniture && o.Category == AssetCategories.Playground);
            Assert.Equal(AssetCategories.Playground, RoleCategories.ForObject(plan.Objects.First(o => o.Role == ObjectRole.Furniture)));
        }

        // ───────────────────────── curation ─────────────────────────

        private const AssetTraits Prop = AssetTraits.StaticObject | AssetTraits.HasMesh | AssetTraits.HasIcon | AssetTraits.Vanilla;

        [Theory]
        [InlineData(AssetCategories.Bench, "GardenBench01", true)]
        [InlineData(AssetCategories.Bench, "CampgroundBench01", true)]
        [InlineData(AssetCategories.Bench, "SportParkBench01", false)]       // the yellow/white stadium seat rows
        [InlineData(AssetCategories.Bench, "TennisBench01", false)]
        [InlineData(AssetCategories.Bench, "StadiumSeatRow", false)]
        [InlineData(AssetCategories.Bench, "SpectatorSeats02", false)]
        [InlineData(AssetCategories.Bench, "Bleacher Bench", false)]
        [InlineData(AssetCategories.Bench, "CafeSeat01", false)]             // a seat is not a bench
        [InlineData(AssetCategories.Decoration, "ParkSandwichBoard01", false)]
        [InlineData(AssetCategories.Decoration, "SportParkScoreboard01", false)]
        [InlineData(AssetCategories.Decoration, "ParkInfosign01", false)]
        [InlineData(AssetCategories.Decoration, "DogParkPlatform01", false)]
        [InlineData(AssetCategories.Rock, "Rock12", true)]
        [InlineData(AssetCategories.Decoration, "ConstructionCone01", false)]
        [InlineData(AssetCategories.Fence, "EU_FenceResidentialPieceLow01", true)]
        [InlineData(AssetCategories.Fence, "FenceIndustrialPieceLow01", false)]
        [InlineData(AssetCategories.Fence, "DogParkJumpFence01", false)]
        [InlineData(AssetCategories.Fence, "ConstructionBarrierFence", false)]
        [InlineData(AssetCategories.Lamp, "LightpolePark01", true)]
        [InlineData(AssetCategories.Centerpiece, "DogParkDrinkingFountain01", false)]
        [InlineData(AssetCategories.Centerpiece, "SmallFountain02", true)]
        [InlineData(AssetCategories.Centerpiece, "WallFountain01", false)]          // needs a wall (playtest 4b)
        [InlineData(AssetCategories.Bench, "OutdoorSofa01", false)]
        [InlineData(AssetCategories.Bench, "HighSchool03 Rooftop Garden", false)]   // building extension with a bench activity
        [InlineData(AssetCategories.Bush, "GardenBed01WideHigh", false)]          // raised bed = planter box
        [InlineData(AssetCategories.Planter, "GardenBed01WideHigh", true)]
        [InlineData(AssetCategories.Lamp, "DrivewayLight01", false)]
        [InlineData(AssetCategories.Kiosk, "PierKiosk01", false)]
        [InlineData(AssetCategories.Tree, "CoconutPalmTree01", false)]
        public void AutomaticEligibilityByName(string category, string name, bool eligible)
        {
            Assert.Equal(eligible, AssetHeuristics.AutoEligible(category, name, Prop));
        }

        [Fact]
        public void AutomaticEligibilityByComponents()
        {
            Assert.False(AssetHeuristics.AutoEligible(AssetCategories.Bench, "GardenBench01", Prop | AssetTraits.BrandObject));
            Assert.False(AssetHeuristics.AutoEligible(AssetCategories.Bench, "GardenBench01", Prop | AssetTraits.WallOrHanging));
            Assert.False(AssetHeuristics.AutoEligible(AssetCategories.Bin, "TrashBin01", Prop | AssetTraits.NetAttached));
            Assert.True(AssetHeuristics.AutoEligible(AssetCategories.Lamp, "LightpolePark01", Prop | AssetTraits.NetAttached));
            Assert.True(AssetHeuristics.AutoEligible(AssetCategories.Bench, "Parkbank Holz", (Prop & ~AssetTraits.Vanilla) | AssetTraits.BenchActivity));
            Assert.True(AssetHeuristics.AutoEligible(AssetCategories.Fence, "Wooden Fence", AssetTraits.Net | AssetTraits.NetFence));
            Assert.True(AssetHeuristics.AutoEligible(AssetCategories.SurfaceGrass, "Grass 01", AssetTraits.Surface));
            // Exercise equipment is sports, not a bench or decoration.
            Assert.Contains(AssetCategories.Sports, AssetHeuristics.Classify("GymParkChinUpBar01", Prop | AssetTraits.ExerciseActivity));
        }

        [Fact]
        public void BenchDefaultPrefersGardenBenchOverSportsSeats()
        {
            Assert.True(AssetHeuristics.DefaultScore(AssetCategories.Bench, "GardenBench01") > AssetHeuristics.DefaultScore(AssetCategories.Bench, "SportParkBench01"));
        }

        [Fact]
        public void ThemedParkPalettesAreNotUsedForGeneralParks()
        {
            Assert.True(AssetHeuristics.IsGeneralParkPalette("CityPark03"));
            Assert.True(AssetHeuristics.IsGeneralParkPalette("PocketPark07"));
            Assert.False(AssetHeuristics.IsGeneralParkPalette("SportPark01"));
            Assert.False(AssetHeuristics.IsGeneralParkPalette("SkatePark01"));
            Assert.False(AssetHeuristics.IsGeneralParkPalette("DogPark02"));
        }

        [Theory]
        [InlineData("PortableToilet01", true)]
        [InlineData("Outhouse01", true)]
        [InlineData("PublicWC01", true)]
        [InlineData("WindowCleaningPlatform01", false)]          // "windowCleaning" contains "wC" (playtest 2)
        [InlineData("WindowCleaningPlatformHoists01", false)]
        [InlineData("ToiletPaperSign01", false)]
        public void RestroomsAreRealRestroomsOnly(string name, bool restroom)
        {
            Assert.Equal(restroom, AssetHeuristics.Classify(name, Prop).Contains(AssetCategories.Restroom));
        }

        [Fact]
        public void PlanterBoxesAreNotBushes()
        {
            var plant = Prop | AssetTraits.Plant;
            Assert.DoesNotContain(AssetCategories.Bush, AssetHeuristics.Classify("GreenBushCity01", plant));
            Assert.Contains(AssetCategories.Planter, AssetHeuristics.Classify("FlowerBushCity02", plant));
            Assert.Contains(AssetCategories.Bush, AssetHeuristics.Classify("GreenBushWild01", plant));
            Assert.False(AssetHeuristics.AutoEligible(AssetCategories.Bush, "FlowerBushCity01", plant));
            Assert.False(AssetHeuristics.AutoEligible(AssetCategories.Bush, "FlowerBushWildDead02", plant));
            Assert.True(AssetHeuristics.AutoEligible(AssetCategories.Bush, "GreenBushWildAdult01", plant));
        }

        [Fact]
        public void RocksAreTheirOwnCategory()
        {
            Assert.Contains(AssetCategories.Rock, AssetHeuristics.Classify("Rock12", Prop));
            Assert.Contains(AssetCategories.Rock, AssetHeuristics.Classify("Boulder03", Prop));
            Assert.DoesNotContain(AssetCategories.Rock, AssetHeuristics.Classify("SpaceRocket01", Prop));
            Assert.DoesNotContain(AssetCategories.Rock, AssetHeuristics.Classify("BillboardHuge01 - GrindARock", Prop));
            Assert.True(AssetHeuristics.AutoEligible(AssetCategories.Rock, "Boulder03", Prop));
        }

        [Fact]
        public void DecalsAreClassifiedByMeshAndThemedByName()
        {
            var decal = Prop | AssetTraits.DecalMesh;
            var leaves = AssetHeuristics.Classify("LeavesDecal01", decal);
            Assert.Contains(AssetCategories.Decal, leaves);
            Assert.Contains(AssetCategories.DecalLeaves, leaves);
            Assert.Contains(AssetCategories.DecalMarkings, AssetHeuristics.Classify("AlphabetDecalA", decal));
            Assert.Contains(AssetCategories.DecalGraffiti, AssetHeuristics.Classify("Graffiti03", decal));
            Assert.Empty(AssetHeuristics.Classify("Graffiti03", Prop));           // not a decal mesh: still excluded
            Assert.True(AssetHeuristics.AutoEligible(AssetCategories.Decal, "MudDecal01", decal));
            Assert.False(AssetHeuristics.AutoEligible(AssetCategories.Decal, "AlphabetDecalA", decal));
            Assert.False(AssetHeuristics.AutoEligible(AssetCategories.DecalMarkings, "AlphabetDecalA", decal));   // letters never automatic (4b)
        }

        [Fact]
        public void CampingSplitsTentsAndFires()
        {
            var tent = AssetHeuristics.Classify("CampingTent01", Prop);
            var fire = AssetHeuristics.Classify("Campfire01", Prop);
            Assert.Contains(AssetCategories.CampingPitch, tent);
            Assert.DoesNotContain(AssetCategories.CampingFire, tent);
            Assert.Contains(AssetCategories.CampingFire, fire);
            Assert.DoesNotContain(AssetCategories.CampingPitch, fire);
        }

        // ───────────────────────── fences ─────────────────────────

        [Fact]
        public void FenceRingCornersAreBitIdentical()
        {
            // Odd angles so a + t·len would not round-trip to the next corner exactly.
            var ring = new List<float2> { new float2(0.3f, 0.1f), new float2(97.13f, 3.7f), new float2(101.9f, 77.77f), new float2(-3.31f, 81.05f) };
            var runs = Fences.Runs(LayerRegion.From(ring), null, 3.75f);
            Assert.True(runs.Count >= 4);
            for (int i = 0; i < runs.Count; i++)
            {
                var next = runs[(i + 1) % runs.Count];
                Assert.True(runs[i].c1.Equals(next.c0), $"run {i} end {runs[i].c1} != run {i + 1} start {next.c0}");
            }
        }

        [Fact]
        public void PlaygroundFenceRingIsClosedExceptTheGate()
        {
            var runs = Fences.Ring(new float2(10, 10), 8f, 10, new float2(0, 1), 3f);
            for (int i = 0; i + 1 < runs.Count; i++) Assert.True(runs[i].c1.Equals(runs[i + 1].c0));
        }

        [Theory]
        [InlineData(10f, 4f, 3)]
        [InlineData(8f, 4f, 2)]
        [InlineData(8.1f, 4f, 2)]
        [InlineData(1f, 4f, 1)]
        public void PropFencePiecesLeaveNoGaps(float len, float piece, int expected)
        {
            int n = Fences.PieceCount(len, piece);
            Assert.Equal(expected, n);
            Assert.True(n * piece >= len - 0.05f * piece - 1e-4f);
        }

        [Fact]
        public void UniformSeedIsStablePerCategory()
        {
            Assert.Equal(AutoAssets.UniformSeed(AssetCategories.Fence, 4), AutoAssets.UniformSeed(AssetCategories.Fence, 4));
            Assert.True(AutoAssets.IsUniform(AssetCategories.Bench));
            Assert.True(AutoAssets.IsUniform(AssetCategories.Fence));
            Assert.False(AutoAssets.IsUniform(AssetCategories.Tree));
        }
    }
}
