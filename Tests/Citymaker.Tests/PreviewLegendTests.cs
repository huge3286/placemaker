// s22: the preview legend and the terraform heat map (Core/Build/PreviewLegend.cs). Every element the overlay draws
// belongs to exactly one legend layer; the legend lists the layers of a plan with counts; hover isolates, click
// toggles; the terraform is a heat map of the height change (cut blue, fill orange/red, nothing for ~0), clipped
// to the area.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Roads;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class PreviewLegendTests
    {
        private static float3[] Ring3(float2[] r) => r.Select(p => new float3(p.x, 0, p.y)).ToArray();
        private static float2[] Rect(float x0, float z0, float x1, float z1) =>
            new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) };

        // ───────────── classification ─────────────

        [Fact]
        public void Every_role_maps_to_exactly_one_known_layer()
        {
            var known = new HashSet<string>(PreviewLegend.Order);
            foreach (ObjectRole r in Enum.GetValues(typeof(ObjectRole)))
                Assert.Contains(PreviewLegend.Of(new PlannedObject { Role = r }), known);
            foreach (NetRole r in Enum.GetValues(typeof(NetRole)))
            {
                Assert.Contains(PreviewLegend.Of(new PlannedNet { Role = r }, "park"), known);
                Assert.Contains(PreviewLegend.Of(new PlannedNet { Role = r }, RoadsLayer.LayerId), known);
            }
            foreach (SurfaceRole r in Enum.GetValues(typeof(SurfaceRole)))
                Assert.Contains(PreviewLegend.Of(new PlannedSurface { Role = r }), known);
            Assert.Contains(PreviewLegend.Of(new PlannedArea()), known);
            Assert.Contains(PreviewLegend.Of(new PlannedPaint()), known);
            Assert.Contains(PreviewLegend.Of(new PlannedTerraform()), known);
            Assert.Contains(PreviewLegend.Of(new PlannedZoning()), known);
            Assert.Contains(PreviewLegend.Of(new ZoneCellMark { State = ZoneCellState.Blocked }), known);
            Assert.Equal(PreviewLegend.Order.Length, known.Count);   // no duplicates in the order
        }

        [Theory]
        [InlineData(ObjectRole.Tree, null, PreviewLegend.Trees)]
        [InlineData(ObjectRole.Bush, null, PreviewLegend.Planting)]
        [InlineData(ObjectRole.Bush, AssetCategories.Hedge, PreviewLegend.Planting)]
        [InlineData(ObjectRole.Furniture, AssetCategories.Flowerbed, PreviewLegend.Planting)]
        [InlineData(ObjectRole.Decal, AssetCategories.DecalLeaves, PreviewLegend.Decals)]
        [InlineData(ObjectRole.Bench, null, PreviewLegend.Objects)]
        [InlineData(ObjectRole.Lamp, null, PreviewLegend.Objects)]
        [InlineData(ObjectRole.Bin, null, PreviewLegend.Objects)]
        [InlineData(ObjectRole.Furniture, AssetCategories.Decoration, PreviewLegend.Objects)]
        [InlineData(ObjectRole.Furniture, AssetCategories.Playground, PreviewLegend.Features)]
        [InlineData(ObjectRole.Furniture, AssetCategories.SportsGoal, PreviewLegend.Features)]
        [InlineData(ObjectRole.Furniture, AssetCategories.Restroom, PreviewLegend.Features)]
        [InlineData(ObjectRole.Centerpiece, AssetCategories.CenterpieceFountain, PreviewLegend.Features)]
        [InlineData(ObjectRole.ParkingBay, null, PreviewLegend.Parking)]
        [InlineData(ObjectRole.AccessPoint, null, PreviewLegend.Entrances)]
        public void Objects_by_role_and_category(ObjectRole role, string category, string layer) =>
            Assert.Equal(layer, PreviewLegend.Of(new PlannedObject { Role = role, Category = category }));

        [Fact]
        public void Nets_paths_roads_and_car_free_streets()
        {
            Assert.Equal(PreviewLegend.Paths, PreviewLegend.Of(new PlannedNet { Role = NetRole.Pathway }, "park"));
            Assert.Equal("road:carFree", PreviewLegend.Of(new PlannedNet { Role = NetRole.Pathway }, RoadsLayer.LayerId));
            Assert.Equal("road:street", PreviewLegend.Of(new PlannedNet { Role = NetRole.Street }, "park"));
            Assert.Equal("road:local", PreviewLegend.Of(new PlannedNet { Role = NetRole.CulDeSac }, RoadsLayer.LayerId));
            Assert.Equal(PreviewLegend.Parking, PreviewLegend.Of(new PlannedNet { Role = NetRole.DriveAisle }, "parking"));
            Assert.Equal(PreviewLegend.Water, PreviewLegend.Of(new PlannedSurface { Role = SurfaceRole.Water }));
            Assert.Equal(PreviewLegend.Surfaces, PreviewLegend.Of(new PlannedSurface { Role = SurfaceRole.Grass }));
            Assert.Equal(PreviewLegend.Cutouts, PreviewLegend.Of(new ZoneCellMark { State = ZoneCellState.Zoned }));
            Assert.Equal(PreviewLegend.Blocked, PreviewLegend.Of(new ZoneCellMark { State = ZoneCellState.Blocked }));
            // Overlay and legend share the s16 road colours.
            Assert.Equal(RoadRoles.Color(RoadRoles.Avenue), PreviewLegend.Color("road:avenue"));
        }

        // ───────────── the legend of a plan ─────────────

        private static ComposedPlan SmallPlan()
        {
            var plan = new ComposedPlan();
            var park = new AreaPlan { GeneratorId = "park" };
            park.Nets.Add(new PlannedNet { Role = NetRole.Pathway, A = new float3(0, 0, 0), B = new float3(10, 0, 0), C = new float3(20, 0, 0), D = new float3(30, 0, 0) });
            park.Objects.Add(new PlannedObject { Role = ObjectRole.Tree });
            park.Objects.Add(new PlannedObject { Role = ObjectRole.Tree });
            park.Objects.Add(new PlannedObject { Role = ObjectRole.Bench });
            park.Objects.Add(new PlannedObject { Role = ObjectRole.Decal });
            park.Surfaces.Add(new PlannedSurface { Role = SurfaceRole.Water, Ring = Ring3(Rect(0, 0, 10, 10)) });
            park.Surfaces.Add(new PlannedSurface { Role = SurfaceRole.Grass, Ring = Ring3(Rect(0, 0, 20, 10)) });
            plan.Layers.Add(park);
            var roads = new AreaPlan { GeneratorId = RoadsLayer.LayerId };
            roads.Nets.Add(new PlannedNet { Role = NetRole.Street, A = new float3(0, 0, 50), B = new float3(20, 0, 50), C = new float3(40, 0, 50), D = new float3(60, 0, 50), Asset = new AssetRef("Small Road") });
            plan.Layers.Add(roads);
            plan.ZoneCells.Add(new ZoneCellMark { A = new float2(0, 0), B = new float2(8, 0), C = new float2(8, 8), D = new float2(0, 8), State = ZoneCellState.Zoned });
            plan.ZoneCells.Add(new ZoneCellMark { A = new float2(0, 8), B = new float2(8, 8), C = new float2(8, 16), D = new float2(0, 16), State = ZoneCellState.Blocked, Clipped = Rect(0, 8, 8, 12) });
            return plan;
        }

        [Fact]
        public void Legend_lists_the_layers_of_the_plan_in_order_with_counts()
        {
            var legend = PreviewLegend.Compute(SmallPlan(), entrances: 2);
            var ids = legend.Select(e => e.Id).ToList();
            Assert.Equal(new[] { "paths", "road:street", "trees", "water", "surfaces", "objects", "entrances", "cutouts", "blocked", "decals" }, ids);
            var by = legend.ToDictionary(e => e.Id);
            Assert.Equal(2, by["trees"].Count);
            Assert.Equal(30, by["paths"].LengthM, 0);
            Assert.Equal(60, by["road:street"].LengthM, 0);
            Assert.Equal("Small Road", by["road:street"].AssetId);
            Assert.Equal(RoadRoles.LabelKey("street"), by["road:street"].LabelKey);
            Assert.Equal(100, by["water"].AreaM2, 0);
            Assert.Equal(200, by["surfaces"].AreaM2, 0);
            Assert.Equal(32, by["blocked"].AreaM2, 0);   // the clipped part only
            Assert.Equal(2, by["entrances"].Count);
            Assert.False(by["blocked"].DefaultOn);
            Assert.False(by["decals"].DefaultOn);
            Assert.True(by["trees"].DefaultOn);
            Assert.Equal("Citymaker.Legend.trees", by["trees"].LabelKey);
        }

        [Fact]
        public void Composed_park_every_element_is_counted_once()
        {
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var ring = TestKit.Rect(140, 120);
            var r = editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 5);
            var site = new SiteContext();
            TestKit.Entered(r, ring, site);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            var legend = PreviewLegend.Compute(plan);
            int elements = plan.Layers.Sum(l => l.Nets.Count + l.Objects.Count + l.Surfaces.Count + l.Areas.Count + l.Paint.Count);
            int counted = legend.Where(e => e.Id != PreviewLegend.Terrain && e.Id != PreviewLegend.Cutouts && e.Id != PreviewLegend.Blocked && e.Id != PreviewLegend.Entrances).Sum(e => e.Count);
            Assert.Equal(elements, counted);
            Assert.Contains(legend, e => e.Id == PreviewLegend.Trees);
            Assert.Contains(legend, e => e.Id == PreviewLegend.Paths);
            // The JSON the UI gets (contract.ts LegendEntry).
            var json = UiModels.Legend(plan, r).ToString();
            Assert.Contains("\"id\":\"trees\"", json);
            Assert.Contains("\"color\":\"#", json);
        }

        [Fact]
        public void Visibility_hover_isolates_overrides_win_defaults_otherwise()
        {
            var ov = new Dictionary<string, bool> { ["trees"] = false, ["blocked"] = true };
            Assert.True(PreviewLegend.Visible("paths", "", ov));
            Assert.False(PreviewLegend.Visible("trees", "", ov));
            Assert.True(PreviewLegend.Visible("blocked", "", ov));
            Assert.False(PreviewLegend.Visible("decals", "", ov));
            Assert.False(PreviewLegend.Visible("decals", null, null));
            // Hover: only that layer, even a hidden or default-off one.
            Assert.True(PreviewLegend.Visible("trees", "trees", ov));
            Assert.False(PreviewLegend.Visible("paths", "trees", ov));
            Assert.True(PreviewLegend.Visible("decals", "decals", null));
        }

        [Fact]
        public void State_and_summary_carry_the_legend()
        {
            var state = UiModels.State(new ToolStateInput { LegendVisibility = new Dictionary<string, bool> { ["trees"] = false } }).ToString();
            Assert.Contains("\"legendVisibility\":{\"trees\":false}", state);
            Assert.DoesNotContain("legendVisibility", UiModels.State(new ToolStateInput()).ToString());
            var summary = UiModels.Summary(new SummaryInput { Plan = SmallPlan() }).ToString();
            Assert.Contains("\"legend\":[", summary);
            Assert.Contains("\"id\":\"road:street\"", summary);
            Assert.Contains("\"group\":\"roads\"", summary);
            Assert.Contains("\"defaultOff\":true", summary);
        }

        // ───────────── terraform heat map ─────────────

        private static ComposedPlan Terraform(float2[] ring, float target, TerraformKind kind = TerraformKind.Level)
        {
            var plan = new ComposedPlan();
            var l = new AreaPlan();
            l.Terraform.Add(new PlannedTerraform { Ring = Ring3(ring), TargetHeight = target, Kind = kind });
            plan.Layers.Add(l);
            return plan;
        }

        [Fact]
        public void Heat_map_no_change_draws_nothing()
        {
            var area = Rect(0, 0, 100, 100);
            Assert.Null(TerrainHeat.Build(Terraform(Rect(10, 10, 90, 90), 5f), area, _ => 5f));
            Assert.Null(TerrainHeat.Build(Terraform(Rect(10, 10, 90, 90), 5.1f), area, _ => 5f));   // below MinChange
            Assert.Null(TerrainHeat.Build(new ComposedPlan(), area, _ => 0f));
            Assert.Equal(0, TerrainHeat.BandOf(0.2f));
            Assert.Equal(0, TerrainHeat.BandOf(float.NaN));
        }

        [Fact]
        public void Heat_map_sign_gives_the_colour_and_magnitude_the_band()
        {
            var area = Rect(0, 0, 100, 100);
            var fill = TerrainHeat.Build(Terraform(Rect(10, 10, 90, 90), 7f), area, _ => 5f);
            Assert.NotNull(fill);
            Assert.All(fill.Rings, r => Assert.Equal(3, r.band));            // +2 m → band 3
            Assert.Equal(2f, fill.MaxDelta, 2);
            Assert.InRange(fill.ChangedM2, 80 * 80 * 0.9, 80 * 80 * 1.1);
            var cut = TerrainHeat.Build(Terraform(Rect(10, 10, 90, 90), 0f), area, _ => 5f);
            Assert.All(cut.Rings, r => Assert.Equal(-4, r.band));            // −5 m → deepest cut band
            Assert.Equal(-5f, cut.MinDelta, 2);
            // Fill is warm, cut is blue; bigger changes are more opaque.
            var (fillHex, fa) = TerrainHeat.Style(3);
            var (cutHex, ca) = TerrainHeat.Style(-4);
            var f = PreviewLegend.Hex(fillHex); var c = PreviewLegend.Hex(cutHex);
            Assert.True(f.x > f.z, "fill is orange/red");
            Assert.True(c.z > c.x, "cut is blue");
            Assert.True(TerrainHeat.Style(1).alpha < TerrainHeat.Style(4).alpha);
            Assert.True(TerrainHeat.Style(-1).alpha < TerrainHeat.Style(-4).alpha);
            // Lower only cuts, Raise only fills.
            Assert.Null(TerrainHeat.Build(Terraform(Rect(10, 10, 90, 90), 7f, TerraformKind.Lower), area, _ => 5f));
            Assert.Null(TerrainHeat.Build(Terraform(Rect(10, 10, 90, 90), 2f, TerraformKind.Raise), area, _ => 5f));
        }

        [Fact]
        public void Heat_map_follows_the_ground_and_later_items_win()
        {
            var area = Rect(0, 0, 100, 100);
            var plan = Terraform(Rect(0, 0, 100, 100), 5f);
            plan.Layers[0].Terraform.Add(new PlannedTerraform { Ring = Ring3(Rect(40, 40, 60, 60)), TargetHeight = 9f });
            // Ground slopes 0 → 10 m along x: the pad cuts on the high side, fills on the low side.
            var heat = TerrainHeat.Build(plan, area, p => p.x * 0.1f);
            Assert.Contains(heat.Rings, r => r.band > 0);
            Assert.Contains(heat.Rings, r => r.band < 0);
            float2 Centre(float2[] r) => Poly.Centroid(r);
            Assert.All(heat.Rings.Where(r => r.band < 0 && !(Centre(r.ring).x > 40 && Centre(r.ring).x < 60 && Centre(r.ring).y > 40 && Centre(r.ring).y < 60)), r => Assert.True(Centre(r.ring).x > 50));
            // The later, higher pad fills (+4 m or more) in the middle.
            Assert.Contains(heat.Rings, r => r.band >= 3 && Poly.Contains(Rect(40, 40, 60, 60), Centre(r.ring)));
        }

        [Fact]
        public void Heat_map_is_clipped_to_the_area()
        {
            // A concave L-shaped area; the terraform reaches far past it.
            var area = Poly.EnsureCcw(new[] { new float2(0, 0), new float2(100, 0), new float2(100, 40), new float2(40, 40), new float2(40, 100), new float2(0, 100) });
            var heat = TerrainHeat.Build(Terraform(Rect(-50, -50, 150, 150), 3f), area, _ => 0f);
            Assert.NotNull(heat);
            var grown = Poly.Offset(area, -0.05f)[0];
            foreach (var (_, ring) in heat.Rings)
                foreach (var p in ring) Assert.True(Poly.Contains(grown, p), $"heat map outside the area at {p}");
            double area2 = Poly.Area(area);
            Assert.InRange(heat.ChangedM2, area2 * 0.98, area2 * 1.02);
            // Cell budget holds for a big area.
            var big = Rect(0, 0, 1000, 1000);
            var bigHeat = TerrainHeat.Build(Terraform(big, 3f), big, _ => 0f);
            Assert.True(bigHeat.Cell >= TerrainHeat.MinCell);
            Assert.True(bigHeat.Rings.Count <= TerrainHeat.MaxCells);
        }

        [Fact]
        public void Composer_sets_the_heat_map()
        {
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var ring = TestKit.Rect(140, 120);
            var r = editor.SingleLayerRecipe(GeneratorRegistry.Get("park"), 5);
            // A sloped site: whatever the park levels shows up as change.
            var site = new SiteContext { HeightAt = p => p.x * 0.15f };
            TestKit.Entered(r, ring, site);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            bool terraforms = plan.Layers.Any(l => l.Terraform.Count > 0);
            if (terraforms && plan.TerrainHeat != null)
                Assert.All(plan.TerrainHeat.Rings, x => Assert.NotEqual(0, x.band));
            if (!terraforms) Assert.Null(plan.TerrainHeat);
        }
    }
}
