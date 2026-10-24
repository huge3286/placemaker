// s43: Paint bucket walls — mixed boundary kinds in the face graph, side-connected paths, dangling paths,
// curb outline from each net's half-width, area walls keep the click's side; Road nodes closing.

using System.Collections.Generic;
using System.Linq;
using Citymaker.Core.Geometry;
using Citymaker.Core.Tool;
using Unity.Mathematics;
using Xunit;

namespace Citymaker.Tests
{
    public class GeometryFillWallsTests
    {
        private static readonly Dictionary<long, float2> Nodes = new Dictionary<long, float2>
        {
            [1] = new float2(0, 0), [2] = new float2(100, 0), [3] = new float2(200, 0),
            [4] = new float2(200, 100), [5] = new float2(100, 100), [6] = new float2(0, 100),
        };

        private static FillCurve C(long id, long a, long b, FillWall kind, float hw = 6f, Dictionary<long, float2> nodes = null)
        {
            nodes ??= Nodes;
            return new FillCurve { Id = id, Host = id, A = a, B = b, Kind = kind, HalfWidth = hw, Geom = new[] { nodes[a], nodes[b] } };
        }

        /// <summary>200×100 block of roads with a path across the middle (x = 100) between two road nodes.</summary>
        private static List<FillCurve> Block(FillWall pathKind = FillWall.Paths) => new List<FillCurve>
        {
            C(11, 1, 2, FillWall.Roads), C(12, 2, 3, FillWall.Roads), C(13, 3, 4, FillWall.Roads),
            C(14, 4, 5, FillWall.Roads), C(15, 5, 6, FillWall.Roads), C(16, 6, 1, FillWall.Roads),
            C(20, 2, 5, pathKind, 2f),
        };

        [Fact]
        public void Paths_SplitTheBlock_AndTurningThemOffMergesTheFaces()
        {
            var on = FillGraph.Build(Block(), Nodes, null, FillWalls.Default);
            var f = on.Pick(new float2(50, 50));
            Assert.True(f.Ok, f.Status.ToString());
            Assert.Equal(10000f, f.Area, 1);
            Assert.Contains(20L, f.EdgeIds);

            var off = FillGraph.Build(Block(), Nodes, null, FillWalls.Default & ~FillWall.Paths);
            var g = off.Pick(new float2(50, 50));
            Assert.True(g.Ok);
            Assert.Equal(20000f, g.Area, 1);
            Assert.DoesNotContain(20L, g.EdgeIds);
        }

        [Fact]
        public void EveryWallKind_ClosesFaces_AndADisabledKindOpensThem()
        {
            // Each side of the left block is a different kind of net.
            var curves = new List<FillCurve>
            {
                C(11, 1, 2, FillWall.Roads), C(20, 2, 5, FillWall.Bikes, 1.5f), C(15, 5, 6, FillWall.Tracks, 3f), C(16, 6, 1, FillWall.Paths, 2f),
            };
            var all = FillGraph.Build(curves, Nodes, null, FillWalls.All);
            var f = all.Pick(new float2(50, 50));
            Assert.True(f.Ok, f.Status.ToString());
            Assert.Equal(4, f.EdgeIds.Count);
            foreach (var kind in new[] { FillWall.Roads, FillWall.Bikes, FillWall.Tracks, FillWall.Paths })
            {
                var g = FillGraph.Build(curves, Nodes, null, FillWalls.All & ~kind);
                Assert.False(g.Pick(new float2(50, 50)).Ok, kind + " off must open the block");
            }
        }

        [Fact]
        public void SideConnectedPath_SplitsTheRoadItEndsOn()
        {
            // Roads 1→3 and 4→6 are single edges; the path's ends (7, 8) sit on the sidewalks without splitting them.
            var nodes = new Dictionary<long, float2>(Nodes) { [7] = new float2(100, 5), [8] = new float2(100, 95) };
            var curves = new List<FillCurve>
            {
                new FillCurve { Id = 31, Host = 31, A = 1, B = 3, Kind = FillWall.Roads, HalfWidth = 6, Geom = new[] { nodes[1], nodes[2], nodes[3] } },
                C(13, 3, 4, FillWall.Roads, nodes: nodes),
                new FillCurve { Id = 34, Host = 34, A = 4, B = 6, Kind = FillWall.Roads, HalfWidth = 6, Geom = new[] { nodes[4], nodes[5], nodes[6] } },
                C(16, 6, 1, FillWall.Roads, nodes: nodes),
                C(20, 7, 8, FillWall.Paths, 2f, nodes),
            };
            var links = new[] { new FillSideLink(31, 7), new FillSideLink(34, 8) };

            var without = FillGraph.Build(curves, nodes, null, FillWalls.Default);
            var whole = without.Pick(new float2(50, 50));
            Assert.True(whole.Ok);
            Assert.Equal(20000f, whole.Area, 0);
            Assert.Contains(20L, whole.InteriorEdgeIds);          // the old behaviour: the path dangles and is ignored

            var with = FillGraph.Build(curves, nodes, links, FillWalls.Default);
            var left = with.Pick(new float2(50, 50));
            Assert.True(left.Ok, left.Status.ToString());
            Assert.InRange(left.Area, 9400f, 10100f);
            Assert.Contains(20L, left.EdgeIds);
            Assert.Contains(left.EdgeIds, id => id == FillGraph.SubEdgeId(31, 0));
            Assert.True(with.TryEdge(FillGraph.SubEdgeId(31, 0), out var sub) && sub.IsSub && sub.Host == 31);
            var right = with.Pick(new float2(150, 50));
            Assert.True(right.Ok);
            Assert.True(Poly.Contains(right.Ring, new float2(150, 50)) && !Poly.Contains(right.Ring, new float2(50, 50)));

            // With paths off the links are ignored (nothing enabled ends there) and the road is not split.
            var off = FillGraph.Build(curves, nodes, links, FillWalls.Default & ~FillWall.Paths);
            Assert.Equal(20000f, off.Pick(new float2(50, 50)).Area, 0);
            Assert.True(off.TryEdge(31, out _));
        }

        [Fact]
        public void OpenEndedPaths_NeverMakeBogusFaces()
        {
            var nodes = new Dictionary<long, float2>(Nodes)
            {
                [40] = new float2(40, 30), [41] = new float2(60, 30), [42] = new float2(60, 60), [43] = new float2(50, 0.0001f), [44] = new float2(45, 40),
            };
            var curves = new List<FillCurve>
            {
                C(11, 1, 2, FillWall.Roads, nodes: nodes), C(20, 2, 5, FillWall.Roads, nodes: nodes), C(15, 5, 6, FillWall.Roads, nodes: nodes), C(16, 6, 1, FillWall.Roads, nodes: nodes),
                // A loose path tree inside the block (touches nothing) …
                C(50, 40, 41, FillWall.Paths, 2f, nodes), C(51, 41, 42, FillWall.Paths, 2f, nodes),
                // … and a dead end from a separate stub.
                C(52, 43, 44, FillWall.Paths, 2f, nodes),
            };
            var g = FillGraph.Build(curves, nodes, null, FillWalls.Default);
            foreach (var p in new[] { new float2(50, 31), new float2(61, 45), new float2(46, 20), new float2(20, 80) })
            {
                var f = g.Pick(p);
                Assert.True(f.Ok, p + " " + f.Status);
                Assert.Equal(10000f, Poly.Area(f.Ring), 1);
                Assert.Empty(f.Holes);
            }
            // The outline keeps off the loose paths too (one simple ring, the paths cut in with their own width).
            var pick = g.Pick(new float2(20, 80));
            var outline = g.CurbOutline(g.Ring(pick.EdgeIds, pick.NodeIds), new float2(20, 80));
            Assert.NotNull(outline);
            Assert.True(Poly.IsSimple(outline));
            Assert.False(Poly.Contains(outline, new float2(50, 30)));
        }

        [Fact]
        public void CurbOutline_UsesEachNetsHalfWidth()
        {
            var g = FillGraph.Build(Block(), Nodes, null, FillWalls.Default);
            var f = g.Pick(new float2(50, 50));
            var outline = g.CurbOutline(g.Ring(f.EdgeIds, f.NodeIds), new float2(50, 50));
            Assert.NotNull(outline);
            // Roads ±6 m on three sides, the path ±2 m on the fourth: x 6..98, y 6..94.
            Poly.Bounds(outline, out var min, out var max);
            Assert.Equal(6f, min.x, 1); Assert.Equal(98f, max.x, 1);
            Assert.Equal(6f, min.y, 1); Assert.Equal(94f, max.y, 1);
            Assert.Equal(92f * 88f, Poly.Area(outline), 0);
            Assert.True(SurfaceAcceptance.IsAccepted(outline));
        }

        [Fact]
        public void AreaWalls_KeepTheClickedSide()
        {
            var ring = new[] { new float2(0, 0), new float2(100, 0), new float2(100, 100), new float2(0, 100) };
            // A creek across the block (from the water predicate) and a lot in one corner.
            var water = FillWalls.WaterRects(ring, p => p.y > 40 && p.y < 52);
            Assert.NotEmpty(water);
            var lot = new[] { new float2(70, 70), new float2(110, 70), new float2(110, 110), new float2(70, 110) };
            var walls = new List<float2[]>(water) { lot };

            var below = FillWalls.ClipToSeed(ring, walls, new float2(50, 10), out int touched);
            Assert.True(touched > 0);
            Assert.NotNull(below);
            Assert.True(Poly.Contains(below, new float2(50, 10)));
            Assert.False(Poly.Contains(below, new float2(50, 80)));
            Assert.InRange(Poly.Area(below), 3500f, 4500f);

            var above = FillWalls.ClipToSeed(ring, walls, new float2(20, 80), out _);
            Assert.True(Poly.Contains(above, new float2(20, 80)));
            Assert.False(Poly.Contains(above, new float2(80, 80)));   // the lot corner is cut out

            // A click on the wall itself takes the nearest side; no wall touching = the ring itself.
            Assert.NotNull(FillWalls.ClipToSeed(ring, walls, new float2(50, 46), out _));
            Assert.Same(ring, FillWalls.ClipToSeed(ring, new List<float2[]> { lot.Select(p => p + 500f).ToArray() }, new float2(50, 10), out _));
        }

        [Fact]
        public void WallKinds_AndJson()
        {
            Assert.True(FillWalls.Counts(FillWall.Lots, BrushObstacleKind.Building));
            Assert.True(FillWalls.Counts(FillWall.Lots, BrushObstacleKind.CitymakerBuild));
            Assert.False(FillWalls.Counts(FillWall.Lots, BrushObstacleKind.ZonedCell));
            Assert.True(FillWalls.Counts(FillWall.Zoned, BrushObstacleKind.ZonedCell));
            Assert.False(FillWalls.Counts(FillWalls.All, BrushObstacleKind.EmptyZoneCell));

            var d = BrushRules.Defaults();
            Assert.Equal(FillWalls.Default, d.BucketWalls);
            Assert.True(FillWalls.Has(d.BucketWalls, FillWall.Paths));
            Assert.False(FillWalls.Has(d.BucketWalls, FillWall.Lots));
            var r = UiModels.ParseBrushRules("{\"walls\":{\"paths\":false,\"zoned\":true}}", d);
            Assert.Equal(FillWalls.Default & ~FillWall.Paths | FillWall.Zoned, r.BucketWalls);
            Assert.False(r.Equals(d));
            var json = UiModels.BrushRulesJson(r).ToString();
            Assert.Contains("\"walls\"", json);
            var back = UiModels.ParseBrushRules(json, BrushRules.Defaults());
            Assert.Equal(r.BucketWalls, back.BucketWalls);
        }

        // ───────────── Road nodes: closing an open chain ─────────────

        private sealed class Grid : IRoadGraph
        {
            public readonly Dictionary<long, float2> Pos = new Dictionary<long, float2>();
            private readonly Dictionary<long, List<RoadLink>> _links = new Dictionary<long, List<RoadLink>>();
            private long _next = 1000;

            public Grid(int n, params (long, long)[] skip)
            {
                for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) Pos[Id(x, z)] = new float2(x * 100, z * 100);
                for (int z = 0; z < n; z++)
                    for (int x = 0; x < n; x++)
                    {
                        if (x + 1 < n) Connect(Id(x, z), Id(x + 1, z), skip);
                        if (z + 1 < n) Connect(Id(x, z), Id(x, z + 1), skip);
                    }
            }

            public static long Id(int x, int z) => x + 10 * z + 1;

            private void Connect(long a, long b, (long, long)[] skip)
            {
                if (skip.Any(s => (s.Item1 == a && s.Item2 == b) || (s.Item1 == b && s.Item2 == a))) return;
                long e = _next++;
                Add(a, b, e); Add(b, a, e);
            }

            private void Add(long from, long to, long e)
            {
                if (!_links.TryGetValue(from, out var l)) _links[from] = l = new List<RoadLink>();
                var d = Pos[to] - Pos[from];
                l.Add(new RoadLink { Edge = e, Other = to, Direction = math.normalize(d), Length = math.length(d) });
            }

            public IReadOnlyList<RoadLink> Links(long node) => _links.TryGetValue(node, out var l) ? l : new List<RoadLink>();

            public List<HalfEdge> Chain(params long[] nodes)
            {
                var c = new List<HalfEdge>();
                for (int i = 0; i + 1 < nodes.Length; i++)
                {
                    var p = new List<HalfEdge>();
                    Assert.True(RoadGraphAlgorithms.ShortestPath(this, nodes[i], nodes[i + 1], 1000, p));
                    c.AddRange(p);
                }
                return c;
            }
        }

        [Fact]
        public void Closing_FollowsTheRoadWhenShort_ElseAStraightChord()
        {
            var g = new Grid(3);
            // Three sides of the cell (0,0)-(1,1): (0,0) → (1,0) → (1,1) → (0,1); the fourth side is a road.
            long a = Grid.Id(0, 0), b = Grid.Id(1, 0), c = Grid.Id(1, 1), d = Grid.Id(0, 1);
            var nodes = new List<long> { a, b, c, d };
            var chain = g.Chain(a, b, c, d);
            var close = RoadGraphAlgorithms.Closing(g, nodes, chain, n => g.Pos[n], 1000);
            Assert.NotNull(close);
            Assert.Single(close);
            Assert.False(RoadGraphAlgorithms.IsStraight(close[0]));
            Assert.Equal(d, close[0].From);
            Assert.Equal(a, close[0].To);

            // Without the fourth side the only road route is the long way round (> 1.5×): straight chord.
            var g2 = new Grid(3, (a, d));
            var close2 = RoadGraphAlgorithms.Closing(g2, nodes, g2.Chain(a, b, c, d), n => g2.Pos[n], 1000);
            Assert.Single(close2);
            Assert.True(RoadGraphAlgorithms.IsStraight(close2[0]));
            Assert.Equal(d, close2[0].From);
            Assert.Equal(a, close2[0].To);

            // A route that would run back through the chain is never used (here: c's neighbours), nor fewer than 3 nodes.
            var close3 = RoadGraphAlgorithms.Closing(g, new List<long> { a, b, c }, g.Chain(a, b, c), n => g.Pos[n], 1000);
            Assert.True(close3.All(h => h.To != b && h.Edge != chain[0].Edge));
            Assert.Null(RoadGraphAlgorithms.Closing(g, new List<long> { a, b }, g.Chain(a, b), n => g.Pos[n], 1000));
            Assert.Null(RoadGraphAlgorithms.Closing(g, new List<long> { a, b, a }, null, n => g.Pos[n], 1000));
        }

        [Fact]
        public void Closing_DiagonalGap_LikeThePlaytestScreenshot()
        {
            // Chain along the west, north and part of the east side of a 2×2-cell area; the last node is at a junction
            // and the first one diagonally across a field: the road way round is far longer than the chord.
            var g = new Grid(3, (Grid.Id(1, 0), Grid.Id(2, 0)), (Grid.Id(2, 0), Grid.Id(2, 1)), (Grid.Id(1, 0), Grid.Id(1, 1)), (Grid.Id(1, 1), Grid.Id(2, 1)));
            long first = Grid.Id(1, 0);
            var nodes = new List<long> { first, Grid.Id(0, 0), Grid.Id(0, 1), Grid.Id(0, 2), Grid.Id(1, 2), Grid.Id(2, 2), Grid.Id(2, 1) };
            var chain = g.Chain(nodes.ToArray());
            var close = RoadGraphAlgorithms.Closing(g, nodes, chain, n => g.Pos[n], 1000);
            Assert.Single(close);
            Assert.True(RoadGraphAlgorithms.IsStraight(close[0]));
            // The closed ring (chain + chord) is a simple polygon.
            var ring = nodes.Select(n => g.Pos[n]).ToArray();
            Assert.True(Poly.IsSimple(ring));
            Assert.True(Poly.Area(ring) > 30000f);
        }
    }
}
