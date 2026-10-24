// s15: builds never touch existing buildings (Core/Build/SiteSafety, Core/Geometry/ObstacleCut).

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class BuildSiteSafetyTests
    {
        private static float2[] Rect2(float x0, float z0, float x1, float z1) =>
            new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) };

        private static float3[] Rect3(float x0, float z0, float x1, float z1) => Poly.To3D(Rect2(x0, z0, x1, z1), _ => 0f);

        private static Obstacle Lot(float x0, float z0, float x1, float z1, ObstacleKind kind = ObstacleKind.Building) =>
            new Obstacle { A = new float2(x0, z0), B = new float2(x1, z0), C = new float2(x1, z1), D = new float2(x0, z1), Kind = kind };

        private static SiteContext SiteWith(params Obstacle[] obstacles)
        {
            var s = new SiteContext();
            s.Obstacles.AddRange(obstacles);
            return s;
        }

        private static PlannedNet Line(float2 a, float2 d, NetRole role = NetRole.Pathway) => new PlannedNet
        {
            A = new float3(a.x, 0, a.y), B = new float3(math.lerp(a.x, d.x, 1 / 3f), 0, math.lerp(a.y, d.y, 1 / 3f)),
            C = new float3(math.lerp(a.x, d.x, 2 / 3f), 0, math.lerp(a.y, d.y, 2 / 3f)), D = new float3(d.x, 0, d.y), Role = role,
        };

        private static ComposedPlan PlanOf(AreaPlan layer)
        {
            var p = new ComposedPlan();
            p.Layers.Add(layer);
            return p;
        }

        private static float Overlap(float2[] a, float2[] b) => Poly.Intersection(a, b).Sum(r => r.Area);

        // ───────────── ObstacleCut ─────────────

        [Fact]
        public void Cut_untouched_ring_is_returned_as_is()
        {
            var ring = Rect2(0, 0, 100, 100);
            var cut = ObstacleCut.Subtract(ring, new List<float2[]> { Rect2(200, 200, 220, 220) }, out int touched);
            Assert.Same(ring, cut);
            Assert.Equal(0, touched);
        }

        [Fact]
        public void Cut_lot_on_the_edge_notches_the_ring()
        {
            var ring = Rect2(0, 0, 100, 100);
            var lot = Rect2(40, -10, 60, 20);
            var cut = ObstacleCut.Subtract(ring, new List<float2[]> { lot }, out int touched);
            Assert.Equal(1, touched);
            Assert.NotNull(cut);
            Assert.True(Poly.IsSimple(cut) && Poly.IsCcw(cut));
            Assert.True(Overlap(cut, lot) < 0.01f);
            Assert.InRange(Poly.Area(cut), 10000 - 20 * 20 - 1, 10000 - 20 * 20 + 1);
        }

        [Fact]
        public void Cut_enclosed_lot_opens_a_corridor_without_holes()
        {
            var ring = Rect2(0, 0, 100, 100);
            var lot = Rect2(40, 40, 60, 60);
            var cut = ObstacleCut.Subtract(ring, new List<float2[]> { lot }, out _);
            Assert.NotNull(cut);
            Assert.True(Poly.IsSimple(cut));
            Assert.True(Overlap(cut, lot) < 0.01f);
            Assert.True(Poly.Area(cut) > 10000 - 400 - 100);   // the lot plus a 1 m corridor
            Assert.True(SurfaceAcceptance.IsAccepted(cut));
        }

        [Fact]
        public void Cut_ring_fully_covered_by_lots_is_null()
        {
            var ring = Rect2(10, 10, 20, 20);
            Assert.Null(ObstacleCut.Subtract(ring, new List<float2[]> { Rect2(0, 0, 30, 30) }, out int touched));
            Assert.Equal(1, touched);
        }

        [Fact]
        public void Grow_adds_the_margin()
        {
            var g = ObstacleCut.Grow(new[] { Rect2(0, 0, 10, 10) }, 0.5f);
            Assert.Single(g);
            Assert.InRange(Poly.Area(g[0]), 11 * 11 - 0.5f, 11 * 11 + 0.5f);
        }

        // ───────────── SiteSafety ─────────────

        [Fact]
        public void Only_buildings_are_protected()
        {
            var site = SiteWith(Lot(0, 0, 10, 10), Lot(20, 0, 30, 10, ObstacleKind.Water), Lot(40, 0, 50, 10, ObstacleKind.Utility));
            Assert.Single(SiteSafety.Protected(site));
            Assert.Empty(SiteSafety.Protected(new SiteContext()));
        }

        [Fact]
        public void Nets_through_a_lot_are_dropped_and_attached_objects_follow()
        {
            var layer = new AreaPlan();
            layer.Nets.Add(Line(new float2(0, 50), new float2(100, 50)));     // crosses the lot
            layer.Nets.Add(Line(new float2(0, 90), new float2(100, 90)));     // clear
            layer.Nets.Add(Line(new float2(0, 10), new float2(100, 10)));     // clear
            layer.Objects.Add(new PlannedObject { Position = new float3(10, 0, 50), Role = ObjectRole.Lamp, AttachedNet = 0 });
            layer.Objects.Add(new PlannedObject { Position = new float3(10, 0, 10), Role = ObjectRole.Lamp, AttachedNet = 2 });
            var plan = PlanOf(layer);
            var r = SiteSafety.Apply(plan, new AreaShape { Ring = Rect3(0, 0, 100, 100) }, SiteWith(Lot(40, 40, 60, 60)));
            Assert.Equal(1, r.Nets);
            Assert.Equal(2, layer.Nets.Count);
            Assert.Single(layer.Objects);
            Assert.Equal(1, layer.Objects[0].AttachedNet);   // was net 2, now net 1
            Assert.Equal(10f, layer.Nets[layer.Objects[0].AttachedNet].A.z);
            Assert.Equal(1, r.BuildingsInArea);
            Assert.Contains(plan.Issues, i => i.Key == SiteSafety.IssueKept && (int)i.Args[0] == 1);
            Assert.Contains(plan.Issues, i => i.Key == SiteSafety.IssueLeftOut);
        }

        [Fact]
        public void Net_passing_beside_a_lot_with_its_width_inside_the_margin_is_dropped()
        {
            var lot = Rect2(40, 40, 60, 60);
            // Centre line 4 m from the lot: a 8 m wide path (half 4) touches it, a 3 m one does not.
            var near = Line(new float2(0, 64), new float2(100, 64));
            Assert.True(SiteSafety.NetTouches(near, 4f, lot));
            Assert.False(SiteSafety.NetTouches(near, 1.5f, lot));
        }

        [Fact]
        public void Objects_on_a_lot_are_dropped_markers_stay()
        {
            var layer = new AreaPlan();
            layer.Objects.Add(new PlannedObject { Position = new float3(50, 0, 50), Role = ObjectRole.Tree });
            layer.Objects.Add(new PlannedObject { Position = new float3(50, 0, 50), Role = ObjectRole.Marker });
            layer.Objects.Add(new PlannedObject { Position = new float3(62.2f, 0, 50), Role = ObjectRole.Bench });   // 2.2 m off: 0.8 m radius + 0.3 m gap clear
            layer.Objects.Add(new PlannedObject { Position = new float3(61f, 0, 50), Role = ObjectRole.Tree });     // 2.5 m crown core reaches in
            var plan = PlanOf(layer);
            var r = SiteSafety.Apply(plan, null, SiteWith(Lot(40, 40, 60, 60)));
            Assert.Equal(2, r.Objects);
            Assert.Contains(layer.Objects, o => o.Role == ObjectRole.Marker);
            Assert.Contains(layer.Objects, o => o.Role == ObjectRole.Bench);
            Assert.DoesNotContain(layer.Objects, o => o.Role == ObjectRole.Tree);
        }

        [Fact]
        public void Object_footprint_from_asset_bounds_is_used()
        {
            var assets = new Dictionary<string, AssetInfo>
            {
                ["Big"] = new AssetInfo { BoundsMin = new float3(-6, 0, -1), BoundsMax = new float3(6, 3, 1) },
            };
            var lot = Rect2(40, 40, 60, 60);
            // 5 m from the lot: the 12 m wide box reaches in when it lies along x, not when turned 90°.
            var along = new PlannedObject { Position = new float3(50, 0, 66), Role = ObjectRole.Furniture, Asset = new AssetRef("Big") };
            var across = new PlannedObject { Position = new float3(66, 0, 50), Role = ObjectRole.Furniture, Asset = new AssetRef("Big") };
            Assert.False(SiteSafety.ObjectTouches(along, assets, lot));
            Assert.True(SiteSafety.ObjectTouches(across, assets, lot));
        }

        [Fact]
        public void Surfaces_are_cut_around_lots_into_acceptable_pieces()
        {
            var layer = new AreaPlan();
            layer.Surfaces.Add(new PlannedSurface { Ring = Rect3(0, 0, 100, 100), Role = SurfaceRole.Grass });   // encloses the lot
            layer.Surfaces.Add(new PlannedSurface { Ring = Rect3(200, 0, 220, 20), Role = SurfaceRole.Paving });  // far away
            var plan = PlanOf(layer);
            var lot = Rect2(40, 40, 60, 60);
            var r = SiteSafety.Apply(plan, null, new SiteContext { Obstacles = { Lot(40, 40, 60, 60) }, HeightAt = _ => 3f });
            Assert.Equal(1, r.Cut);
            Assert.Equal(0, r.Surfaces);
            var grass = layer.Surfaces.Where(s => s.Role == SurfaceRole.Grass).ToList();
            Assert.NotEmpty(grass);
            foreach (var s in grass)
            {
                var ring = Poly.To2D(s.Ring);
                Assert.True(SurfaceAcceptance.IsAccepted(ring));
                Assert.True(Overlap(ring, lot) < 0.05f);
                Assert.All(s.Ring, p => Assert.Equal(3f, p.y));
            }
            float area = grass.Sum(s => Poly.Area(Poly.To2D(s.Ring)));
            Assert.InRange(area, 10000 - 21 * 21 - 5, 10000 - 21 * 21 + 5);   // lot + 0.5 m margin, seams lose nothing
            Assert.Contains(layer.Surfaces, s => s.Role == SurfaceRole.Paving);
        }

        [Fact]
        public void Terraform_paint_and_areas_are_cut_and_zoning_on_a_lot_dropped()
        {
            var layer = new AreaPlan();
            layer.Terraform.Add(new PlannedTerraform { Ring = Rect3(0, 0, 100, 100), TargetHeight = 5f });
            layer.Paint.Add(new PlannedPaint { Ring = Rect3(30, 30, 70, 70), Layer = 2 });
            layer.Areas.Add(new PlannedArea { Ring = Rect3(45, 45, 55, 55), Role = AreaRole.Lot });   // inside the lot: gone
            layer.Zoning.Add(new PlannedZoning { A = new float3(40, 0, 40), B = new float3(60, 0, 40), C = new float3(60, 0, 48), D = new float3(40, 0, 48), ZoneId = "x" });
            layer.Zoning.Add(new PlannedZoning { A = new float3(40, 0, 60), B = new float3(60, 0, 60), C = new float3(60, 0, 68), D = new float3(40, 0, 68), ZoneId = "x" });   // shares the edge only
            var plan = PlanOf(layer);
            var lot = Rect2(40, 40, 60, 60);
            var r = SiteSafety.Apply(plan, null, SiteWith(Lot(40, 40, 60, 60)));
            Assert.Equal(1, r.Areas);
            Assert.Empty(layer.Areas);
            Assert.Equal(1, r.Zoning);
            Assert.Single(layer.Zoning);
            Assert.All(layer.Terraform, t => Assert.True(Overlap(Poly.To2D(t.Ring), lot) < 0.05f));
            Assert.All(layer.Terraform, t => Assert.Equal(5f, t.TargetHeight));
            Assert.All(layer.Paint, p => Assert.True(Overlap(Poly.To2D(p.Ring), lot) < 0.05f));
            Assert.NotEmpty(layer.Paint);
        }

        [Fact]
        public void No_buildings_no_change()
        {
            var layer = new AreaPlan();
            layer.Nets.Add(Line(new float2(0, 50), new float2(100, 50)));
            layer.Surfaces.Add(new PlannedSurface { Ring = Rect3(0, 0, 100, 100) });
            var plan = PlanOf(layer);
            var r = SiteSafety.Apply(plan, new AreaShape { Ring = Rect3(0, 0, 100, 100) }, new SiteContext());
            Assert.Equal(0, r.LeftOut + r.Cut + r.BuildingsInArea);
            Assert.Single(layer.Nets);
            Assert.Single(layer.Surfaces);
            Assert.Empty(plan.Issues);
        }

        [Fact]
        public void Items_touching_a_warned_building_are_found_for_the_build_guard()
        {
            var layer = new AreaPlan();
            layer.Nets.Add(Line(new float2(0, 50), new float2(100, 50)));
            layer.Nets.Add(Line(new float2(0, 90), new float2(100, 90)));
            layer.Objects.Add(new PlannedObject { Position = new float3(45, 0, 45), Role = ObjectRole.Tree });
            layer.Areas.Add(new PlannedArea { Ring = Rect3(30, 30, 45, 45), Role = AreaRole.Lot });
            var plan = PlanOf(layer);
            var work = PlanWork.Flatten(plan);
            var hits = SiteSafety.ItemsTouching(plan, work, new List<float2[]> { Rect2(40, 40, 60, 60) }, null);
            Assert.Equal(3, hits.Count);
            Assert.Contains(hits, w => w.Kind == WorkKind.Net && w.Index == 0);
            Assert.DoesNotContain(hits, w => w.Kind == WorkKind.Net && w.Index == 1);
            Assert.Contains(hits, w => w.Kind == WorkKind.Object);
            Assert.Contains(hits, w => w.Kind == WorkKind.Area);
        }

        [Fact]
        public void Declutter_rechecks_objects_against_buildings()
        {
            var layer = new AreaPlan();
            layer.Objects.Add(new PlannedObject { Position = new float3(50, 0, 50), Role = ObjectRole.Bench, Asset = new AssetRef("B") });
            layer.Objects.Add(new PlannedObject { Position = new float3(90, 0, 90), Role = ObjectRole.Bench, Asset = new AssetRef("B") });
            var plan = PlanOf(layer);
            plan.Protected.Add(Rect2(40, 40, 60, 60));
            var assets = new Dictionary<string, AssetInfo> { ["B"] = new AssetInfo { BoundsMin = new float3(-1, 0, -0.5f), BoundsMax = new float3(1, 1, 0.5f) } };
            Declutter.Run(plan, assets);
            Assert.Single(layer.Objects);
            Assert.Equal(90f, layer.Objects[0].Position.x);
        }

        [Fact]
        public void Composed_park_keeps_off_a_building_in_the_middle()
        {
            var ring = TestKit.Rect(220, 160);
            var site = TestKit.RoadAlongBottom(220);
            site.Obstacles.Add(Lot(90, 60, 130, 100));   // a 40 × 40 m lot in the middle of the park
            var recipe = TestKit.Entered(Recipe.Single(TestKit.Gen("park"), 7), ring, site);
            var plan = Composer.Compose(recipe, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            var lot = Rect2(90, 60, 130, 100);
            foreach (var l in plan.Layers)
            {
                foreach (var n in l.Nets) Assert.False(SiteSafety.NetTouches(n, Declutter.NetHalfWidth(n, null), lot, 0f), $"{n.Role} crosses the lot");
                foreach (var o in l.Objects) if (o.Role != ObjectRole.Marker) Assert.False(SiteSafety.ObjectTouches(o, null, lot, 0f), $"{o.Role} at {o.Position} on the lot");
                foreach (var s in l.Surfaces) Assert.True(Overlap(Poly.To2D(s.Ring), lot) < 0.5f, $"{s.Role} surface covers the lot");
                foreach (var t in l.Terraform) Assert.True(Overlap(Poly.To2D(t.Ring), lot) < 0.5f, "terraform covers the lot");
            }
            Assert.Contains(plan.Issues, i => i.Key == SiteSafety.IssueKept && (int)i.Args[0] == 1);
            Assert.Same(plan.Protected, plan.Protected);
            Assert.NotEmpty(plan.Protected);
        }
    }
}
