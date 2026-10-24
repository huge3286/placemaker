// s19: zoned buildings grew over a plaza. Core/Build/ZoneGuard decides which cells of the roads' zone blocks a
// build zones (its cutouts, snapped to the real cells) and which it blocks (everything else under it), and the
// blocker rings that make CS2's BlockCellsJob block exactly those.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Common;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class ZoneGuardTests
    {
        private static float2[] Rect(float x0, float z0, float x1, float z1) =>
            new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) };

        /// <summary>A road along z = 0 (north of the block), its zone block south of it: 10 × 6 cells, x 0…80, z −48…0.</summary>
        private static ZoneGrid Block() => new ZoneGrid { Position = new float2(40, -24), Direction = new float2(0, 1), Size = new int2(10, 6) };

        private static List<(int x, int y)> CellsWithCentreIn(ZoneGrid g, float2[] quad)
        {
            var r = new List<(int, int)>();
            for (int x = 0; x < g.Size.x; x++)
                for (int y = 0; y < g.Size.y; y++)
                    if (Poly.Contains(quad, ZoneGuard.CellCentre(g, x, y))) r.Add((x, y));
            return r;
        }

        private static ZoneGuard.Result Run(float2[] footprint, float2[] quad, IReadOnlyList<float2[]> lots = null, bool grid = true) =>
            ZoneGuard.Compute(new[] { footprint }, quad != null ? new[] { quad } : new float2[0][], quad != null ? new[] { "EU Commercial Low" } : new string[0],
                lots ?? new List<float2[]>(), grid ? new[] { Block() } : new ZoneGrid[0]);

        private static bool AnyBlockerTouches(ZoneGuard.Result r, float2[] square) =>
            ZoneGuard.Blocks(r.BlockerRegions, square) || r.Blockers.Any(b => ObstacleCut.Overlaps(square, b));

        // ───────────── cell geometry ─────────────

        [Fact]
        public void Cell_geometry_matches_ZoneUtils()
        {
            var g = Block();
            // Row 0 is next to the road (+z), column 0 on the block's right (+x for a block facing +z).
            Assert.Equal(new float2(76, -4), ZoneGuard.CellCentre(g, 0, 0));
            Assert.Equal(new float2(4, -44), ZoneGuard.CellCentre(g, 9, 5));
            var sq = ZoneGuard.CellSquare(g, 0, 0);
            Assert.True(Poly.IsCcw(sq));
            Assert.InRange(Poly.Area(sq), 63.99f, 64.01f);
            var rect = ZoneGuard.CellRect(g, 5, 7, 0, 1);
            Assert.InRange(Poly.Area(rect), 3 * 2 * 64 - 0.01f, 3 * 2 * 64 + 0.01f);
            Assert.Equal(6, CellsWithCentreIn(g, rect).Count);
        }

        // ───────────── cell selection ─────────────

        [Fact]
        public void Cutout_gets_exactly_its_cells_and_the_rest_of_the_park_is_blocked()
        {
            var g = Block();
            var foot = Rect(0, -60, 80, 0);
            var quad = Rect(16, -17, 40, 1);   // a 24 m × 16 m cutout (+1 m on the road side, like EdgeZoning)
            var r = Run(foot, quad);

            var zoned = r.Cells.Where(c => c.State == ZoneCellState.Zoned).ToList();
            Assert.Equal(6, zoned.Count);                       // 3 columns × 2 rows: the cutout depth only
            Assert.All(zoned, c => Assert.Equal("EU Commercial Low", c.ZoneId));
            Assert.Equal(60 - 6, r.Cells.Count(c => c.State == ZoneCellState.Blocked));
            Assert.True(r.Snapped[0]);

            // The zoning quad the build paints takes exactly these cells (Marquee: cell centre inside the quad).
            Assert.Single(r.Zoning);
            var picked = CellsWithCentreIn(g, r.Zoning[0].Corners);
            Assert.Equal(6, picked.Count);
            Assert.All(picked, p => Assert.InRange(p.y, 0, 1));

            // No blocker touches a zoned cell; every other cell of the park is touched (or blocked behind one).
            foreach (var (x, y) in picked) Assert.False(AnyBlockerTouches(r, ZoneGuard.CellSquare(g, x, y, -0.02f)), $"blocker on zoned cell {x},{y}");
            for (int x = 0; x < 10; x++)
                if (!picked.Any(p => p.x == x))
                    Assert.True(ZoneGuard.Blocks(r.BlockerRegions, ZoneGuard.CellSquare(g, x, 0, -0.02f)), $"front cell of column {x} not blocked");
            // Behind the cutout (row 2) the park is blocked, so buildings stay cutout-deep.
            foreach (var x in picked.Select(p => p.x).Distinct())
                Assert.True(ZoneGuard.Blocks(r.BlockerRegions, ZoneGuard.CellSquare(g, x, 2, -0.02f)));
            Assert.NotEmpty(r.Blockers);
            Assert.All(r.Blockers, b => Assert.True(SurfaceAcceptance.IsAccepted(b)));
        }

        [Fact]
        public void Misaligned_cutout_snaps_to_the_cells_by_centre()
        {
            var r = Run(Rect(0, -60, 80, 0), Rect(13, -19, 43, 1));
            var zoned = r.Cells.Where(c => c.State == ZoneCellState.Zoned).ToList();
            Assert.Equal(6, zoned.Count);   // centres x 20/28/36, rows 0–1; x 44 and row 2 (z −20) are outside
            Assert.Contains(r.ZonedSquares, s => Poly.Contains(s, new float2(20, -12)));
        }

        [Fact]
        public void One_row_cutout_is_zoned_two_rows_deep()
        {
            // Vanilla blocks row 0 when row 1 is blocked (CleanBlockedCells), so one row alone would never grow.
            var r = Run(Rect(0, -60, 80, 0), Rect(16, -8, 40, 1));
            Assert.Equal(6, r.Cells.Count(c => c.State == ZoneCellState.Zoned));
        }

        [Fact]
        public void Front_strip_between_outline_and_road_is_zoned_with_the_cutout()
        {
            // The outline keeps 6 m off the road: row 0 lies mostly outside it; the cutout starts at the outline.
            var g = Block();
            var r = Run(Rect(0, -60, 80, -6), Rect(16, -23, 40, -5));
            var picked = CellsWithCentreIn(g, r.Zoning.Single().Corners);
            Assert.Equal(9, picked.Count);   // rows 0 (front strip), 1, 2
            Assert.Contains(picked, p => p.y == 0);
            // Other columns: row 0 is still blocked (the inset footprint reaches into it).
            Assert.True(ZoneGuard.Blocks(r.BlockerRegions, ZoneGuard.CellSquare(g, 0, 0, -0.02f)));
        }

        [Fact]
        public void Cutout_behind_park_land_is_not_zoned_and_stays_a_polygon()
        {
            // Cells whose front (road side) is park can never host a building of this road: no zoning there.
            var quad = Rect(16, -31, 40, -25);
            var r = Run(Rect(0, -60, 80, 0), quad);
            Assert.False(r.Snapped[0]);
            Assert.Empty(r.Zoning);
            Assert.Equal(0, r.Cells.Count(c => c.State == ZoneCellState.Zoned));
            // The quad stays free of blockers (another road — e.g. a lane this build adds — may zone it).
            var inner = new[] { new float2(20, -28) };
            Assert.DoesNotContain(r.Blockers, b => Poly.Contains(b, inner[0]));
        }

        [Fact]
        public void Existing_building_lot_keeps_its_cells()
        {
            var g = Block();
            var lot = Rect(72, -24, 80, 0);   // column 0, rows 0–2 (a grown house)
            var r = Run(Rect(0, -60, 80, 0), Rect(16, -17, 40, 1), new List<float2[]> { lot });
            for (int y = 0; y < 3; y++)
                Assert.False(ZoneGuard.Blocks(r.BlockerRegions, ZoneGuard.CellSquare(g, 0, y, -0.02f)), $"lot cell row {y} blocked");
            Assert.Equal(3, r.Cells.Count(c => c.State == ZoneCellState.Kept));
            Assert.All(r.Blockers, b => Assert.False(ObstacleCut.Overlaps(b, lot)));
        }

        [Fact]
        public void Cutout_cells_never_zone_over_a_building()
        {
            var g = Block();
            var lot = Rect(32, -16, 40, 0);   // column 5 (x 32–40), rows 0–1, inside the cutout
            var r = Run(Rect(0, -60, 80, 0), Rect(16, -17, 40, 1), new List<float2[]> { lot });
            Assert.Equal(4, r.Cells.Count(c => c.State == ZoneCellState.Zoned));
            Assert.DoesNotContain(r.Zoning.SelectMany(z => CellsWithCentreIn(g, z.Corners)), p => p.x == 5);
        }

        [Fact]
        public void Without_captured_blocks_the_quads_are_kept_and_kept_free()
        {
            var quad = Rect(16, -17, 40, 1);
            var r = Run(Rect(0, -60, 80, 0), quad, grid: false);
            Assert.False(r.Snapped[0]);
            Assert.Empty(r.Zoning);
            Assert.Empty(r.Cells);
            Assert.NotEmpty(r.Blockers);
            Assert.All(r.Blockers, b => Assert.False(ObstacleCut.Overlaps(b, Rect(16.2f, -16.8f, 39.8f, -0.6f))));
            float covered = r.Blockers.Sum(b => Poly.Area(b));
            Assert.InRange(covered, 79 * 59.5f - 25 * 18 - 60, 79 * 59.5f - 24 * 16);
        }

        [Fact]
        public void Park_without_cutouts_blocks_every_cell()
        {
            var r = Run(Rect(0, -60, 80, 0), null);
            Assert.Equal(60, r.Cells.Count(c => c.State == ZoneCellState.Blocked));
            Assert.Empty(r.Zoning);
        }

        [Fact]
        public void Blocks_far_away_are_ignored()
        {
            var far = new ZoneGrid { Position = new float2(5000, 5000), Direction = new float2(0, 1), Size = new int2(10, 6) };
            var r = ZoneGuard.Compute(new[] { Rect(0, -60, 80, 0) }, new float2[0][], new string[0], null, new[] { far });
            Assert.Empty(r.Cells);
            Assert.NotEmpty(r.Blockers);
        }

        // ───────────── plan integration ─────────────

        [Fact]
        public void Apply_replaces_snapped_quads_and_sets_the_blockers()
        {
            var plan = new ComposedPlan();
            var zoning = new AreaPlan { GeneratorId = "edgeZoning" };
            zoning.Zoning.Add(new PlannedZoning
            {
                A = new float3(16, 0, 1), B = new float3(40, 0, 1), C = new float3(40, 0, -17), D = new float3(16, 0, -17), ZoneId = "EU Commercial Low",
            });
            var park = new AreaPlan { GeneratorId = "park" };
            park.Objects.Add(new PlannedObject { Role = ObjectRole.Furniture, Position = new float3(30, 0, -15.5f) });   // on a zoned cell, just outside the quad? (inside)
            park.Objects.Add(new PlannedObject { Role = ObjectRole.Furniture, Position = new float3(60, 0, -30) });
            plan.Layers.Add(park);
            plan.Layers.Add(zoning);
            plan.Footprints.Add(Rect(0, -60, 80, 0));
            var site = new SiteContext();
            site.ZoneGrids.Add(Block());

            ZoneGuard.Apply(plan, site);

            Assert.Single(zoning.Zoning);
            var picked = CellsWithCentreIn(Block(), Poly.To2D(new[] { zoning.Zoning[0].A, zoning.Zoning[0].B, zoning.Zoning[0].C, zoning.Zoning[0].D }));
            Assert.Equal(6, picked.Count);
            Assert.NotEmpty(plan.ZoneBlockers);
            Assert.Equal(6, plan.ZoneCells.Count(c => c.State == ZoneCellState.Zoned));
            // The park object on a zoned cell outside any cutout region is cleared; the one in the park stays.
            Assert.Single(park.Objects);
            Assert.Equal(60f, park.Objects[0].Position.x);
        }

        [Fact]
        public void Apply_without_footprint_does_nothing()
        {
            var plan = new ComposedPlan();
            var zoning = new AreaPlan { GeneratorId = "edgeZoning" };
            zoning.Zoning.Add(new PlannedZoning { A = new float3(16, 0, 1), B = new float3(40, 0, 1), C = new float3(40, 0, -17), D = new float3(16, 0, -17) });
            plan.Layers.Add(zoning);
            var site = new SiteContext();
            site.ZoneGrids.Add(Block());
            ZoneGuard.Apply(plan, site);
            Assert.Empty(plan.ZoneBlockers);
            Assert.Equal(16f, zoning.Zoning[0].A.x);
        }

        // ───────────── composed types ─────────────

        /// <summary>Zone blocks of the four streets of <see cref="ZoningCutoutTests.Streets"/>: front edge 2 m outside the outline.</summary>
        private static void AddBlocks(SiteContext site, float3[] ring)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                float2 p0 = ring[i].xz, p1 = ring[(i + 1) % ring.Length].xz;
                float2 d = math.normalize(p1 - p0);
                float2 outward = new float2(d.y, -d.x);
                int cols = (int)(math.distance(p0, p1) / 8f);
                site.ZoneGrids.Add(new ZoneGrid { Position = 0.5f * (p0 + p1) - outward * 22f, Direction = outward, Size = new int2(cols, 6) });
            }
        }

        [Theory]
        [InlineData("plaza")]
        [InlineData("park")]
        [InlineData("marketSquare")]
        public void Composed_type_blocks_park_cells_and_zones_only_cutouts(string type)
        {
            var editor = new DesignEditor(GeneratorRegistry.Get);
            var ring = TestKit.Rect(130, 130);
            var site = ZoningCutoutTests.Streets(ring);
            AddBlocks(site, ring);
            var r = editor.SingleLayerRecipe(GeneratorRegistry.Get(type), 9);
            editor.SetFeature(r, "0", LayerBase.StreetShopsLayer, true, 31);
            r.Root.Settings.Values[StreetShops.LayoutId] = "band";
            r.Root.Settings.Values[StreetShops.AmountId] = StreetShops.Lots;
            TestKit.Entered(r, ring, site);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);

            Assert.Single(plan.Footprints);
            Assert.NotEmpty(plan.ZoneBlockers);
            var zoned = plan.ZoneCells.Where(c => c.State == ZoneCellState.Zoned).ToList();
            Assert.NotEmpty(zoned);
            Assert.Contains(plan.ZoneCells, c => c.State == ZoneCellState.Blocked);
            // No blocker touches a zoned cell; no zoning quad of the plan takes the centre of a blocked cell.
            var quads = plan.Layers.SelectMany(l => l.Zoning).Select(z => Poly.To2D(new[] { z.A, z.B, z.C, z.D })).ToList();
            foreach (var c in zoned)
            {
                var sq = Shrink(new[] { c.A, c.B, c.C, c.D }, 0.02f);
                Assert.False(plan.ZoneBlockers.Any(b => ObstacleCut.Overlaps(sq, b)), $"{type}: blocker on a zoned cell at {Poly.Centroid(sq)}");
            }
            // A blocked cell may share its square with a zoned cell of the crossing street's block (corners): the game
            // zones only visible cells, so only a quad over a cell that no block zones is a mistake.
            foreach (var c in plan.ZoneCells.Where(c => c.State == ZoneCellState.Blocked))
            {
                var centre = Poly.Centroid(new[] { c.A, c.B, c.C, c.D });
                bool zonedThere = zoned.Any(z => Poly.Contains(new[] { z.A, z.B, z.C, z.D }, centre));
                Assert.False(!zonedThere && quads.Any(q => Poly.Contains(q, centre)), $"{type}: zoning over a blocked cell at {centre}");
            }
            Assert.All(plan.ZoneBlockers, b => Assert.True(SurfaceAcceptance.IsAccepted(b)));
        }

        [Fact]
        public void Roads_recipe_has_no_footprint()
        {
            var plan = new ComposedPlan();
            ZoneGuard.Apply(plan, new SiteContext());
            Assert.Empty(plan.ZoneBlockers);
            Assert.True(GeneratorRegistry.IsLoose("roads"));
        }

        private static float2[] Shrink(float2[] q, float by)
        {
            var c = Poly.Centroid(q);
            return q.Select(p => p + math.normalizesafe(c - p) * by).ToArray();
        }
    }
}
