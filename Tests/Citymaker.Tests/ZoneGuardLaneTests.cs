// s26: cutouts that actually zone. Playtests: (a) 12 of 16 cutout cells blocked after the build — the park's paths
// split the streets' edges, the game rebuilt their zone blocks a metre or two off the captured grid and the blockers
// covered the new cells (ZoneGuard.Refresh re-snaps to the live blocks after Apply); (b) shop plots facing a lane the
// build adds floated and never zoned, shops grew facing the street BEHIND (lane blocks predicted by
// ZoneBlockPredictor, the facing road ranked first, a column zones one plot only); (c) lanes cut by cutout
// enforcement ended in dead-end caps (roads are never clipped); (d) cutouts of two roads crossing at angles.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Citymaker.Core.Layers.Common;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class ZoneGuardLaneTests
    {
        private static float2[] Rect(float x0, float z0, float x1, float z1) =>
            new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) };

        private static PlannedNet Net(float2 a, float2 b, NetRole role, string asset) => new PlannedNet
        {
            A = new float3(a.x, 0, a.y), B = new float3(math.lerp(a, b, 1f / 3f).x, 0, math.lerp(a, b, 1f / 3f).y),
            C = new float3(math.lerp(a, b, 2f / 3f).x, 0, math.lerp(a, b, 2f / 3f).y), D = new float3(b.x, 0, b.y), Role = role, Asset = new AssetRef(asset),
        };

        private static float2 Centre(ZoneCellMark c) => 0.25f * (c.A + c.B + c.C + c.D);

        // ───────────── ZoneBlockPredictor (port of BlockSystem.CreateBlocks) ─────────────

        [Fact]
        public void Straight_road_gets_blocks_on_both_sides_flush_with_its_edge()
        {
            var blocks = ZoneBlockPredictor.Blocks(new float3(0, 0, 0), new float3(30, 0, 0), new float3(60, 0, 0), new float3(80, 0, 0), 16f);
            Assert.Equal(2, blocks.Count);
            foreach (var g in blocks)
            {
                Assert.Equal(10, g.Size.x);
                Assert.Equal(6, g.Size.y);
                // Row 0 lies against the road edge (8 m from the centre line), 8 m cells.
                var c0 = ZoneGuard.CellCentre(g, 0, 0);
                Assert.Equal(12f, math.abs(c0.y), 2);
                var sq = ZoneGuard.CellSquare(g, 0, 0);
                Assert.Equal(8f, sq.Min(p => math.abs(p.y)), 2);
                // Direction points at the road.
                Assert.True(math.dot(g.Direction, new float2(0, -math.sign(g.Position.y))) > 0.99f);
            }
            Assert.Contains(blocks, g => g.Position.y > 0);
            Assert.Contains(blocks, g => g.Position.y < 0);
        }

        [Fact]
        public void Alley_blocks_start_one_cell_out_and_long_roads_split_into_ten_cell_blocks()
        {
            var alley = ZoneBlockPredictor.Blocks(new float3(0, 0, 0), new float3(10, 0, 0), new float3(20, 0, 0), new float3(40, 0, 0), 8f);
            // An 8 m alley is one cell wide (ZoneUtils.GetCellWidth): its blocks start 4 m from the centre line.
            Assert.All(alley, g => Assert.Equal(4f, ZoneGuard.CellSquare(g, 0, 0).Min(p => math.abs(p.y)), 2));
            var longRoad = ZoneBlockPredictor.Blocks(new float3(0, 0, 0), new float3(60, 0, 0), new float3(120, 0, 0), new float3(200, 0, 0), 16f);
            Assert.All(longRoad, g => Assert.InRange(g.Size.x, 2, 10));
            Assert.Equal(25 * 2, longRoad.Sum(g => g.Size.x));
        }

        [Fact]
        public void Curved_road_blocks_are_rotated_pieces_along_the_curve()
        {
            float r = 100f, k = 4f / 3f * math.tan(math.radians(90f) / 4f) * r;
            var a = new float3(r, 0, 0); var d = new float3(0, 0, r);
            var blocks = ZoneBlockPredictor.Blocks(a, a + new float3(0, 0, k), d + new float3(k, 0, 0), d, 16f);
            Assert.True(blocks.Count >= 4);
            // Outer side: every block's row 0 sits on the road's edge (radius 108), facing the centre.
            foreach (var g in blocks.Where(g => math.length(g.Position) > r))
            {
                float rr = math.length(ZoneGuard.CellCentre(g, g.Size.x / 2, 0));
                Assert.InRange(rr, r + 8f + 3f, r + 8f + 9f);
                // Row 0 never reaches onto the road.
                Assert.True(ZoneGuard.CellSquare(g, g.Size.x / 2, 0).Min(p => math.length(p)) > r + 8f - 0.3f);
                Assert.True(math.dot(math.normalize(g.Direction), -math.normalize(g.Position)) > 0.95f);
            }
        }

        // ───────────── lane-facing plots ─────────────

        /// <summary>
        /// A shopping-plaza strip: street along z = −8 (16 m, its captured block on the north side), our lane along
        /// z = 40 (8 m alley), a plot 16 m deep between them facing the lane (z 20…36… flush to the lane's south edge).
        /// </summary>
        private static (ComposedPlan plan, SiteContext site, float2[] plot) LanePlot()
        {
            var plan = new ComposedPlan();
            var foot = Rect(0, 0, 120, 80);
            plan.Footprints.Add(foot);
            var host = new AreaPlan { GeneratorId = "shoppingPlaza" };
            host.Nets.Add(Net(new float2(-10, 40), new float2(130, 40), NetRole.Alley, "Alley"));
            var plot = Rect(40, 20, 64, 36);   // the lane's south edge is z = 36
            host.Zoning.Add(new PlannedZoning { A = new float3(40, 0, 20), B = new float3(64, 0, 20), C = new float3(64, 0, 37), D = new float3(40, 0, 37), ZoneId = "EU Commercial Low" });
            plan.Layers.Add(host);
            var site = new SiteContext();
            site.NearbyNets.Add(new NearbyNet { A = new float3(-40, 0, -8), B = new float3(20, 0, -8), C = new float3(100, 0, -8), D = new float3(160, 0, -8), Width = 16f, Category = NetCategory.Road });
            // The street's north block: front at z = 0, 6 rows up to z = 48 — it reaches the plot (rows 2–4) AND crosses the lane.
            site.ZoneGrids.Add(new ZoneGrid { Position = new float2(60, 24), Direction = new float2(0, -1), Size = new int2(15, 6) });
            return (plan, site, plot);
        }

        [Fact]
        public void Plot_facing_a_planned_lane_zones_from_the_lane_not_the_street_behind()
        {
            var (plan, site, plot) = LanePlot();
            ZoneGuard.Apply(plan, site);
            var zoned = plan.ZoneCells.Where(c => c.State == ZoneCellState.Zoned).ToList();
            Assert.NotEmpty(zoned);
            Assert.DoesNotContain(plan.Issues, i => i.Key == ZoneGuard.NotZonableKey);
            // Every zoned cell lies in the plot, flush against the lane: its row-0 edge on z = 36.
            foreach (var c in zoned) Assert.True(Poly.Contains(Rect(39.5f, 19.5f, 64.5f, 36.5f), Centre(c)), $"cell at {Centre(c)} outside the plot");
            Assert.Contains(zoned, c => math.abs(math.max(math.max(c.A.y, c.B.y), math.max(c.C.y, c.D.y)) - 36f) < 0.05f);
            // The lane is a road: it blocks the street's cells under it, so the street never zones the plot (no cell
            // of the street's grid among the zoned ones: the street's rows sit at z 0–8, 8–16…, the lane's at 28–36, 20–28).
            Assert.All(zoned, c => Assert.True(math.abs((Centre(c).y - 32f) % 8f) < 0.05f || math.abs((Centre(c).y - 24f) % 8f) < 0.05f));
            Assert.DoesNotContain(zoned, c => math.abs(Centre(c).y - 20f) < 0.05f || math.abs(Centre(c).y - 28f) < 0.05f);
        }

        [Fact]
        public void Zoned_cells_of_two_roads_never_overlap()
        {
            // Corner: a street along z = −8 (north block) and one along x = −8 (east block); a cutout in the corner.
            var foot = Rect(0, 0, 80, 80);
            var grids = new List<ZoneGrid>
            {
                new ZoneGrid { Position = new float2(40, 24), Direction = new float2(0, -1), Size = new int2(10, 6) },
                new ZoneGrid { Position = new float2(24, 40), Direction = new float2(-1, 0), Size = new int2(10, 6) },
            };
            var quads = new[] { Rect(-1, -1, 30, 30) };
            var r = ZoneGuard.Compute(new[] { foot }, quads, new[] { "EU Commercial Low" }, new List<float2[]>(), grids);
            var zoned = r.Cells.Where(c => c.State == ZoneCellState.Zoned).ToList();
            Assert.NotEmpty(zoned);
            for (int i = 0; i < zoned.Count; i++)
                for (int j = i + 1; j < zoned.Count; j++)
                {
                    var a = Shrink(new[] { zoned[i].A, zoned[i].B, zoned[i].C, zoned[i].D }, 0.5f);
                    var b = Shrink(new[] { zoned[j].A, zoned[j].B, zoned[j].C, zoned[j].D }, 0.5f);
                    Assert.False(ObstacleCut.Overlaps(a, b), $"cells at {Centre(zoned[i])} and {Centre(zoned[j])} overlap");
                }
        }

        [Fact]
        public void A_column_zones_one_plot_never_the_plot_behind_it()
        {
            // Two plots in a row from the street: the near one faces the street; the far one (behind it) faces nothing.
            var foot = Rect(0, 0, 80, 80);
            var grids = new List<ZoneGrid> { new ZoneGrid { Position = new float2(40, 24), Direction = new float2(0, -1), Size = new int2(10, 6) } };
            var near = Rect(16, -1, 40, 16);
            var far = Rect(16, 16, 40, 40);
            var r = ZoneGuard.Compute(new[] { foot }, new[] { near, far }, new[] { "z", "z" }, new List<float2[]>(), grids);
            Assert.True(r.Snapped[0]);
            Assert.False(r.Snapped[1]);
            Assert.All(r.Cells.Where(c => c.State == ZoneCellState.Zoned), c => Assert.True(Centre(c).y < 16f));
        }

        [Fact]
        public void A_plot_no_road_reaches_is_dropped_with_an_issue()
        {
            var plan = new ComposedPlan();
            plan.Footprints.Add(Rect(0, 0, 120, 120));
            var host = new AreaPlan { GeneratorId = "park" };
            host.Zoning.Add(new PlannedZoning { A = new float3(50, 0, 50), B = new float3(74, 0, 50), C = new float3(74, 0, 66), D = new float3(50, 0, 66), ZoneId = "z" });
            host.Zoning.Add(new PlannedZoning { A = new float3(16, 0, -1), B = new float3(40, 0, -1), C = new float3(40, 0, 16), D = new float3(16, 0, 16), ZoneId = "z" });
            plan.Layers.Add(host);
            var site = new SiteContext();
            site.ZoneGrids.Add(new ZoneGrid { Position = new float2(60, 24), Direction = new float2(0, -1), Size = new int2(15, 6) });
            ZoneGuard.Apply(plan, site);
            Assert.Single(plan.Issues, i => i.Key == ZoneGuard.NotZonableKey);
            // The floating plot is gone; the street plot is zoned; the blockers cover the dropped plot's land.
            Assert.DoesNotContain(host.Zoning, z => Poly.Contains(Rect(50, 50, 74, 66), 0.25f * (z.A.xz + z.B.xz + z.C.xz + z.D.xz)));
            Assert.Contains(plan.ZoneCells, c => c.State == ZoneCellState.Zoned);
            Assert.True(plan.ZoneBlockers.Any(b => Poly.Contains(b, new float2(62, 58))));
        }

        [Fact]
        public void The_builds_own_paths_close_the_cells_under_them()
        {
            var foot = Rect(0, 0, 80, 80);
            var grids = new List<ZoneGrid> { new ZoneGrid { Position = new float2(40, 24), Direction = new float2(0, -1), Size = new int2(10, 6) } };
            var quads = new[] { Rect(16, -1, 56, 16) };
            // A 4 m path along x = 28 from the street into the park crosses the second column of the cutout.
            var own = new List<(float2[] line, float half)> { (new[] { new float2(28, -2), new float2(28, 60) }, 2f) };
            var r = ZoneGuard.Compute(new[] { foot }, quads, new[] { "z" }, new List<float2[]>(), grids, null, true, own);
            var zoned = r.Cells.Where(c => c.State == ZoneCellState.Zoned).ToList();
            Assert.DoesNotContain(zoned, c => math.abs(Centre(c).x - 28f) < 0.1f);
            // (s36) The column left alone beside the path (x = 20) is blocked by the game's depth rule: not planned.
            Assert.DoesNotContain(zoned, c => math.abs(Centre(c).x - 20f) < 0.1f);
            Assert.Contains(zoned, c => math.abs(Centre(c).x - 36f) < 0.1f);
            Assert.Contains(zoned, c => math.abs(Centre(c).x - 52f) < 0.1f);
        }

        // ───────────── lanes are never cut by cutouts ─────────────

        [Fact]
        public void Cutout_enforcement_never_cuts_a_lane()
        {
            var plan = new ComposedPlan();
            var l = new AreaPlan { GeneratorId = "shoppingPlaza" };
            var lane = Net(new float2(0, 40), new float2(120, 40), NetRole.Alley, "Alley");
            var path = Net(new float2(50, 0), new float2(50, 80), NetRole.Pathway, "");
            l.Nets.Add(lane);
            l.Nets.Add(path);
            l.Objects.Add(new PlannedObject { Role = ObjectRole.Lamp, Position = new float3(50, 0, 70), AttachedNet = 1 });
            plan.Layers.Add(l);
            Cutouts.Enforce(plan, new List<float2[]> { Rect(40, 30, 64, 52) });
            Assert.Contains(l.Nets, n => n == lane && n.A.x == 0f && n.D.x == 120f);
            Assert.DoesNotContain(l.Nets, n => n.Role == NetRole.Pathway && Poly.Contains(Rect(40.5f, 30.5f, 63.5f, 51.5f), Composer.Bezier(n.A, n.B, n.C, n.D, 0.5f).xz));
            // The lamp follows its (clipped) path.
            var lamp = l.Objects.Single();
            Assert.True(lamp.AttachedNet >= 0 && l.Nets[lamp.AttachedNet].Role == NetRole.Pathway);
        }

        // ───────────── refresh against the live blocks after Apply ─────────────

        [Fact]
        public void Refresh_resnaps_cutouts_and_blockers_to_shifted_live_blocks()
        {
            var plan = new ComposedPlan();
            plan.Footprints.Add(Rect(0, 0, 120, 80));
            var host = new AreaPlan { GeneratorId = "park" };
            host.Zoning.Add(new PlannedZoning { A = new float3(16, 0, -1), B = new float3(40, 0, -1), C = new float3(40, 0, 16), D = new float3(40, 0, 16), ZoneId = "z" });
            host.Zoning[0].D = new float3(16, 0, 16);
            plan.Layers.Add(host);
            var designGrid = new ZoneGrid { Position = new float2(60, 24), Direction = new float2(0, -1), Size = new int2(15, 6) };
            ZoneGuard.Apply(plan, new SiteContext { ZoneGrids = new List<ZoneGrid> { designGrid } });
            Assert.Equal(6, plan.ZoneCells.Count(c => c.State == ZoneCellState.Zoned));
            // A park path split the street at x = 70: the game rebuilt the block, its cells now 3 m further east.
            var live = new List<ZoneGrid> { new ZoneGrid { Position = new float2(31, 24), Direction = new float2(0, -1), Size = new int2(8, 6), Ours = false } };
            var quads = ZoneGuard.Refresh(plan, live);
            Assert.NotNull(quads);
            var zoned = plan.ZoneCells.Where(c => c.State == ZoneCellState.Zoned).ToList();
            Assert.NotEmpty(zoned);
            Assert.All(zoned, c => Assert.True(math.abs(math.round((Centre(c).x - 3f) / 8f) * 8f + 3f - Centre(c).x) < 0.05f, $"cell at {Centre(c)} not on the live grid"));
            foreach (var c in zoned)
            {
                var sq = Shrink(new[] { c.A, c.B, c.C, c.D }, 0.03f);
                Assert.DoesNotContain(plan.ZoneBlockers, b => ObstacleCut.Overlaps(sq, b));
            }
            Assert.All(quads, q => Assert.Equal("z", q.ZoneId));
            // After Apply the build's own paths block their cells for real: a live Blocked cell inside is closed.
            var blockedLive = live[0];
            blockedLive.Cells = Enumerable.Repeat((byte)(ZoneGrid.CellVisible | ZoneGrid.CellBlocked), 8 * 6).ToArray();
            var none = ZoneGuard.Refresh(plan, new List<ZoneGrid> { blockedLive });
            Assert.DoesNotContain(plan.ZoneCells, c => c.State == ZoneCellState.Zoned);
        }

        // ───────────── s54: build-own roads, live blocks off the prediction ─────────────

        private static float2[] ShrinkedPlot(float x0, float z0, float x1, float z1) => Rect(x0 + 1, z0 + 1, x1 - 1, z1 - 1);

        /// <summary>A market square: its own 8 m lane at z = 40 (x −10…130) with plots of whole cells on its south side.</summary>
        private static (ComposedPlan plan, SiteContext site) MarketSquare()
        {
            var plan = new ComposedPlan();
            plan.Footprints.Add(Rect(0, 0, 120, 80));
            var host = new AreaPlan { GeneratorId = "marketSquare" };
            host.Nets.Add(Net(new float2(-10, 40), new float2(130, 40), NetRole.Alley, "Alley"));
            // Plots of whole cells of the lane's predicted south block (cell centres at multiples of 8, rows z 28…36 + 20…28),
            // shrunk QuadShrink as ZoneLots makes them.
            foreach (var (x0, x1) in new[] { (4f, 36f), (52f, 76f), (84f, 116f) })
            {
                var q = ZoneLots.ZoningQuad(Rect(x0, 20, x1, 36));
                host.Zoning.Add(new PlannedZoning { A = new float3(q[0].x, 0, q[0].y), B = new float3(q[1].x, 0, q[1].y), C = new float3(q[2].x, 0, q[2].y), D = new float3(q[3].x, 0, q[3].y), ZoneId = "EU Commercial Low" });
            }
            plan.Layers.Add(host);
            var site = new SiteContext();
            site.NearbyNets.Add(new NearbyNet { A = new float3(-40, 0, -8), B = new float3(20, 0, -8), C = new float3(100, 0, -8), D = new float3(160, 0, -8), Width = 16f, Category = NetCategory.Road });
            site.ZoneGrids.Add(new ZoneGrid { Position = new float2(60, 24), Direction = new float2(0, -1), Size = new int2(15, 6) });
            return (plan, site);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.5f)]
        [InlineData(2.5f)]
        [InlineData(4f)]
        [InlineData(-3.3f)]
        [InlineData(7.2f)]
        public void Refresh_keeps_the_planned_cell_count_when_the_lanes_live_block_is_shifted(float shift)
        {
            var (plan, site) = MarketSquare();
            ZoneGuard.Apply(plan, site);
            int planned = plan.ZoneCells.Count(c => c.State == ZoneCellState.Zoned);
            Assert.True(planned >= 6, "the scenario must plan cells");
            var live = new List<ZoneGrid> { site.ZoneGrids[0] };
            // The game's blocks of our lane: same rows, but the columns sit shifted along the road and the ends are trimmed.
            foreach (var g in ZoneGuard.PredictedGrids(plan, site))
            {
                var s = g;
                s.Position += new float2(shift, 0);
                s.Predicted = false; s.Ours = true;
                live.Add(s);
            }
            // The street's own block is hidden under the lane's block there (not Visible) — the plots belong to the lane.
            var quads = ZoneGuard.Refresh(plan, live);
            Assert.NotNull(quads);
            int refreshed = plan.ZoneCells.Count(c => c.State == ZoneCellState.Zoned);
            Assert.True(planned == refreshed, $"planned {planned} != refreshed {refreshed}: {ZoneGuard.LastRefreshReport}");
            var squares = plan.ZoneCells.Where(c => c.State == ZoneCellState.Zoned).Select(c => new[] { c.A, c.B, c.C, c.D }).ToList();
            Assert.False(ZoneGuard.TouchesAny(plan.ZoneBlockers, squares));
        }

        [Fact]
        public void Refresh_returns_the_land_of_a_plot_that_cannot_zone_and_the_preview_shows_it_blocked()
        {
            var (plan, site) = MarketSquare();
            ZoneGuard.Apply(plan, site);
            // Live: the lane's blocks exist only west of x = 44 (trimmed at a junction); the other plots find nothing.
            var live = new List<ZoneGrid> { site.ZoneGrids[0] };
            foreach (var g in ZoneGuard.PredictedGrids(plan, site))
            {
                if (g.Position.y > 40) continue;
                live.Add(new ZoneGrid { Position = new float2(24, g.Position.y), Direction = g.Direction, Size = new int2(5, 6), Ours = true });
            }
            // the street's block is blocked in that area (under the lane block's rows)
            var street = live[0];
            street.Cells = Enumerable.Repeat((byte)(ZoneGrid.CellVisible | ZoneGrid.CellBlocked), 15 * 6).ToArray();
            live[0] = street;
            var quads = ZoneGuard.Refresh(plan, live);
            Assert.NotNull(quads);
            var squares = plan.ZoneCells.Where(c => c.State == ZoneCellState.Zoned).Select(c => new[] { c.A, c.B, c.C, c.D }).ToList();
            Assert.False(ZoneGuard.TouchesAny(plan.ZoneBlockers, squares));
            // No zoning quad is left over a plot without cells, and no cell is previewed as Zoned outside the live block.
            Assert.All(plan.ZoneCells.Where(c => c.State == ZoneCellState.Zoned), c => Assert.True(Centre(c).x < 44f));
            Assert.All(quads, q => Assert.True(Poly.Centroid(q.Corners).x < 44f, ZoneGuard.LastRefreshReport));
        }

        [Fact]
        public void Refresh_moves_a_plot_whose_live_cells_are_blocked_onto_the_nearest_open_cells()
        {
            var (plan, site) = MarketSquare();
            ZoneGuard.Apply(plan, site);
            var live = new List<ZoneGrid> { site.ZoneGrids[0] };
            foreach (var g in ZoneGuard.PredictedGrids(plan, site))
            {
                var s = g; s.Predicted = false; s.Ours = true;
                s.Cells = new byte[s.Size.x * s.Size.y];
                for (int x = 0; x < s.Size.x; x++)
                    for (int y = 0; y < s.Size.y; y++)
                    {
                        bool blocked = math.abs(ZoneGuard.CellCentre(s, x, y).x - 64f) < 14f;   // under the middle plot: blocked live
                        s.Cells[x + y * s.Size.x] = (byte)(ZoneGrid.CellVisible | (blocked ? ZoneGrid.CellBlocked : 0));
                    }
                live.Add(s);
            }
            var quads = ZoneGuard.Refresh(plan, live);
            Assert.NotNull(quads);
            var zoned = plan.ZoneCells.Where(c => c.State == ZoneCellState.Zoned).ToList();
            // the middle plot found open cells at least 2 x 2 elsewhere; nothing zoned on the blocked ones
            Assert.DoesNotContain(zoned, c => math.abs(Centre(c).x - 64f) < 10f && Centre(c).y > 20f && Centre(c).y < 36f);
            Assert.Contains("moved to the nearest open cells", ZoneGuard.LastRefreshReport);
            Assert.False(ZoneGuard.TouchesAny(plan.ZoneBlockers, zoned.Select(c => new[] { c.A, c.B, c.C, c.D }).ToList()));
        }

        [Fact]
        public void Blockers_never_touch_kept_cells_among_many_scattered_plots()
        {
            var foot = Rect(0, 0, 160, 96);
            var grid = new ZoneGrid { Position = new float2(80, 24), Direction = new float2(0, -1), Size = new int2(10, 6) };
            var grids = new List<ZoneGrid> { grid, new ZoneGrid { Position = new float2(0, 24), Direction = new float2(0, -1), Size = new int2(10, 6) } };
            grids[1] = new ZoneGrid { Position = new float2(120, 24), Direction = new float2(0, -1), Size = new int2(10, 6) };
            var quads = new List<float2[]>();
            for (int i = 0; i < 6; i++) quads.Add(Rect(2 + i * 26 + (i % 2) * 3.3f, 1, 2 + i * 26 + 15f + (i % 2) * 3.3f, 17));
            var ids = quads.Select(_ => "z").ToList();
            var r = ZoneGuard.Compute(new[] { foot }, quads, ids, new List<float2[]>(), grids);
            var zoned = r.Cells.Where(c => c.State == ZoneCellState.Zoned).Select(c => new[] { c.A, c.B, c.C, c.D }).ToList();
            Assert.NotEmpty(zoned);
            Assert.False(ZoneGuard.TouchesAny(r.Blockers, zoned));
        }

        // ───────────── composed commercial sites (design preview == what the game zones) ─────────────

        [Theory]
        [InlineData("shoppingPlaza", "")]
        [InlineData("stripMall", "")]
        [InlineData("outdoorMall", "loop")]
        public void Lane_plots_of_commercial_types_snap_to_the_lanes_blocks(string type, string preset)
        {
            var editor = new Citymaker.Core.Tool.DesignEditor(GeneratorRegistry.Get);
            var ring = TestKit.Rect(160, 160);
            var site = ZoningCutoutTests.Streets(ring);
            var r = editor.SingleLayerRecipe(GeneratorRegistry.Get(type), 5);
            if (preset != "") editor.ApplyPreset(r, Citymaker.Core.Tool.RecipePaths.Root, preset);
            uint n = 0; editor.AddDefaultFeatures(r, () => ++n);
            ShopTestEntrances.Place(r, ring, site, footpath: true, driveway: true);
            var plan = Composer.Compose(r, new AreaShape { Ring = ring, Source = AreaSource.Drawn }, site, CancellationToken.None);
            var lanes = plan.Layers.SelectMany(l => l.Nets).Where(x => ZoneBlockPredictor.Zones(x.Role)).ToList();
            Assert.NotEmpty(lanes);
            var zoned = plan.ZoneCells.Where(c => c.State == ZoneCellState.Zoned).ToList();
            Assert.NotEmpty(zoned);
            // Every zoned cell is a cell of a predicted lane block (no street block was captured in this site).
            var predicted = ZoneGuard.PredictedGrids(plan, site);
            foreach (var c in zoned)
                Assert.True(predicted.Any(g => Enumerable.Range(0, g.Size.x).Any(x => Enumerable.Range(0, g.Size.y).Any(y => math.distance(ZoneGuard.CellCentre(g, x, y), Centre(c)) < 0.05f))),
                    $"{type}: zoned cell at {Centre(c)} is on no lane block");
            // Cells never overlap, never lie on a lane.
            for (int i = 0; i < zoned.Count; i++)
                for (int j = i + 1; j < zoned.Count; j++)
                    Assert.False(ObstacleCut.Overlaps(Shrink(new[] { zoned[i].A, zoned[i].B, zoned[i].C, zoned[i].D }, 0.5f), Shrink(new[] { zoned[j].A, zoned[j].B, zoned[j].C, zoned[j].D }, 0.5f)));
            foreach (var c in zoned)
                foreach (var lane in lanes)
                    for (int k = 0; k <= 16; k++)
                        Assert.False(Poly.Contains(Shrink(new[] { c.A, c.B, c.C, c.D }, 0.5f), Composer.Bezier(lane.A, lane.B, lane.C, lane.D, k / 16f).xz), $"{type}: zoned cell on a lane");
            // Lanes stay whole: every lane piece's ends meet another piece, the street (attached) — no dead ends from clipping.
            foreach (var lane in lanes)
            {
                bool startOk = lane.StartAttach >= 0 || lanes.Any(o => o != lane && (math.distance(o.A.xz, lane.A.xz) < 0.01f || math.distance(o.D.xz, lane.A.xz) < 0.01f));
                bool endOk = lane.EndAttach >= 0 || lanes.Any(o => o != lane && (math.distance(o.A.xz, lane.D.xz) < 0.01f || math.distance(o.D.xz, lane.D.xz) < 0.01f));
                Assert.True(startOk || endOk, $"{type}: lane piece {lane.A.xz}–{lane.D.xz} is cut loose");
            }
        }

        private static float2[] Shrink(float2[] q, float by)
        {
            var c = Poly.Centroid(q);
            return q.Select(p => p + math.normalizesafe(c - p) * by).ToArray();
        }
    }
}
