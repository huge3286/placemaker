// s36: cutouts on a curved 4-lane road came out as one column of 2 cells, or none, and the game blocked even those
// ("row 0 with row 1 blocked … behind a blocked cell"). The game rule (Game.Zones.CellOverlapJobs, Blocked pass,
// decompiled 1.6.2f1): every block evens out its column depths — CalculateLeftDepth / CalculateRightDepth from both
// ends, ReduceDepth blocks every cell from the new depth on. A column deeper than both neighbours is cut to the
// deeper neighbour unless a neighbour of its own depth stands beside it: a lone 1-column cutout between blocked
// columns is blocked entirely. ZoneGuard now ports the rule, widens lone columns to two and drops what the game
// would block anyway (preview == game).

using System.Collections.Generic;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class ZoneGuardDepthRuleTests
    {
        private static ZoneGrid Row(int columns) => new ZoneGrid { Position = new float2(columns * 4f, 24), Direction = new float2(0, -1), Size = new int2(columns, 6) };

        private static int[] Reduce(params int[] depths)
        {
            var g = Row(depths.Length);
            var d = new List<int[]> { (int[])depths.Clone() };
            ZoneGuard.ReduceDepths(new[] { g }, d);
            return d[0];
        }

        [Fact]
        public void Lone_column_between_blocked_columns_is_blocked()
        {
            Assert.Equal(new[] { 0, 0, 0, 0, 0 }, Reduce(0, 0, 2, 0, 0));
            Assert.Equal(new[] { 0, 0, 0, 0, 0 }, Reduce(0, 0, 6, 0, 0));
        }

        [Fact]
        public void Two_columns_of_one_depth_survive()
        {
            Assert.Equal(new[] { 0, 2, 2, 0, 0 }, Reduce(0, 2, 2, 0, 0));
            Assert.Equal(new[] { 0, 3, 3, 3, 0 }, Reduce(0, 3, 3, 3, 0));
        }

        [Fact]
        public void A_single_deeper_column_is_cut_to_its_neighbours()
        {
            Assert.Equal(new[] { 0, 2, 2, 2, 0 }, Reduce(0, 2, 4, 2, 0));
            Assert.Equal(new[] { 6, 6, 6, 6, 6 }, Reduce(6, 6, 6, 6, 6));
        }

        // ───────────── the playtest: a curved 4-lane road, a 2 × 4-cell plot ─────────────

        private const float R = 160f, Half = 12f;   // Four-Lane Road (24 m) around a centre

        private static float2 Polar(float r, float deg) => new float2(math.cos(math.radians(deg)), math.sin(math.radians(deg))) * r;

        /// <summary>The curved road's blocks (predicted like the game makes them) and the road itself.</summary>
        private static (List<ZoneGrid> grids, List<NearbyNet> nets) Road()
        {
            var nets = new List<NearbyNet>();
            var grids = new List<ZoneGrid>();
            for (int i = 0; i < 2; i++)
            {
                float a0 = -20f + 20f * i, a1 = a0 + 20f;
                float k = 4f / 3f * math.tan(math.radians(20f) / 4f) * R;
                float2 p0 = Polar(R, a0), p3 = Polar(R, a1);
                float2 p1 = p0 + Polar(1f, a0 + 90f) * k, p2 = p3 - Polar(1f, a1 + 90f) * k;
                var n = new NearbyNet { A = new float3(p0.x, 0, p0.y), B = new float3(p1.x, 0, p1.y), C = new float3(p2.x, 0, p2.y), D = new float3(p3.x, 0, p3.y), Width = 2 * Half, Category = NetCategory.Road };
                nets.Add(n);
                grids.AddRange(ZoneBlockPredictor.Blocks(n.A, n.B, n.C, n.D, 2 * Half));
            }
            return (grids, nets);
        }

        [Fact]
        public void Curved_four_lane_road_two_by_four_plot_gets_all_its_cells_and_the_game_keeps_them()
        {
            var (grids, nets) = Road();
            // The park on the outer side, its outline 1 m off the kerb; the plot 16 m along the road, 32 m deep, drawn
            // along the outline (a chord): at an angle to the cells of the curved road's blocks.
            var foot = new List<float2>();
            for (float a = -18f; a <= 18.01f; a += 2f) foot.Add(Polar(R + Half + 1f, a));
            for (float a = 18f; a >= -18.01f; a -= 2f) foot.Add(Polar(R + 90f, a));
            var footprint = Poly.EnsureCcw(foot.ToArray());
            float2 f0 = Polar(R + Half, 3f), f1 = Polar(R + Half, 3f + math.degrees(16f / (R + Half)));
            float2 dir = math.normalize(f1 - f0), inward = new float2(-dir.y, dir.x);
            if (math.dot(inward, Polar(1f, 4f)) < 0f) inward = -inward;
            var plot = Poly.EnsureCcw(new[] { f0 - inward, f1 - inward, f1 + inward * 32f, f0 + inward * 32f });

            var r = ZoneGuard.Compute(new[] { footprint }, new[] { plot }, new[] { "EU Commercial Low" }, new List<float2[]>(), grids, nets);
            var zoned = r.Cells.Where(c => c.State == ZoneCellState.Zoned).ToList();
            Assert.True(zoned.Count >= 8, $"only {zoned.Count} cells for a 2 × 4-cell plot");

            // Under the game's rules (blockers block their cells; CleanBlockedCells; the depth rule) every zoned cell stays.
            var depth = new List<int[]>();
            foreach (var g in grids)
            {
                var d = new int[g.Size.x];
                for (int x = 0; x < g.Size.x; x++)
                {
                    int y = 0;
                    while (y < g.Size.y && !ZoneGuard.Blocks(r.BlockerRegions, ZoneGuard.CellSquare(g, x, y, -0.02f))
                        && !OnRoad(ZoneGuard.CellCentre(g, x, y), nets)) y++;
                    if (y == 1) y = 0;
                    d[x] = y;
                }
                depth.Add(d);
            }
            ZoneGuard.ReduceDepths(grids, depth);
            foreach (var c in zoned)
            {
                var centre = 0.25f * (c.A + c.B + c.C + c.D);
                bool alive = false;
                for (int gi = 0; gi < grids.Count && !alive; gi++)
                    for (int x = 0; x < grids[gi].Size.x && !alive; x++)
                        for (int y = 0; y < depth[gi][x]; y++)
                            if (math.distance(ZoneGuard.CellCentre(grids[gi], x, y), centre) < 0.05f) { alive = true; break; }
                Assert.True(alive, $"the game would block the zoned cell at {centre}");
            }
        }

        [Fact]
        public void A_lone_column_is_widened_or_dropped_never_left_to_the_game_to_block()
        {
            // A straight street, a plot exactly one cell wide: the guard widens it to two columns (the neighbour lies
            // partly on the plot) — never plans a lone column the game blocks.
            var grid = new ZoneGrid { Position = new float2(40, 24), Direction = new float2(0, -1), Size = new int2(10, 6) };
            var foot = new[] { new float2(0, 0), new float2(80, 0), new float2(80, 80), new float2(0, 80) };
            var plot = new[] { new float2(30, -1), new float2(40, -1), new float2(40, 16), new float2(30, 16) };   // centres x 36 in; x 28 has 25 %
            var r = ZoneGuard.Compute(new[] { foot }, new[] { plot }, new[] { "z" }, new List<float2[]>(), new[] { grid });
            var columns = r.Cells.Where(c => c.State == ZoneCellState.Zoned).Select(c => math.round((0.25f * (c.A + c.B + c.C + c.D)).x)).Distinct().ToList();
            Assert.Equal(2, columns.Count);
            // A plot a sliver beside its one column: no partner column → nothing zoned (the game would block it).
            var thin = new[] { new float2(32.5f, -1), new float2(39.5f, -1), new float2(39.5f, 16), new float2(32.5f, 16) };
            var r2 = ZoneGuard.Compute(new[] { foot }, new[] { thin }, new[] { "z" }, new List<float2[]>(), new[] { grid });
            Assert.DoesNotContain(r2.Cells, c => c.State == ZoneCellState.Zoned);
        }

        private static bool OnRoad(float2 c, List<NearbyNet> nets)
        {
            foreach (var n in nets)
                for (int i = 0; i <= 32; i++)
                {
                    var p = Composer.Bezier(n.A, n.B, n.C, n.D, i / 32f).xz;
                    if (math.distance(p, c) < n.Width * 0.5f + ZoneGuard.RoadCellReach) return true;
                }
            return false;
        }
    }
}
