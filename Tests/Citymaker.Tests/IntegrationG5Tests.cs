using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    /// <summary>g5 platform integration: terrain field, surface categories, path joins, aliases, variety, timing.</summary>
    public class IntegrationG5Tests
    {
        // ───────────── TerrainField ─────────────

        [Fact]
        public void TerrainField_LaterOverrideWins_BaseOutside()
        {
            var f = new TerrainField(p => 1f);
            Assert.False(f.HasOverrides);
            f.OverridePolygon(new[] { new float2(0, 0), new float2(10, 0), new float2(10, 10), new float2(0, 10) }, 5f);
            f.OverridePolygon(new[] { new float2(4, 4), new float2(6, 4), new float2(6, 6), new float2(4, 6) }, 7f);
            Assert.Equal(1f, f.HeightAt(new float2(20, 20)));
            Assert.Equal(5f, f.HeightAt(new float2(2, 2)));
            Assert.Equal(7f, f.HeightAt(new float2(5, 5)));
        }

        [Fact]
        public void FromTerraform_LevelsPolygonsAndKeepsBase()
        {
            var tf = new List<PlannedTerraform>
            {
                new PlannedTerraform { Ring = Ring3(0, 0, 12, 12), TargetHeight = 3f },
                new PlannedTerraform { Ring = Ring3(12, 0, 24, 12), TargetHeight = 4f },
                new PlannedTerraform { Ring = Ring3(10, 2, 14, 6), TargetHeight = 9f },   // later wins
            };
            var h = TerrainField.FromTerraform(tf, p => -1f);
            Assert.Equal(3f, h(new float2(5, 5)));
            Assert.Equal(4f, h(new float2(20, 10)));
            Assert.Equal(9f, h(new float2(12, 4)));
            Assert.Equal(-1f, h(new float2(50, 50)));
            Assert.Null(TerrainField.FromTerraform(new List<PlannedTerraform>(), p => 0f));
        }

        [Fact]
        public void Composer_ChildSite_UsesHeightAfterOrTerraform()
        {
            var site = new SiteContext { HeightAt = p => 2f };
            var flat = new AreaPlan();
            Assert.Same(site, Composer.ChildSite(flat, site));
            var raised = new AreaPlan();
            raised.Terraform.Add(new PlannedTerraform { Ring = Ring3(0, 0, 10, 10), TargetHeight = 6f });
            var child = Composer.ChildSite(raised, site);
            Assert.Equal(6f, child.HeightAt(new float2(5, 5)));
            Assert.Equal(2f, child.HeightAt(new float2(50, 5)));
            Assert.Same(site.NearbyNets, child.NearbyNets);
            raised.HeightAfter = p => 11f;
            Assert.Equal(11f, Composer.ChildSite(raised, site).HeightAt(new float2(5, 5)));
        }

        [Fact]
        public void ShapingLayer_ObjectsAndChildren_StandOnNewGround()
        {
            // Raised terrain as the parent, a feature add-on as its child: the child's objects must sit on
            // the raised ground (the composer passes the parent's terraform as the child's HeightAt).
            var terrain = new RecipeStep { Layer = "terrain", Seed = 5 };
            terrain.Settings.Values["shape"] = "raised";
            terrain.Settings.Values["height"] = 4.0;
            terrain.Children.Add(new RecipeStep { Layer = "picnic", Seed = 3 });
            var site = new SiteContext { HeightAt = p => 10f };
            var plan = Composer.Compose(new Recipe { Root = terrain }, new AreaShape { Ring = TestKit.Rect(200, 200) }, site, CancellationToken.None);
            var parent = plan.Layers[0];
            Assert.NotEmpty(parent.Terraform);
            var ground = Composer.ChildSite(parent, site).HeightAt;
            var centre = new float2(100, 100);
            Assert.True(ground(centre) > 12f, "raised centre " + ground(centre));
            var child = plan.Layers.Skip(1).FirstOrDefault(l => l.GeneratorId == "picnic");
            Assert.NotNull(child);
            Assert.NotEmpty(child.Objects);
            Assert.Contains(child.Objects, o => ground(o.Position.xz) > 10.5f);
            foreach (var o in child.Objects)
                Assert.True(math.abs(o.Position.y - ground(o.Position.xz)) < 1e-3f, $"object at {o.Position} vs ground {ground(o.Position.xz)}");
        }

        [Fact]
        public void TerraformDabs_LaterLayerWinsWhereItLevels()
        {
            var parent = (Ring2(0, 0, 100, 100), 5f, 0);
            var pad = (Ring2(30, 30, 70, 70), 8f, 1);
            var dabs = TerraformDabs.Merge(new List<(float2[], float, int)> { parent, pad }, 12f);
            Assert.Contains(dabs, d => d.Target == 8f);
            Assert.Contains(dabs, d => d.Target == 5f);
            foreach (var d in dabs)
                if (d.Target == 5f)
                    Assert.False(d.P.x > 30 && d.P.x < 70 && d.P.y > 30 && d.P.y < 70, "parent dab inside the pad at " + d.P);
        }

        // ───────────── Surfaces ─────────────

        [Fact]
        public void SurfaceRules_AddCustomCategories()
        {
            var rubber = AssetHeuristics.Classify("Playground Rubber Surface", AssetTraits.Surface);
            Assert.Contains(SurfaceCategories.Rubber, rubber);
            Assert.Contains(AssetCategories.SurfaceAny, rubber);
            Assert.Contains(SurfaceCategories.Bark, AssetHeuristics.Classify("Bark Mulch", AssetTraits.Surface));
            Assert.Contains(SurfaceCategories.Clay, AssetHeuristics.Classify("Tennis Clay", AssetTraits.Surface));
            Assert.Contains(SurfaceCategories.Track, AssetHeuristics.Classify("Running Track Tartan", AssetTraits.Surface));
            Assert.DoesNotContain(SurfaceCategories.Rubber, AssetHeuristics.Classify("Grass 01", AssetTraits.Surface));
        }

        [Fact]
        public void AutoAssets_EmptyCustomCategory_FallsBackToParent()
        {
            var plan = new ComposedPlan();
            var l = new AreaPlan { StepPath = "0@root", Seed = 1 };
            l.Surfaces.Add(new PlannedSurface { Ring = Ring3(0, 0, 5, 5), Role = SurfaceRole.Paving, Category = SurfaceCategories.Track });
            l.Surfaces.Add(new PlannedSurface { Ring = Ring3(0, 0, 5, 5), Role = SurfaceRole.Ground, Category = SurfaceCategories.Bark });
            plan.Layers.Add(l);
            var pools = new Dictionary<string, IReadOnlyList<string>>
            {
                { AssetCategories.SurfacePaving, new[] { "Paving A" } },
                { AssetCategories.SurfaceNatural, new[] { "Gravel" } },
            };
            AutoAssets.Resolve(plan, c => pools.TryGetValue(c, out var p) ? p : null);
            Assert.Equal("Paving A", l.Surfaces[0].Asset.Id);   // track → rubber → paving
            Assert.Equal("Gravel", l.Surfaces[1].Asset.Id);     // bark → dirt → natural
        }

        [Fact]
        public void Nets_ReachTypeRules_AndBikeNetsStayOutOfFootpaths()
        {
            var t = AssetTraits.Net | AssetTraits.Pathway;
            var bike = AssetHeuristics.Classify("TwoWayBikePath", t);
            Assert.Contains(AssetCategories.Pathway, bike);
            Assert.Contains("net.bike", bike);
            Assert.False(AssetHeuristics.AutoEligible(AssetCategories.Pathway, "TwoWayBikePath", t));
            Assert.True(AssetHeuristics.AutoEligible(AssetCategories.Pathway, "Pedestrian Pathway", t));
            Assert.True(AssetHeuristics.DefaultScore(AssetCategories.Pathway, "PedestrianPathWithBikeLanes")
                < AssetHeuristics.DefaultScore(AssetCategories.Pathway, "Gravel Pathway"));
            Assert.DoesNotContain(AssetHeuristics.Classify("Basic Fence", AssetTraits.Net | AssetTraits.NetFence), c => !c.StartsWith("net.") && c != AssetCategories.Fence);
            Assert.Contains(SurfaceCategories.Gravel, AssetHeuristics.Classify("Gravel Path Surface", AssetTraits.Surface));
        }

        [Fact]
        public void PlannedNetCategory_ResolvesWithFallback()
        {
            var plan = new ComposedPlan();
            var l = new AreaPlan { StepPath = "0@root", Seed = 1 };
            var bike = Straight(new float3(0, 0, 0), new float3(20, 0, 0), NetRole.PathwayWide);
            bike.Category = "net.bike";
            var trail = Straight(new float3(0, 0, 10), new float3(20, 0, 10));
            trail.Category = "net.trail";
            l.Nets.Add(bike);
            l.Nets.Add(trail);
            plan.Layers.Add(l);
            var pools = new Dictionary<string, IReadOnlyList<string>>
            {
                { "net.bike", new[] { "TwoWayBikePath" } },
                { AssetCategories.Pathway, new[] { "Pedestrian Pathway" } },
            };
            AutoAssets.Resolve(plan, c => pools.TryGetValue(c, out var p) ? p : null);
            Assert.Equal("TwoWayBikePath", bike.Asset.Id);
            Assert.Equal("Pedestrian Pathway", trail.Asset.Id);   // empty net.trail → pathway
            Assert.Equal("net.bike", RoleCategories.ForNet(bike));
        }

        // ───────────── Path joins ─────────────

        [Fact]
        public void PathJoins_SplitsHostWhereSpurEnds()
        {
            var nets = new List<PlannedNet>
            {
                Straight(new float3(0, 0, 0), new float3(30, 0, 0)),
                Straight(new float3(12, 0, 0.3f), new float3(12, 0, 20)),
            };
            Assert.Equal(1, PathJoins.JoinAll(nets));
            Assert.Equal(3, nets.Count);
            var spur = nets[1];
            Assert.True(spur.A.Equals(nets[0].D), "spur start must be the new node");
            Assert.True(nets[2].A.Equals(nets[0].D));
            Assert.True(math.abs(nets[0].D.x - 12f) < 0.05f);
            Assert.Equal(30f, nets[2].D.x);
            // Idempotent: already joined.
            Assert.Equal(0, PathJoins.JoinAll(nets));
        }

        [Fact]
        public void PathJoins_SnapsToNearbyEndInsteadOfTinyPiece()
        {
            var nets = new List<PlannedNet>
            {
                Straight(new float3(0, 0, 0), new float3(30, 0, 0)),
                Straight(new float3(29.5f, 0, 0.4f), new float3(29.5f, 0, 20)),
            };
            PathJoins.JoinAll(nets);
            Assert.Equal(2, nets.Count);
            Assert.True(nets[1].A.Equals(nets[0].D));
        }

        [Fact]
        public void PathJoins_IgnoresRoadsAndFences()
        {
            var nets = new List<PlannedNet>
            {
                Straight(new float3(0, 0, 0), new float3(30, 0, 0), NetRole.Street),
                Straight(new float3(12, 0, 0.3f), new float3(12, 0, 20), NetRole.Fence),
            };
            Assert.Equal(0, PathJoins.JoinAll(nets));
            Assert.Equal(2, nets.Count);
        }

        // ───────────── Aliases ─────────────

        [Fact]
        public void RetiredIds_StayLoadable_AndNormaliseToCanonical()
        {
            foreach (var (old, canon) in new[] { ("honeyHives", "apiary"), ("farmStall", "farmStand") })
            {
                var g = GeneratorRegistry.Get(old);
                Assert.NotNull(g);
                Assert.True(GeneratorRegistry.IsHidden(g));
                Assert.Equal(canon, LayerAliases.Canonical(old));
                Assert.NotNull(GeneratorRegistry.Get(canon));
            }
            var step = new RecipeStep { Layer = "park" };
            var hives = new RecipeStep { Layer = "honeyHives" };
            hives.Settings.Values["count"] = 5.0;
            hives.Settings.Values["layout"] = "circle";
            hives.Locked.Add("count");
            var stall = new RecipeStep { Layer = "farmStall" };
            stall.Settings.Values["stalls"] = 2.0;
            stall.Settings.Values["tables"] = 0.0;
            step.Children.Add(hives);
            step.Children.Add(stall);
            Assert.Equal(2, LayerAliases.Normalize(step));
            Assert.Equal("apiary", hives.Layer);
            Assert.Equal(5.0, hives.Settings.Num("hives", 0));
            Assert.Equal("ring", hives.Settings.S("layout", ""));
            Assert.Contains("hives", hives.Locked);
            Assert.Equal("farmStand", stall.Layer);
            Assert.Equal(2.0, stall.Settings.Num("count", 0));
            Assert.False(stall.Settings.B("seating", true));
        }

        [Fact]
        public void RecipeJson_NormalisesRetiredIds()
        {
            var file = RecipeJson.Parse("{\"id\":\"x\",\"root\":{\"layer\":\"urbanFarm\",\"children\":[{\"layer\":\"honeyHives\",\"settings\":{\"count\":3}}]}}");
            var child = file.Recipe.Root.Children.Single();
            Assert.Equal("apiary", child.Layer);
            Assert.Equal(3.0, child.Settings.Num("hives", 0));
        }

        [Fact]
        public void AliasGenerator_PlansWithCanonicalLayer()
        {
            var g = GeneratorRegistry.Get("honeyHives");
            var s = new SettingsBag();
            s.Values["count"] = 4.0;
            var input = TestKit.Input(TestKit.Rect(120, 120), s);
            input.RegionTags = new[] { RegionTags.Green };
            var plan = g.Plan(input, CancellationToken.None);
            Assert.NotNull(plan);
            Assert.False(plan.HasErrors);
        }

        // ───────────── Variety ─────────────

        [Theory]
        [InlineData("industrial.scrap")]
        [InlineData("industrial.wreck")]
        [InlineData("ruins.junk")]
        [InlineData("construction.pile")]
        [InlineData("farm.crop")]
        [InlineData("garden.crop")]
        [InlineData("logistics.pallet")]
        [InlineData("rock")]
        [InlineData("flowerbed")]
        public void VarietyCategories_AreNotUniform(string category) => Assert.False(AutoAssets.IsUniform(category));

        [Fact]
        public void TimingBudget_NeverBelowDesignBudget() => Assert.True(TimingBudget.Ms(250) >= 250);

        // ───────────── helpers ─────────────

        private static float3[] Ring3(float x0, float z0, float x1, float z1) => new[]
        {
            new float3(x0, 0, z0), new float3(x1, 0, z0), new float3(x1, 0, z1), new float3(x0, 0, z1),
        };

        private static float2[] Ring2(float x0, float z0, float x1, float z1) => new[]
        {
            new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1),
        };

        private static PlannedNet Straight(float3 a, float3 b, NetRole role = NetRole.Pathway) => new PlannedNet
        {
            A = a, B = math.lerp(a, b, 1f / 3f), C = math.lerp(a, b, 2f / 3f), D = b, Role = role,
        };
    }
}
