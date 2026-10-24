// s22: a cutout drawn (and zoned) across Juniper Street. Playtest: a park in a curved block; the bottom-right cutout
// ran past the outline, over the sidewalk and the curved road, rotated unlike the road. Cause: a column of ANOTHER
// road's zone block (6 cells = 48 m deep) crossed the curved road and reached into the cutout; ZoneGuard took every
// row in front of the cutout that lay outside the park as "the strip between outline and kerb" — the road included.
// Fix: a column ends at its first cell on a road (the game blocks it and every cell behind), hidden cells (overlapping
// blocks on curves) and captured Blocked cells outside the build are never used, one piece of land is zoned by one
// block only, and the blocked-cell preview is clipped to the outline.

using System.Collections.Generic;
using System.Linq;
using Citymaker.Core;
using Citymaker.Core.Build;
using Citymaker.Core.Geometry;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class ZoneGuardCurveTests
    {
        private const float R = 100f;        // curved road centre line radius (centre at the origin)
        private const float RoadHalf = 8f;   // 16 m Two-Lane Road
        private const float InnerR = 62f;    // a straight road inside the curve
        private const float InnerHalf = 6f;

        private static float2 Polar(float r, float deg) => new float2(math.cos(math.radians(deg)), math.sin(math.radians(deg))) * r;

        /// <summary>The curved road (−30° … +30°) as two cubic Béziers of 30° each.</summary>
        private static List<NearbyNet> Roads()
        {
            var nets = new List<NearbyNet>();
            for (int i = 0; i < 2; i++)
            {
                float a0 = -30f + 30f * i, a1 = a0 + 30f;
                float k = 4f / 3f * math.tan(math.radians(30f) / 4f) * R;
                float2 p0 = Polar(R, a0), p3 = Polar(R, a1);
                float2 t0 = Polar(1f, a0 + 90f), t1 = Polar(1f, a1 + 90f);
                float2 p1 = p0 + t0 * k, p2 = p3 - t1 * k;
                nets.Add(new NearbyNet
                {
                    ExternalId = 100 + i, A = new float3(p0.x, 0, p0.y), B = new float3(p1.x, 0, p1.y), C = new float3(p2.x, 0, p2.y), D = new float3(p3.x, 0, p3.y),
                    Width = RoadHalf * 2f, Category = NetCategory.Road, HasSidewalk = true,
                });
            }
            // The straight road inside the curve, perpendicular to the 20° radial.
            float2 c = Polar(InnerR, 20f), d = Polar(1f, 110f);
            float2 a = c - d * 40f, b = c + d * 40f;
            nets.Add(new NearbyNet
            {
                ExternalId = 200, A = new float3(a.x, 0, a.y), B = new float3(math.lerp(a, b, 1f / 3f).x, 0, math.lerp(a, b, 1f / 3f).y),
                C = new float3(math.lerp(a, b, 2f / 3f).x, 0, math.lerp(a, b, 2f / 3f).y), D = new float3(b.x, 0, b.y), Width = InnerHalf * 2f, Category = NetCategory.Road,
            });
            return nets;
        }

        /// <summary>
        /// Zone blocks as the game makes them: the curved road's outer side gets one straight block per 10° piece
        /// (rotated with the piece, front edge on the road edge, 6 rows deep); the inner straight road's outer block
        /// reaches 48 m outwards — across the curved road into the park.
        /// </summary>
        private static List<ZoneGrid> Grids()
        {
            // The crossing block comes first (capture order is the search tree's: any order is possible).
            float2 o = Polar(1f, 20f);
            var g = new List<ZoneGrid> { new ZoneGrid { Position = Polar(InnerR + InnerHalf, 20f) + o * 24f, Direction = -o, Size = new int2(4, 6) } };
            for (float a = -30f; a < 30f - 0.1f; a += 10f)
            {
                float mid = a + 5f;
                float chord = 2f * (R + RoadHalf) * math.sin(math.radians(5f));
                int cols = (int)(chord / 8f);
                float2 outward = Polar(1f, mid);
                float2 front = Polar(R + RoadHalf, mid);
                g.Add(new ZoneGrid { Position = front + outward * 24f, Direction = -outward, Size = new int2(cols, 6) });
            }
            return g;
        }

        /// <summary>The park: the curved block on the outer side, its outline 1 m off the kerb.</summary>
        private static float2[] Park()
        {
            var ring = new List<float2>();
            for (float a = -25f; a <= 25.01f; a += 2.5f) ring.Add(Polar(R + RoadHalf + 1f, a));
            for (float a = 25f; a >= -25.01f; a -= 2.5f) ring.Add(Polar(R + 80f, a));
            return Poly.EnsureCcw(ring.ToArray());
        }

        /// <summary>The cutout at 14°…26° (EdgeZoning puts its road side 1 m past the outline).</summary>
        private static float2[] Cutout() => Poly.EnsureCcw(new[] { Polar(R + RoadHalf, 14f), Polar(R + RoadHalf, 24f), Polar(125f, 24f), Polar(125f, 14f) });

        private static float DistanceToRoad(float2 p)
        {
            float best = float.MaxValue;
            foreach (var n in Roads())
            {
                float half = n.Width * 0.5f;
                for (int i = 0; i < 64; i++)
                {
                    float t0 = i / 64f, t1 = (i + 1) / 64f;
                    float2 a = Bez(n, t0), b = Bez(n, t1);
                    var ab = b - a;
                    float t = math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.lengthsq(ab)));
                    best = math.min(best, math.distance(p, a + ab * t) - half);
                }
            }
            return best;   // < 0: on the road
        }

        private static float2 Bez(NearbyNet n, float t)
        {
            float u = 1f - t;
            return (u * u * u * n.A + 3f * u * u * t * n.B + 3f * u * t * t * n.C + t * t * t * n.D).xz;
        }

        private static ZoneGuard.Result Run(List<ZoneGrid> grids, bool nets = true) =>
            ZoneGuard.Compute(new[] { Park() }, new[] { Cutout() }, new[] { "EU Commercial Low" }, new List<float2[]>(), grids, nets ? Roads() : null);

        [Fact]
        public void Fixture_reproduces_the_playtest_without_the_road_check()
        {
            // Without the captured roads the inner road's block zones its column across the curved road (the bug; since
            // s26 the curved road's own blocks win the cutout first, so the crossing block is run on its own here).
            var r = Run(new List<ZoneGrid> { Grids()[0] }, nets: false);
            Assert.Contains(r.Cells, c => c.State == ZoneCellState.Zoned && DistanceToRoad(Poly.Centroid(new[] { c.A, c.B, c.C, c.D })) < 0f);
        }

        [Fact]
        public void Cutout_cells_never_lie_on_the_curved_road()
        {
            var r = Run(Grids());
            var zoned = r.Cells.Where(c => c.State == ZoneCellState.Zoned).ToList();
            Assert.NotEmpty(zoned);
            var park = Park();
            foreach (var c in zoned)
            {
                var sq = new[] { c.A, c.B, c.C, c.D };
                var centre = Poly.Centroid(sq);
                // Inside the outline or in the strip between outline and kerb — never on the road.
                Assert.True(DistanceToRoad(centre) >= 3.5f, $"zoned cell on the road at {centre}");
                foreach (var p in sq) Assert.True(DistanceToRoad(p) > -0.2f, $"zoned cell corner on the road at {p}");
                Assert.True(Poly.Contains(park, centre) || Poly.DistanceToBoundary(park, centre) < 8f);
            }
            // The zoning the build paints: every Marquee rect avoids the road too.
            foreach (var z in r.Zoning)
                foreach (var p in z.Corners) Assert.True(DistanceToRoad(p) > -0.2f, $"zoning rect over the road at {p}");
            // Every listed cell (zoned, kept or blocked) is off the road.
            foreach (var c in r.Cells)
                Assert.True(DistanceToRoad(Poly.Centroid(new[] { c.A, c.B, c.C, c.D })) >= 3.5f);
        }

        [Fact]
        public void Cutout_cells_follow_the_curved_roads_own_rotated_blocks()
        {
            var r = Run(Grids());
            var zoned = r.Cells.Where(c => c.State == ZoneCellState.Zoned).ToList();
            // Each zoned cell is aligned with the curved road's local block: its front edge faces the road centre.
            foreach (var c in zoned)
            {
                var sq = new[] { c.A, c.B, c.C, c.D };
                var centre = Poly.Centroid(sq);
                float2 radial = math.normalize(centre);
                float2 edge = math.normalize(c.B - c.A);
                float align = math.abs(math.dot(edge, radial));
                // Edges are radial or tangential within the 5° of a 10° piece (never the inner road's 20° grid at 14°).
                Assert.True(align > math.cos(math.radians(7f)) || align < math.sin(math.radians(7f)), $"cell at {centre} rotated unlike the road");
            }
        }

        [Fact]
        public void Overlapping_blocks_zone_a_piece_of_land_once()
        {
            var grids = Grids();
            grids.Add(grids[6]);   // the 20°–30° piece twice (inside of a curve, junction corners)
            var r = Run(grids);
            var zoned = r.Cells.Where(c => c.State == ZoneCellState.Zoned).Select(c => Poly.Centroid(new[] { c.A, c.B, c.C, c.D })).ToList();
            for (int i = 0; i < zoned.Count; i++)
                for (int j = i + 1; j < zoned.Count; j++)
                    Assert.True(math.distance(zoned[i], zoned[j]) > 4f, $"cell zoned twice at {zoned[i]}");
        }

        [Fact]
        public void Hidden_and_road_blocked_cells_from_the_game_are_never_used()
        {
            var grids = Grids();
            // The game says: the 10°–20° piece's cells are hidden (another block won the overlap) …
            var hidden = grids[5];
            hidden.Cells = new byte[hidden.Size.x * hidden.Size.y];
            grids[5] = hidden;
            var r = Run(grids);
            foreach (var c in r.Cells)
            {
                var centre = Poly.Centroid(new[] { c.A, c.B, c.C, c.D });
                for (int x = 0; x < hidden.Size.x; x++)
                    for (int y = 0; y < hidden.Size.y; y++)
                        Assert.False(math.distance(centre, ZoneGuard.CellCentre(hidden, x, y)) < 0.5f, $"hidden cell listed at {centre}");
            }
            var park = new[] { Park() };
            // A cell the game blocks OUTSIDE the build (a sidewalk, another lot) is closed; visible it is open …
            var inner = Grids()[0];
            inner.Cells = Enumerable.Repeat((byte)(ZoneGrid.CellVisible | ZoneGrid.CellBlocked), inner.Size.x * inner.Size.y).ToArray();
            Assert.False(ZoneGuard.CellOpen(inner, 1, 0, park, null));
            inner.Cells = Enumerable.Repeat(ZoneGrid.CellVisible, inner.Size.x * inner.Size.y).ToArray();
            Assert.True(ZoneGuard.CellOpen(inner, 1, 0, park, null));
            // … a cell on a captured road never is, whatever the game says …
            Assert.False(ZoneGuard.CellOpen(inner, 1, 3, park, Roads()));
            // … and a Blocked cell INSIDE the build stays usable (the build being edited blocks its own land).
            var own = Grids()[6];
            own.Cells = Enumerable.Repeat((byte)(ZoneGrid.CellVisible | ZoneGrid.CellBlocked), own.Size.x * own.Size.y).ToArray();
            Assert.True(Poly.Contains(park[0], ZoneGuard.CellCentre(own, 1, 1)));
            Assert.True(ZoneGuard.CellOpen(own, 1, 1, park, Roads()));
        }

        [Fact]
        public void Blocked_preview_stays_inside_the_outline()
        {
            var r = Run(Grids());
            var blocked = r.Cells.Where(c => c.State == ZoneCellState.Blocked).ToList();
            Assert.NotEmpty(blocked);
            var grown = Poly.Offset(Park(), -0.05f)[0];
            foreach (var c in blocked)
            {
                Assert.NotNull(c.Clipped);
                foreach (var p in c.Clipped) Assert.True(Poly.Contains(grown, p), $"blocked preview outside the outline at {p}");
            }
            // Cells straddling the outline are cut, not dropped.
            Assert.Contains(blocked, c => Poly.Area(c.Clipped) < ZoneGuard.Cell * ZoneGuard.Cell - 1f);
        }

        [Fact]
        public void Apply_keeps_the_plans_zoning_off_the_road()
        {
            var plan = new ComposedPlan();
            plan.Footprints.Add(Park());
            var layer = new AreaPlan { GeneratorId = "park" };
            var q = Cutout();
            layer.Zoning.Add(new PlannedZoning { A = new float3(q[0].x, 0, q[0].y), B = new float3(q[1].x, 0, q[1].y), C = new float3(q[2].x, 0, q[2].y), D = new float3(q[3].x, 0, q[3].y), ZoneId = "EU Commercial Low" });
            plan.Layers.Add(layer);
            var site = new SiteContext { NearbyNets = Roads(), ZoneGrids = Grids() };
            ZoneGuard.Apply(plan, site);
            Assert.NotEmpty(layer.Zoning);
            foreach (var z in layer.Zoning)
                foreach (var p in new[] { z.A.xz, z.B.xz, z.C.xz, z.D.xz })
                    Assert.True(DistanceToRoad(p) > -0.2f, $"zoning over the road at {p}");
        }
    }

    /// <summary>
    /// s22 (lead, in-game after building the park): no zone cell at all along the park — not even in its two cutouts.
    /// A park on a block between four streets with two cutouts: the blockers must leave every cutout cell free, every
    /// cutout cell must be zoned by the build, and a cell the game hides (another block won the overlap, e.g. at a
    /// corner or on a curve) must never be the one zoned while the visible cell on the same land gets blocked.
    /// </summary>
    public class ZoneGuardTwoCutoutTests
    {
        private static float2[] Rect(float x0, float z0, float x1, float z1) =>
            new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) };

        // Block 0…160 × 0…120 (outline 1 m off the kerbs); streets 16 m wide along all four sides.
        private static float2[] Park() => Rect(1, 1, 159, 119);

        private static List<NearbyNet> Streets()
        {
            NearbyNet S(float2 a, float2 b) => new NearbyNet
            {
                A = new float3(a.x, 0, a.y), B = new float3(math.lerp(a, b, 1f / 3f).x, 0, math.lerp(a, b, 1f / 3f).y),
                C = new float3(math.lerp(a, b, 2f / 3f).x, 0, math.lerp(a, b, 2f / 3f).y), D = new float3(b.x, 0, b.y), Width = 16f, Category = NetCategory.Road,
            };
            return new List<NearbyNet> { S(new float2(-40, -8), new float2(200, -8)), S(new float2(-40, 128), new float2(200, 128)),
                S(new float2(-8, -40), new float2(-8, 160)), S(new float2(168, -40), new float2(168, 160)) };
        }

        /// <summary>The four streets' blocks on the park side (front edge on the kerb, 6 rows), corners overlapping.</summary>
        private static List<ZoneGrid> Blocks() => new List<ZoneGrid>
        {
            new ZoneGrid { Position = new float2(80, 24), Direction = new float2(0, -1), Size = new int2(20, 6) },    // south street
            new ZoneGrid { Position = new float2(80, 96), Direction = new float2(0, 1), Size = new int2(20, 6) },     // north street
            new ZoneGrid { Position = new float2(24, 60), Direction = new float2(-1, 0), Size = new int2(15, 6) },    // west street
            new ZoneGrid { Position = new float2(136, 60), Direction = new float2(1, 0), Size = new int2(15, 6) },    // east street
        };

        // Cutouts along the south street (west part) and the north street (east part), 3 cells × 2 rows.
        private static readonly float2[][] Cutouts = { Rect(16, -1, 40, 16), Rect(112, 104, 136, 121) };

        private static ZoneGuard.Result Run(List<ZoneGrid> blocks) =>
            ZoneGuard.Compute(new[] { Park() }, Cutouts, new[] { "EU Commercial Low", "EU Commercial Low" }, new List<float2[]>(), blocks, Streets());

        [Fact]
        public void Both_cutouts_are_zoned_and_no_blocker_touches_them()
        {
            var r = Run(Blocks());
            Assert.True(r.Snapped[0] && r.Snapped[1]);
            var zoned = r.Cells.Where(c => c.State == ZoneCellState.Zoned).ToList();
            Assert.Equal(12, zoned.Count);   // 2 cutouts × 3 columns × 2 rows
            foreach (var cut in Cutouts)
                Assert.Equal(6, zoned.Count(c => Poly.Contains(cut, Poly.Centroid(new[] { c.A, c.B, c.C, c.D }))));
            // Zoning rects (the Marquee quads the build paints) cover every cutout cell centre.
            foreach (var c in zoned)
            {
                var centre = Poly.Centroid(new[] { c.A, c.B, c.C, c.D });
                Assert.Contains(r.Zoning, z => Poly.Contains(z.Corners, centre));
                var shrunk = new[] { c.A, c.B, c.C, c.D }.Select(p => p + math.normalizesafe(centre - p) * 0.03f).ToArray();
                Assert.False(ZoneGuard.Blocks(r.BlockerRegions, shrunk), $"blocker region on cutout cell {centre}");
                Assert.DoesNotContain(r.Blockers, b => ObstacleCut.Overlaps(shrunk, b));
            }
            // Everything else of the park is blocked (front cells of the other columns).
            Assert.Contains(r.Cells, c => c.State == ZoneCellState.Blocked);
            Assert.NotEmpty(r.Blockers);
            Assert.All(r.Blockers, b => Assert.True(SurfaceAcceptance.IsAccepted(b)));
        }

        [Fact]
        public void Hidden_cells_are_not_zoned_the_visible_block_on_the_same_land_is()
        {
            // The game resolved the south street's overlap against a duplicate block (a split edge): the first copy's
            // cells are hidden, the second's visible. Before s22 ZoneGuard zoned the hidden copy and blocked the visible
            // one — no cutout cell survived the build.
            var blocks = Blocks();
            var hidden = blocks[0];
            hidden.Cells = new byte[hidden.Size.x * hidden.Size.y];          // nothing visible
            var visible = blocks[0];
            visible.Cells = Enumerable.Repeat(ZoneGrid.CellVisible, visible.Size.x * visible.Size.y).ToArray();
            blocks[0] = hidden;
            blocks.Add(visible);
            var r = Run(blocks);
            var zoned = r.Cells.Where(c => c.State == ZoneCellState.Zoned).ToList();
            Assert.Equal(6, zoned.Count(c => Poly.Contains(Cutouts[0], Poly.Centroid(new[] { c.A, c.B, c.C, c.D }))));
            foreach (var c in zoned)
            {
                var centre = Poly.Centroid(new[] { c.A, c.B, c.C, c.D });
                var shrunk = new[] { c.A, c.B, c.C, c.D }.Select(p => p + math.normalizesafe(centre - p) * 0.03f).ToArray();
                Assert.False(ZoneGuard.Blocks(r.BlockerRegions, shrunk));
            }
        }

        [Fact]
        public void Park_companion_keeps_out_of_the_cutouts()
        {
            // The companion building looked for the emptiest road-side spot — an empty cutout — and took its cells.
            var plan = new ComposedPlan();
            var l = new AreaPlan();
            foreach (var q in Cutouts)
                l.Zoning.Add(new PlannedZoning { A = new float3(q[0].x, 0, q[0].y), B = new float3(q[1].x, 0, q[1].y), C = new float3(q[2].x, 0, q[2].y), D = new float3(q[3].x, 0, q[3].y), ZoneId = "EU Commercial Low" });
            // A few trees everywhere else, so the cutouts would be the emptiest land.
            for (float x = 10; x < 160; x += 20)
                for (float z = 10; z < 120; z += 20)
                    if (!Cutouts.Any(c => Poly.Contains(c, new float2(x, z)))) l.Objects.Add(new PlannedObject { Role = ObjectRole.Tree, Position = new float3(x, 0, z) });
            plan.Layers.Add(l);
            var objs = new List<float2>();
            var paths = new List<(float2, float2, float)>();
            ParkCompanionPlanner.Obstacles(plan, objs, paths);
            var site = ParkCompanionPlanner.Find(Park(), Streets(), objs, paths);
            if (!site.Found) return;   // no spot is fine; a spot in a cutout is not
            float h = ParkCompanionPlanner.LotSize * 0.5f;
            var lot = Rect(site.Position.x - h, site.Position.y - h, site.Position.x + h, site.Position.y + h);
            foreach (var c in Cutouts) Assert.False(ObstacleCut.Overlaps(lot, c), $"companion lot at {site.Position} in a cutout");
        }
    }
}
