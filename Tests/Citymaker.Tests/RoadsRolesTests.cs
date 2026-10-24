// s16: road roles — what Automatic places per role (fixture catalog), the design JSON saying so
// ("Automatic · Two-Lane Road"), the road legend by role, and road mixes (linked controls).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Layers.Roads;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using JsonValue = Citymaker.Core.Tool.JsonValue;

namespace Citymaker.Tests
{
    public class RoadsRolesTests
    {
        private static readonly float3[] Rect = { new float3(0, 0, 0), new float3(600, 0, 0), new float3(600, 0, 400), new float3(0, 0, 400) };

        // ── fixture catalog (the gallery's: automatic pools + net widths of the vanilla roads) ──

        private sealed class Fixture
        {
            public Dictionary<string, List<string>> Pools = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            public Dictionary<string, AssetInfo> Items = new Dictionary<string, AssetInfo>(StringComparer.Ordinal);

            public AutoAssetSource Source(Func<string, bool> also = null) => new AutoAssetSource
            {
                Pool = c => Pools.TryGetValue(c ?? "", out var l) ? l : (IReadOnlyList<string>)Array.Empty<string>(),
                Exists = id => Items.ContainsKey(id) && (also == null || also(id)),
            };
        }

        private static Fixture _fx;

        private static Fixture Load()
        {
            if (_fx != null) return _fx;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "citymaker.csproj"))) dir = dir.Parent;
            var root = JsonNode.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "Tests", "Citymaker.Tests", "Fixtures", "catalog-bounds.json")))!;
            var f = new Fixture();
            foreach (var kv in root["pools"]!.AsObject()) f.Pools[kv.Key] = kv.Value!.AsArray().Select(x => (string)x!).ToList();
            foreach (var kv in root["items"]!.AsObject())
            {
                var info = new AssetInfo { Id = kv.Key };
                if (kv.Value?["w"] != null) info.NetWidth = (float)(double)kv.Value["w"]!;
                f.Items[kv.Key] = info;
            }
            return _fx = f;
        }

        private static ComposedPlan Compose(Action<SettingsBag> edit, Fixture fx, Func<string, bool> also = null)
        {
            var gen = GeneratorRegistry.Get(RoadsLayer.LayerId);
            var recipe = Recipe.Single(gen, 7);
            edit?.Invoke(recipe.Root.Settings);
            var plan = Composer.Compose(recipe, new AreaShape { Ring = Rect, Source = AreaSource.Drawn }, new SiteContext { Assets = fx.Items }, CancellationToken.None);
            AutoAssets.Resolve(plan, fx.Source(also));
            return plan;
        }

        private static IEnumerable<PlannedNet> Nets(ComposedPlan p, NetRole role) => p.Layers.SelectMany(l => l.Nets).Where(n => n.Role == role);

        private static void GridWithAvenues(SettingsBag s)
        {
            s.Values[RoadsIds.Pattern] = "grid";
            s.Values[RoadsIds.AvenueColumn] = true;
            s.Values[RoadsIds.AvenueRow] = true;
        }

        // ── 1. Automatic resolution ──

        [Fact]
        public void Automatic_StreetIsTwoLane_AvenueIsFourLane_FromFixtureCatalog()
        {
            var fx = Load();
            // The fixture's automatic road pool leads with a pedestrian street: the role picks must win over it.
            Assert.NotEqual(RoadRoles.TwoLane, fx.Pools[AssetCategories.Road][0]);
            var plan = Compose(GridWithAvenues, fx);
            Assert.NotEmpty(Nets(plan, NetRole.Street));
            Assert.NotEmpty(Nets(plan, NetRole.Avenue));
            Assert.All(Nets(plan, NetRole.Street), n => Assert.Equal("Small Road", n.Asset.Id));
            Assert.All(Nets(plan, NetRole.Avenue), n => Assert.Equal("Medium Road", n.Asset.Id));
            Assert.All(Nets(plan, NetRole.PerimeterRoad), n => Assert.Equal("Small Road", n.Asset.Id));
            Assert.Equal("Two-Lane Road", RoadRoles.NameOf("Small Road"));
            Assert.Equal("Four-Lane Road", RoadRoles.NameOf("Medium Road"));
        }

        [Fact]
        public void Automatic_RoundaboutRingIsTwoLaneOneWay_LocalsTwoLane()
        {
            var fx = Load();
            var radial = Compose(s => s.Values[RoadsIds.Pattern] = "radial", fx);
            Assert.NotEmpty(Nets(radial, NetRole.Roundabout));
            Assert.All(Nets(radial, NetRole.Roundabout), n => Assert.Equal("Small Road Oneway", n.Asset.Id));
            var tree = Compose(s => s.Values[RoadsIds.Pattern] = "organic", fx);
            Assert.NotEmpty(Nets(tree, NetRole.CulDeSac));
            Assert.All(Nets(tree, NetRole.CulDeSac), n => Assert.Equal("Small Road", n.Asset.Id));
        }

        [Fact]
        public void Automatic_FallsBackDownTheRoleList_ThenThePool()
        {
            var fx = Load();
            // A playset without the Four-Lane Road: the divided one; without both: the Two-Lane Road.
            var noMedium = Compose(GridWithAvenues, fx, id => id != "Medium Road");
            Assert.All(Nets(noMedium, NetRole.Avenue), n => Assert.Equal("Medium Road Divided", n.Asset.Id));
            var noFour = Compose(GridWithAvenues, fx, id => !id.StartsWith("Medium Road", StringComparison.Ordinal));
            Assert.All(Nets(noFour, NetRole.Avenue), n => Assert.Equal("Small Road", n.Asset.Id));
            // Nothing preferred loaded at all: the category pool still answers.
            var bare = Compose(GridWithAvenues, fx, id => !RoadRoles.Known.Any(k => k.Id == id));
            Assert.All(Nets(bare, NetRole.Street), n => Assert.False(string.IsNullOrEmpty(n.Asset.Id)));
        }

        [Fact]
        public void ExplicitPick_Wins_AndAutomaticLocalsFollowTheStreetPick()
        {
            var fx = Load();
            var plan = Compose(s =>
            {
                s.Values[RoadsIds.Pattern] = "organic";
                s.Values[RoadsIds.StreetAsset] = "Gravel Road";
            }, fx);
            Assert.All(Nets(plan, NetRole.CulDeSac), n => Assert.Equal("Gravel Road", n.Asset.Id));
            Assert.All(Nets(plan, NetRole.Avenue), n => Assert.Equal("Medium Road", n.Asset.Id));
        }

        [Fact]
        public void AvenueWidth_FollowsTheAutomaticPrefab()
        {
            var fx = Load();
            var plan = Compose(GridWithAvenues, fx);
            var roads = plan.Layers.Single(l => l.GeneratorId == RoadsLayer.LayerId);
            // Blocks are cut at the real road widths: with 24 m avenues, nothing of a block is closer than 12 m to an avenue centre line.
            var avenue = roads.Nets.First(n => n.Role == NetRole.Avenue);
            foreach (var r in roads.Regions.Where(r => r.Tags.Contains(RegionTags.Block)))
                foreach (var p in r.Ring)
                    Assert.True(new RoadCurve(avenue.A, avenue.B, avenue.C, avenue.D).DistanceXZ(p.xz, out _) > 11.5f);
        }

        // ── 2. Design JSON carries the resolved names ──

        [Fact]
        public void DesignJson_ResolvedAssets_AndNetLegend()
        {
            var fx = Load();
            var gen = GeneratorRegistry.Get(RoadsLayer.LayerId);
            var recipe = Recipe.Single(gen, 7);
            GridWithAvenues(recipe.Root.Settings);
            var plan = Composer.Compose(recipe, new AreaShape { Ring = Rect, Source = AreaSource.Drawn }, new SiteContext { Assets = fx.Items }, CancellationToken.None);
            AutoAssets.Resolve(plan, fx.Source());
            Func<string, string, AssetLabel> label = (cat, id) => new AssetLabel { Name = RoadRoles.NameOf(id), Icon = "thumb://" + id };
            var j = JsonValue.Parse(UiModels.Design(recipe, "roads", false, "0", null, plan, null, null, null, false, assetLabel: label).ToString())["root"];
            var res = j["resolvedAssets"];
            Assert.Equal("Small Road", res["streetAsset"]["id"].AsString());
            Assert.Equal("Two-Lane Road", res["streetAsset"]["name"].AsString());
            Assert.Equal("Medium Road", res["avenueAsset"]["id"].AsString());
            Assert.Equal("Four-Lane Road", res["avenueAsset"]["name"].AsString());
            Assert.Equal("thumb://Medium Road", res["avenueAsset"]["icon"].AsString());
            Assert.Equal("Small Road", res["perimeterAsset"]["id"].AsString());
            // No roundabout ring in this plan: what Automatic WOULD place, marked unused.
            Assert.Equal("Small Road Oneway", res["roundaboutAsset"]["id"].AsString());
            Assert.True(res["roundaboutAsset"]["unused"].AsBool());
            Assert.True(res["streetAsset"]["unused"].IsNull);
            // ... and greyed out with s15's disabled-with-reason.
            Assert.Equal(UiModels.NotInLayoutKey, j["disabled"]["roundaboutAsset"]["key"].AsString());
            Assert.Equal(UiModels.NotInLayoutKey, j["disabled"]["localAsset"]["key"].AsString());
            Assert.True(j["disabled"]["streetAsset"].IsNull);
            var legend = j["netLegend"];
            var roles = Enumerable.Range(0, legend.Count).Select(i => legend.Items[i]["role"].AsString()).ToList();
            Assert.Equal(new[] { "street", "avenue", "perimeter", "carFree" }, roles);   // s34: walkways (car-free) by default
            Assert.Equal("Four-Lane Road", legend.Items[1]["assetName"].AsString());
            Assert.Equal(RoadRoles.Color("avenue"), legend.Items[1]["color"].AsString());
            Assert.Equal("Citymaker.Roads.Role.avenue", legend.Items[1]["label"].AsString());
            Assert.True(legend.Items[0]["lengthM"].AsNumber() > 100);
            // The road mix shows what the picks are.
            Assert.Equal("auto", j["values"][RoadsIds.RoadMix].AsString());
        }

        [Fact]
        public void ResolvedAssets_UntaggedLayers_UseTheControlCategory()
        {
            var plan = new ComposedPlan();
            var l = new AreaPlan { GeneratorId = "fake", StepPath = "0@" };
            l.Objects.Add(new PlannedObject { Role = ObjectRole.Bench, Asset = new AssetRef("GardenBench02") });
            plan.Layers.Add(l);
            var gen = new FakeGen(new ControlSchema { Id = "benchAsset", Kind = ControlKind.Asset, AssetCategory = AssetCategories.Bench });
            var o = JsonValue.Parse(UiModels.ResolvedAssets(plan, "0", gen).ToString());
            Assert.Equal("GardenBench02", o["benchAsset"]["id"].AsString());
        }

        // ── 3. Legend from a plan ──

        [Fact]
        public void Legend_RolesInOrder_WithLengthsCountsAndMainPrefab()
        {
            PlannedNet N(NetRole role, float x0, float x1, string asset) => new PlannedNet
            {
                Role = role, A = new float3(x0, 0, 0), B = new float3(math.lerp(x0, x1, 1f / 3f), 0, 0), C = new float3(math.lerp(x0, x1, 2f / 3f), 0, 0),
                D = new float3(x1, 0, 0), Asset = new AssetRef(asset),
            };
            var nets = new List<PlannedNet>
            {
                N(NetRole.Avenue, 0, 50, "Medium Road"),
                N(NetRole.Street, 0, 100, "Small Road"),
                N(NetRole.Street, 0, 30, "Alley"),
                N(NetRole.Street, 0, 20, "Small Road"),
                N(NetRole.Pathway, 0, 40, "Pavement Path"),
                N(NetRole.Fence, 0, 99, "Fence"),
                N(NetRole.DriveAisle, 0, 99, "Invisible Road Path - 1xTwoway"),
            };
            var legend = RoadRoles.Legend(nets);
            Assert.Equal(new[] { "street", "avenue", "carFree" }, legend.Select(e => e.Role).ToArray());
            Assert.Equal(150, legend[0].LengthM, 0);
            Assert.Equal(3, legend[0].Count);
            Assert.Equal("Small Road", legend[0].AssetId);      // longest-used prefab of the role
            Assert.Equal(RoadsIds.StreetAsset, legend[0].Control);
            Assert.Equal(50, legend[1].LengthM, 0);
            Assert.Equal(40, legend[2].LengthM, 0);
            // A park's footpaths alone are no road plan.
            Assert.Empty(RoadRoles.Legend(new[] { N(NetRole.Pathway, 0, 40, "Pavement Path") }));
        }

        // ── 4. Road mixes ──

        [Fact]
        public void Mixes_UseOnlyVanillaRoadsOfTheFixtureCatalog()
        {
            var fx = Load();
            Assert.InRange(RoadMixes.All.Length, 6, 9);
            foreach (var m in RoadMixes.All)
                foreach (var id in RoadMixes.RoleIds)
                {
                    string v = RoadMixes.RoleValue(m, id);
                    if (v.Length == 0) continue;
                    Assert.True(fx.Items.ContainsKey(v), m.Id + " uses " + v);
                    Assert.NotNull(RoadRoles.Find(v));
                }
            Assert.Equal(RoadMixes.Values().Distinct().Count(), RoadMixes.Values().Length);
            Assert.Equal(RoadMixes.Custom, RoadMixes.Values().Last());
        }

        [Fact]
        public void Mixes_ApplyThenMatch_RoundTrips()
        {
            foreach (var m in RoadMixes.All)
            {
                var s = new RoadsLayer().Defaults();
                RoadMixes.Apply(s, m.Id);
                Assert.Equal(m.Id, RoadMixes.Match(s));
            }
            var d = new RoadsLayer().Defaults();
            Assert.Equal(RoadMixes.Automatic, RoadMixes.Match(d));
            d.Values[RoadsIds.AvenueAsset] = "Large Road";
            Assert.Equal(RoadMixes.Custom, RoadMixes.Match(d));
            // An old save without the new keys reads as Automatic.
            Assert.Equal(RoadMixes.Automatic, RoadMixes.Match(new SettingsBag()));
        }

        [Fact]
        public void Editor_PickingAMix_WritesThePicks_AndChangingAPick_MakesItCustom()
        {
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var recipe = Recipe.Single(GeneratorRegistry.Get(RoadsLayer.LayerId), 3);
            editor.ApplyDefaults(recipe);
            Assert.True(editor.SetValue(recipe, "0", RoadsIds.RoadMix, "downtown", true));
            var s = recipe.Root.Settings;
            Assert.Equal("Medium Road", s.S(RoadsIds.StreetAsset, ""));
            Assert.Equal("Large Road", s.S(RoadsIds.AvenueAsset, ""));
            Assert.Equal("Small Road Oneway - 3 lanes", s.S(RoadsIds.RoundaboutAsset, ""));
            Assert.Contains(RoadsIds.AvenueAsset, recipe.Root.Locked);
            Assert.Equal("downtown", s.S(RoadsIds.RoadMix, ""));

            Assert.True(editor.SetValue(recipe, "0", RoadsIds.RoadMix, "boulevards", true));
            Assert.True(s.B(RoadsIds.AvenueMedianTrees, false));
            Assert.Equal("Medium Road Divided", s.S(RoadsIds.AvenueAsset, ""));

            Assert.True(editor.SetValue(recipe, "0", RoadsIds.StreetAsset, "Alley", true));
            Assert.Equal(RoadMixes.Custom, s.S(RoadsIds.RoadMix, ""));
            // "Custom" itself changes nothing; back to Automatic clears every pick and upgrade.
            editor.SetValue(recipe, "0", RoadsIds.RoadMix, RoadMixes.Custom, true);
            Assert.Equal("Alley", s.S(RoadsIds.StreetAsset, ""));
            editor.SetValue(recipe, "0", RoadsIds.RoadMix, RoadMixes.Automatic, true);
            Assert.Equal("", s.S(RoadsIds.StreetAsset, "x"));
            Assert.False(s.B(RoadsIds.AvenueMedianTrees, true));
        }

        [Fact]
        public void Presets_AreNotModifiedByTheDerivedMix()
        {
            var gen = new RoadsLayer();
            foreach (var p in gen.Presets)
                Assert.Equal(RoadMixes.Match(p.Values), p.Values.S(RoadsIds.RoadMix, ""));
        }

        [Fact]
        public void MixPlans_PlaceTheMixRoads()
        {
            var fx = Load();
            var plan = Compose(s => { GridWithAvenues(s); RoadMixes.Apply(s, "pedestrian"); }, fx);
            Assert.All(Nets(plan, NetRole.Street), n => Assert.Equal("Pedestrian Street", n.Asset.Id));
            Assert.All(Nets(plan, NetRole.Avenue), n => Assert.Equal("Small Road", n.Asset.Id));
            Assert.NotEmpty(Nets(plan, NetRole.PerimeterRoad));
            Assert.All(Nets(plan, NetRole.PerimeterRoad), n => Assert.Equal("Small Road", n.Asset.Id));
            var bike = Compose(s => { GridWithAvenues(s); RoadMixes.Apply(s, "bike"); }, fx);
            Assert.All(Nets(bike, NetRole.Avenue), n => Assert.Contains("Left.SecondaryLane", n.Upgrades));
        }

        // ── 5. Schema JSON ──

        [Fact]
        public void ControlJson_HasDescriptions_OptionDetails_AndSwatches()
        {
            var gen = new RoadsLayer();
            var mix = JsonValue.Parse(UiModels.Control(gen.Controls.First(c => c.Id == RoadsIds.RoadMix)).ToString());
            Assert.Equal("Citymaker.Roads.Control.roadMix.Description", mix["description"].AsString());
            var opts = mix["options"];
            Assert.Equal(RoadMixes.Values().Length, opts.Count);
            Assert.Equal("Citymaker.Roads.Option.roadMix.auto.Detail", opts.Items[0]["detail"].AsString());
            var sw = opts.Items[0]["swatch"];
            Assert.Equal(3, sw.Count);
            Assert.Equal("street", sw.Items[0]["role"].AsString());
            Assert.Equal(16, sw.Items[0]["width"].AsNumber(), 1);
            Assert.Equal(24, sw.Items[1]["width"].AsNumber(), 1);   // the Four-Lane avenue
            Assert.True(opts.Items[opts.Count - 1]["swatch"].IsNull); // custom: no picture
            foreach (var id in new[] { RoadsIds.StreetAsset, RoadsIds.AvenueAsset, RoadsIds.LocalAsset, RoadsIds.RoundaboutAsset, RoadsIds.PathAsset, RoadsIds.PerimeterAsset })
                Assert.Equal("Citymaker.Roads.Control." + id + ".Description",
                    JsonValue.Parse(UiModels.Control(gen.Controls.First(c => c.Id == id)).ToString())["description"].AsString());
        }

        private sealed class FakeGen : IAreaGenerator
        {
            private readonly List<ControlSchema> _c;
            public FakeGen(params ControlSchema[] c) { _c = c.ToList(); }
            public string Id => "fake";
            public string NameKey => "";
            public string Icon => "";
            public LayerKind Kind => LayerKind.Fill;
            public string[] Accepts => Array.Empty<string>();
            public string[] Emits => Array.Empty<string>();
            public int Version => 1;
            public IReadOnlyList<ControlSchema> Controls => _c;
            public IReadOnlyList<PresetInfo> Presets => Array.Empty<PresetInfo>();
            public SettingsBag Defaults() => new SettingsBag();
            public float MinArea => 0;
            public float MaxArea => 1e9f;
            public Readiness CheckReady(AreaInput input) => new Readiness();
            public List<EdgeMarker> SuggestMarkers(AreaInput input) => new List<EdgeMarker>();
            public SettingsBag Randomize(SettingsBag current, uint seed, ICollection<string> locked) => current;
            public AreaPlan Plan(AreaInput input, CancellationToken cancel) => new AreaPlan();
        }
    }
}
